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
        public gpu_recorder_session session { get; private set; }
        public bool is_recording { get; private set; }
        public string last_error { get; private set; }
        public Task mux_task => session?.mux_task ?? Task.CompletedTask;
        Coroutine loop;
        RenderTexture camera_rt;
        int previous_capture;
        bool owns_capture_clock;

        public void begin()
        {
            if (is_recording || (session != null && !session.is_closed)) throw new InvalidOperationException("既に録画中です。");
            if (source_texture == null && target_camera == null) throw new InvalidOperationException("カメラまたはRenderTextureを指定してください。");
            if (source_texture == null && GraphicsSettings.currentRenderPipeline != null)
                throw new NotSupportedException("URP/HDRPでは描画済みRenderTextureを指定してください。");
            if (source_texture == null)
            {
                camera_rt = new RenderTexture(options.width,options.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                    {name="nvenc_camera_target",hideFlags=HideFlags.HideAndDontSave};
                camera_rt.Create();
            }
            try { session = new gpu_recorder_session(options); }
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
                        if (target_camera == null) throw new InvalidOperationException("録画カメラが破棄されました。");
                        var old_target = target_camera.targetTexture;
                        try { target_camera.targetTexture = camera_rt; target_camera.Render(); }
                        finally { if(target_camera != null) target_camera.targetTexture = old_target; }
                        source = camera_rt;
                    }
                    session.capture(source);
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
            if (camera_rt == null) return;
            camera_rt.Release();
            if (Application.isPlaying) Destroy(camera_rt); else DestroyImmediate(camera_rt);
            camera_rt = null;
        }
        void OnDisable() { stop(); }
        void OnDestroy() { stop(); }
    }
}
