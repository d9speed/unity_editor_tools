using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using D9speed.NvencGpu;
using D9speed.NvencGpu.Editor;
using D9speed.Recording;
using D9speed.Recording.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    // Run only in a dedicated validation project with a real D3D11 graphics device.
    public static class nvenc_package_smoke_checks
    {
        public static readonly string output_root = Path.GetFullPath("Logs/nvenc_smoke");
        static readonly List<string> checks = new List<string>();
        static string ffmpeg;

        public static void Run()
        {
            Directory.CreateDirectory(output_root);
            try
            {
                ffmpeg = Environment.GetEnvironmentVariable("D9_NVENC_FFMPEG") ?? FfmpegEnvProbe.FindOnPath();
                require(File.Exists(ffmpeg), "External FFmpeg is required for validation");
                check("Package registration, native importer and Runtime/Editor separation", package_layout);
                check("Existing recorder menu opens", () => {
                    require(EditorApplication.ExecuteMenuItem("D9speed/Recorder/FFmpeg Recorder"), "Recorder menu");
                    foreach (var window in Resources.FindObjectsOfTypeAll<gpu_recorder_window>()) window.Close();
                });
                check("Missing FFmpeg and invalid dimensions are rejected", invalid_settings);
                check("HEVC 1080p60, 180 frames, native release and MP4 mux", record_hevc);
                check("ProRes CPU from Camera, alpha PNG, clock/camera restoration", () =>
                    prores_validation.run(false, true, 12, ffmpeg));
                check("ProRes Vulkan preference from RenderTexture, alpha PNG and cleanup", () =>
                    prores_validation.run(true, false, 12, ffmpeg));
                check("No active capture sessions remain", () => {
                    require(gpu_recorder_session.active_sessions == 0 && AlphaCaptureRecorder.ActiveSessions == 0, "Leaked session");
                });
                save(true, "");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                save(false, e.ToString());
                EditorApplication.Exit(1);
            }
        }

        static void package_layout()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Single(p => p.name == "io.github.d9speed.nvenc_gpu_recorder");
            require(package.version == "0.1.0", "Package version");
            require(!Directory.GetFiles(package.resolvedPath, "*.exe", SearchOption.AllDirectories).Any(), "Bundled executable");
            require(!Directory.Exists(Path.Combine(package.resolvedPath, "results~")), "Bundled recording results");
            var dll = Directory.GetFiles(Path.Combine(package.resolvedPath, "Plugins/x86_64"), "*.dll").Single();
            string plugin_path = "Packages/" + package.name + "/Plugins/x86_64/" + Path.GetFileName(dll);
            var importer = (PluginImporter)AssetImporter.GetAtPath(plugin_path);
            require(importer != null && !importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithEditor(), "Plugin importer");
            require(importer.GetEditorData("OS") == "Windows" && importer.GetEditorData("CPU") == "x86_64", "Editor DLL platform");
            require(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), "Windows x64 DLL");
            require(!importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows)
                && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64)
                && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), "Unsupported DLL platform");
            var player = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            require(player.Any(a => a.name == "D9speed.NvencGpu.Runtime"), "Missing Runtime assembly");
            require(!player.Any(a => a.name == "D9speed.NvencGpu.Editor"), "Editor included in Player");
        }

        static void invalid_settings()
        {
            bool rejected = false;
            try { gpu_recorder_window.prepare_alpha(Path.Combine(output_root, "missing_ffmpeg.exe"), 640, 360, true); }
            catch (FileNotFoundException) { rejected = true; }
            require(rejected, "Missing FFmpeg accepted");
            rejected = false;
            try { using var session = new gpu_recorder_session(new gpu_recorder_options { width = 17 }); }
            catch (ArgumentException) { rejected = true; }
            require(rejected, "Invalid width accepted");
        }

        static void record_hevc()
        {
            const int width = 1920, height = 1080, frames = 180, fps = 60;
            string output = Path.Combine(output_root, "hevc_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".mp4");
            var source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pattern = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Point };
            require(source.Create(), "RenderTexture creation");
            var settings = new gpu_recorder_options { width = width, height = height, fps = fps,
                bitrate = 8000000, cq = 0, ffmpeg_path = ffmpeg, output_path = output };
            try
            {
                using (var session = new gpu_recorder_session(settings))
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        pattern.SetPixels(new[] { Color.blue, Color.white, Color.red, new Color(0, .5f + .4f * frame / frames, 0) });
                        pattern.Apply(); Graphics.Blit(pattern, source);
                        if (frame == 0) save_reference(source, output + ".reference.png");
                        session.capture(source); GL.Flush();
                        if (frame % 30 == 0) Thread.Sleep(1);
                    }
                    session.stop();
                    require(session.mux_task.Wait(TimeSpan.FromSeconds(65)), "MP4 mux timeout");
                    session.mux_task.GetAwaiter().GetResult();
                    var stats = session.stats;
                    require(stats.state == 3 && stats.captured == frames && stats.encoded == frames && stats.written == frames, "Incomplete HEVC frames");
                    require(stats.raw_readback_bytes == 0 && stats.gpu_copies == frames, "Unexpected HEVC readback");
                    require(session.is_closed && string.IsNullOrEmpty(session.error) && File.Exists(output), "Session did not finish");
                    // The source belongs to the caller, even after disposing the native session.
                    require(source.IsCreated(), "Source RenderTexture was released");
                }
                bool rejected = false;
                try { using var duplicate = new gpu_recorder_session(settings); }
                catch (IOException) { rejected = true; }
                require(rejected, "Existing output accepted");
            }
            finally { source.Release(); Object.DestroyImmediate(source); Object.DestroyImmediate(pattern); }
        }

        static void save_reference(RenderTexture source, string path)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            try { RenderTexture.active = source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }

        static void check(string name, Action body) { body(); checks.Add(name); Debug.Log("[NVENC package] PASS " + name); }
        static void require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void save(bool passed, string error) => File.WriteAllText(Path.Combine(output_root, "smoke_results.json"),
            JsonUtility.ToJson(new report { passed = passed, unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                graphics_api = SystemInfo.graphicsDeviceType.ToString(), checks = checks.ToArray(), error = error }, true));
        [Serializable] sealed class report { public bool passed; public string unity, gpu, graphics_api, error; public string[] checks; }
    }
}
