using System;
using System.IO;
using D9speed.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace D9speed.NvencGpu.Editor
{
    public static class prores_validation
    {
        public static string run(bool prefer_vulkan, bool camera_input = false, int frames = 12, string executable = null)
        {
            const int width = 640, height = 360, fps = 30;
            var prepared = gpu_recorder_window.prepare_alpha(executable ?? D9speed.Recording.Editor.FfmpegEnvProbe.FindOnPath(), width, height, prefer_vulkan);
            string prefix = (prepared.vulkan ? "prores_vulkan_" : "prores_cpu_") + (camera_input ? "camera_" : "rt_");
            string output = Path.Combine(D9speed.PackageValidation.nvenc_package_smoke_checks.output_root, prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".mov");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            int clock = Time.captureFramerate;
            var scene = EditorSceneManager.NewPreviewScene();
            var source = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            source.Create();
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            var go = new GameObject("alpha_validation"); SceneManager.MoveGameObjectToScene(go, scene);
            var recorder = go.AddComponent<AlphaCaptureRecorder>();
            Camera camera = null; Material material = null;
            if (camera_input)
            {
                camera = go.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.cameraType = CameraType.Preview;
                camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = (float)width / height;
                camera.transform.position = new Vector3(0, 0, -10); camera.backgroundColor = Color.magenta;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = true;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); SceneManager.MoveGameObjectToScene(quad, scene);
                quad.transform.localScale = new Vector3(6, 3, 1);
                material = new Material(Shader.Find("Unlit/Texture")); material.mainTexture = texture;
                quad.GetComponent<Renderer>().sharedMaterial = material;
            }
            try
            {
                recorder.StartRecording(new AlphaCaptureRecorder.Config {
                    target_camera = camera, source_texture = camera_input ? null : source, width = width, height = height, fps = fps,
                    alpha = true, offline_mode = true, manual_capture = true, force_ldr = true, flip_vertical = true, max_queue = 4
                }, prepared.executable, prores_encoding.arguments(width, height, fps, prepared.vulkan, output), output);
                for (int frame = 0; frame < frames; frame++)
                {
                    var pixels = new Color32[width * height];
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    {
                        byte alpha = x < width / 4 ? (byte)0 : x < width / 2 ? (byte)64 : x < 3 * width / 4 ? (byte)128 : (byte)255;
                        pixels[y * width + x] = new Color32((byte)(y < height / 2 ? 210 : 50), (byte)(100 + frame % 40 * 3), (byte)(y < height / 2 ? 40 : 220), alpha);
                    }
                    texture.SetPixels32(pixels); texture.Apply(); Graphics.Blit(texture, source);
                    if (frame == 0)
                    {
                        if (camera_input)
                        {
                            camera.targetTexture = source; camera.backgroundColor = Color.clear; camera.allowHDR = false; camera.Render();
                            camera.targetTexture = null; camera.backgroundColor = Color.magenta; camera.allowHDR = true;
                        }
                        save_reference(source, output + ".reference.png");
                    }
                    recorder.CaptureFrame();
                }
                recorder.StopRecording();
                bool camera_restored = camera == null || camera.targetTexture == null && camera.backgroundColor == Color.magenta && camera.allowHDR;
                if (recorder.ExitCode != 0 || recorder.FramesPushed != frames || recorder.FramesDropped != 0 || Time.captureFramerate != clock || !camera_restored)
                    throw new InvalidOperationException("ProRes validation failed: " + recorder.LastError);
                string png = prores_encoding.export_png(prepared.executable, output, prepared.vulkan).GetAwaiter().GetResult();
                File.WriteAllText(output + ".alpha_test.json", JsonUtility.ToJson(new report {
                    unity = Application.unityVersion, output = output, png_directory = png, vulkan = prepared.vulkan,
                    camera = camera_input, frames = frames, fps = fps, width = width, height = height,
                    raw_readback_bytes = recorder.RawReadbackBytes, clock_restored = Time.captureFramerate == clock, camera_restored = camera_restored
                }, true));
                return output;
            }
            finally
            {
                recorder.StopRecording();
                if (material != null) Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture); source.Release(); Object.DestroyImmediate(source);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static void save_reference(RenderTexture source, string path)
        {
            var prev = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { RenderTexture.active = prev; Object.DestroyImmediate(texture); }
        }
        [Serializable] sealed class report
        {
            public string unity, output, png_directory;
            public bool vulkan, camera, clock_restored, camera_restored;
            public int frames, fps, width, height;
            public long raw_readback_bytes;
        }
        public static void batch_run()
        {
            try { run(true, false, 120); run(false); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
