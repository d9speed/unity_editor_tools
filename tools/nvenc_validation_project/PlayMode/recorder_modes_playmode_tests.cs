using System;
using System.Collections;
using System.IO;
using D9speed.NvencGpu;
using D9speed.Recording;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace D9speed.PackageValidation
{
    public sealed class recorder_modes_playmode_tests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator camera_input_keeps_game_view_aspect()
        {
            string ffmpeg = Environment.GetEnvironmentVariable("D9_NVENC_FFMPEG");
            Assert.That(File.Exists(ffmpeg), Is.True, "FFmpeg is required for validation");
            string directory = Path.GetFullPath("Logs/nvenc_camera_aspect/" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(directory);

            var camera_object = new GameObject("aspect_camera_check");
            var camera = camera_object.AddComponent<Camera>();
            camera.enabled = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.8f, 0.1f, 0.2f, 1f);
            camera_object.tag = "MainCamera";
            camera.ResetAspect();
            float original_aspect = camera.aspect;
            Assert.That(original_aspect, Is.GreaterThan(0f));
            int original_command_count = camera.GetCommandBuffers(CameraEvent.AfterEverything).Length;
            bool source_redirected = false;
            Camera.CameraCallback on_pre_cull = current =>
            {
                if (current != camera || current.targetTexture == null) return;
                source_redirected = true;
                current.aspect = (float)current.targetTexture.width / current.targetTexture.height;
            };
            Camera.onPreCull += on_pre_cull;

            var recorder = new GameObject("aspect_recorder_check").AddComponent<gpu_camera_recorder>();
            recorder.target_camera = camera;
            recorder.offline_mode = true;
            recorder.max_frames = 3;
            recorder.options = new gpu_recorder_options { width = 256, height = 256, fps = 30,
                ffmpeg_path = ffmpeg };
            for (int take = 0; take < 3; take++)
            {
                camera.enabled = take != 2;
                recorder.options.output_path = Path.Combine(directory, "camera_" + take + ".mp4");
                recorder.begin();
                yield return wait_until(() => !recorder.is_recording && recorder.mux_task.IsCompleted);
                recorder.mux_task.GetAwaiter().GetResult();
                Assert.That(recorder.session.requested_frames, Is.EqualTo(3));
                Assert.That(File.Exists(recorder.options.output_path), Is.True);
                Assert.That(camera.targetTexture, Is.Null);
                Assert.That(camera.aspect, Is.EqualTo(original_aspect).Within(0.001f));
                Assert.That(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length, Is.EqualTo(original_command_count));
            }
            camera.enabled = true;
            Camera.onPreCull -= on_pre_cull;
            Assert.That(source_redirected, Is.False, "Recorder redirected the Game View camera");

            UnityEngine.Object.Destroy(recorder.gameObject);
            UnityEngine.Object.Destroy(camera_object);
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator single_png_and_video_frame_limits()
        {
            string ffmpeg = Environment.GetEnvironmentVariable("D9_NVENC_FFMPEG");
            Assert.That(File.Exists(ffmpeg), Is.True, "FFmpeg is required for validation");
            string directory = Path.GetFullPath("Logs/nvenc_mode_checks/" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(directory);

            var source = new RenderTexture(65, 33, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Assert.That(source.Create(), Is.True);
            var pattern = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pattern.SetPixel(0, 0, new Color(1f, 0f, 0f, 0.5f)); pattern.Apply();
            Graphics.Blit(pattern, source);
            UnityEngine.Object.Destroy(pattern);

            bool png_done = false;
            Exception png_error = null;
            string png_path = Path.Combine(directory, "single.png");
            var png = new GameObject("single_png_check").AddComponent<single_frame_png_capture>();
            png.source_texture = source; png.width = 65; png.height = 33; png.alpha = true;
            png.output_path = png_path;
            png.finished += (_, error) => { png_error = error; png_done = true; };
            png.begin();
            yield return wait_until(() => png_done);
            Assert.That(png_error, Is.Null);
            Assert.That(File.Exists(png_path), Is.True);
            var decoded = new Texture2D(2, 2);
            Assert.That(decoded.LoadImage(File.ReadAllBytes(png_path)), Is.True);
            Assert.That(decoded.width, Is.EqualTo(65));
            Assert.That(decoded.height, Is.EqualTo(33));
            Assert.That(decoded.GetPixel(20, 10).a, Is.GreaterThan(0.45f));
            UnityEngine.Object.Destroy(decoded);

            var hevc = new GameObject("bounded_hevc_check").AddComponent<gpu_camera_recorder>();
            hevc.source_texture = source; hevc.offline_mode = true; hevc.max_frames = 3;
            hevc.options = new gpu_recorder_options { width = 256, height = 256, fps = 30,
                ffmpeg_path = ffmpeg, output_path = Path.Combine(directory, "bounded.mp4") };
            hevc.begin();
            yield return wait_until(() => !hevc.is_recording && hevc.mux_task.IsCompleted);
            hevc.mux_task.GetAwaiter().GetResult();
            Assert.That(hevc.session.requested_frames, Is.EqualTo(3));
            Assert.That(hevc.session.stats.written, Is.EqualTo(3));
            Assert.That(File.Exists(hevc.options.output_path), Is.True);

            bool prores_done = false;
            int prores_code = int.MinValue;
            var prores = new GameObject("bounded_prores_check").AddComponent<AlphaCaptureRecorder>();
            prores.Finished += (code, _, __) => { prores_code = code; prores_done = true; };
            string movie = Path.Combine(directory, "bounded.mov");
            prores.StartRecording(new AlphaCaptureRecorder.Config { source_texture = source,
                width = 64, height = 32, fps = 30, max_frames = 4, alpha = true,
                offline_mode = true, max_queue = 4 }, ffmpeg,
                prores_encoding.arguments(64, 32, 30, false, movie), movie);
            yield return wait_until(() => prores_done);
            Assert.That(prores_code, Is.Zero, prores.LastError);
            Assert.That(prores.FramesPushed, Is.EqualTo(4));
            Assert.That(File.Exists(movie), Is.True);
            Assert.That(Directory.GetFiles(directory, "*.json"), Is.Empty);
            Assert.That(Directory.GetFiles(directory, "*.log"), Is.Empty);

            UnityEngine.Object.Destroy(hevc.gameObject);
            UnityEngine.Object.Destroy(prores.gameObject);
            source.Release(); UnityEngine.Object.Destroy(source);
        }

        static IEnumerator wait_until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for recorder");
        }
    }
}
