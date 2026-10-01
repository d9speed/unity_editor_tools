using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace D9speed.NvencGpu
{
    [AddComponentMenu("D9speed/Recorder/NVENC GPU Recorder")]
    public sealed class gpu_camera_recorder : MonoBehaviour
    {
        public Camera target_camera;
        public RenderTexture source_texture;
        public gpu_recorder_options options = new gpu_recorder_options();
        [Tooltip("ゲーム時間を録画FPSに固定します。実時間の録画ではオフにしてください。")]
        public bool offline_mode;
        public int max_frames;
        public gpu_recorder_session session { get; private set; }
        public bool is_recording { get; private set; }
        public string last_error { get; private set; }
        public Task mux_task => session?.mux_task ?? Task.CompletedTask;
        Coroutine loop;
        RenderTexture camera_rt;
        Camera camera_capture_source;
        Camera fallback_camera;
        CommandBuffer camera_capture_commands;
        int previous_capture;
        bool owns_capture_clock;

        public void begin()
        {
            if (is_recording || (session != null && !session.is_closed)) throw new InvalidOperationException("既に録画中です。");
            if (source_texture == null && target_camera == null) throw new InvalidOperationException("カメラまたはRenderTextureを指定してください。");
            if (source_texture == null && GraphicsSettings.currentRenderPipeline != null)
                throw new NotSupportedException("URP/HDRPでは描画済みRenderTextureを指定してください。");
            try
            {
                if (source_texture == null)
                {
                    camera_capture_source = target_camera;
                    camera_rt = new RenderTexture(options.width,options.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                        {name="nvenc_camera_target",hideFlags=HideFlags.HideAndDontSave};
                    if (!camera_rt.Create()) throw new InvalidOperationException("録画用RenderTextureを作成できませんでした。");
                    camera_capture_commands = new CommandBuffer { name = "d9_nvenc_camera_copy" };
                    camera_capture_commands.Blit(BuiltinRenderTextureType.CameraTarget, camera_rt);
                    camera_capture_source.AddCommandBuffer(CameraEvent.AfterEverything, camera_capture_commands);
                }
                session = new gpu_recorder_session(options);
            }
            catch { release_camera(); throw; }
            previous_capture = Time.captureFramerate; owns_capture_clock = offline_mode;
            if (owns_capture_clock) Time.captureFramerate = options.fps;
            last_error = ""; is_recording = true; loop = StartCoroutine(capture_loop());
        }
        IEnumerator capture_loop()
        {
            var end_of_frame = new WaitForEndOfFrame();
            double next = Time.realtimeSinceStartupAsDouble;
            while (is_recording)
            {
                yield return end_of_frame;
                double now = Time.realtimeSinceStartupAsDouble;
                if (!offline_mode && now + .0001 < next) continue;
                if (!offline_mode) next = Math.Max(next + 1.0/options.fps,now);
                try
                {
                    var source = source_texture;
                    if (source == null)
                    {
                        if (camera_capture_source == null) throw new InvalidOperationException("録画カメラが破棄されました。");
                        if (!camera_capture_source.isActiveAndEnabled) render_fallback_camera();
                        source = camera_rt;
                    }
                    session.capture(source);
                    if (max_frames > 0 && session.requested_frames >= max_frames)
                    {
                        stop();
                        yield break;
                    }
                }
                catch (Exception e) { last_error = e.Message; Debug.LogError("[NVENC GPU] " + last_error); is_recording = false; }
                if (!is_recording) { stop(); yield break; }
            }
        }
        public void stop()
        {
            is_recording = false;
            if (loop != null) { StopCoroutine(loop); loop = null; }
            try { session?.stop(); }
            finally
            {
                if (owns_capture_clock) { Time.captureFramerate = previous_capture; owns_capture_clock = false; }
                release_camera();
            }
            if (session != null && !string.IsNullOrEmpty(session.error)) last_error = session.error;
        }
        void release_camera()
        {
            if (camera_capture_source != null && camera_capture_commands != null)
                camera_capture_source.RemoveCommandBuffer(CameraEvent.AfterEverything, camera_capture_commands);
            camera_capture_commands?.Release();
            camera_capture_commands = null;
            camera_capture_source = null;
            if (fallback_camera != null)
            {
                if (Application.isPlaying) Destroy(fallback_camera.gameObject); else DestroyImmediate(fallback_camera.gameObject);
                fallback_camera = null;
            }
            if (camera_rt == null) return;
            camera_rt.Release();
            if (Application.isPlaying) Destroy(camera_rt); else DestroyImmediate(camera_rt);
            camera_rt = null;
        }
        void render_fallback_camera()
        {
            if (fallback_camera == null)
            {
                var fallback_object = new GameObject("d9_nvenc_fallback_camera") { hideFlags = HideFlags.HideAndDontSave };
                fallback_camera = fallback_object.AddComponent<Camera>();
            }
            fallback_camera.CopyFrom(camera_capture_source);
            fallback_camera.enabled = false;
            fallback_camera.targetTexture = camera_rt;
            fallback_camera.transform.SetPositionAndRotation(camera_capture_source.transform.position, camera_capture_source.transform.rotation);
            fallback_camera.Render();
        }
        void OnDisable() { stop(); }
        void OnDestroy() { stop(); }
    }
}
