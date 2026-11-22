using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace D9speed.NvencGpu.Editor
{
    internal sealed class capture_range_preview : IDisposable
    {
        internal Texture2D image { get; private set; }
        internal bool waiting => commands != null;
        RenderTexture camera_target;
        Camera attached_camera;
        CommandBuffer commands;
        bool camera_rendered, video;
        double deadline;
        int sample_width, sample_height;

        internal void capture(Camera camera, RenderTexture texture, int width, int height, bool png, bool alpha)
        {
            Dispose();
            video = !png;
            if (width < 1 || height < 1 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                throw new ArgumentException("出力幅と高さをサポート範囲内に設定してください。");
            float scale = Mathf.Min(1f, 512f / Mathf.Max(width, height));
            sample_width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            sample_height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            try
            {
                if (texture != null)
                {
                    if (!texture.IsCreated()) throw new InvalidOperationException("入力RenderTextureの描画を開始してください。");
                    read_frame(texture);
                    return;
                }
                if (camera == null) throw new InvalidOperationException("カメラまたはRenderTextureを指定してください。");
                if (GraphicsSettings.currentRenderPipeline != null)
                    throw new NotSupportedException("URP/HDRPでは描画済みRenderTextureを指定してください。");
                int samples = video && alpha ? Mathf.Max(1, QualitySettings.antiAliasing) : 1;
                camera_target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, samples);
                if (png || alpha)
                    camera_capture_rendering.render_offscreen(camera, camera_target, alpha, alpha);
                else if (camera.isActiveAndEnabled)
                {
                    // HEVC copies the selected camera's composited target after its normal render.
                    attached_camera = camera;
                    commands = camera_capture_rendering.attach_camera_copy(camera, camera_target);
                    Camera.onPostRender += on_camera_rendered;
                    deadline = EditorApplication.timeSinceStartup + 3;
                    return;
                }
                else
                {
                    var copy = new GameObject("d9_preview_camera") { hideFlags = HideFlags.HideAndDontSave };
                    try { camera_capture_rendering.render_fallback(camera, camera_target, copy.AddComponent<Camera>()); }
                    finally { Object.DestroyImmediate(copy); }
                }
                read_frame(camera_target);
            }
            finally { if (!waiting) release_camera(); }
        }

        void on_camera_rendered(Camera camera)
        {
            if (camera == attached_camera) camera_rendered = true;
        }

        internal void poll()
        {
            if (!waiting) return;
            if (!camera_rendered && attached_camera != null && EditorApplication.timeSinceStartup < deadline) return;
            try
            {
                if (!camera_rendered)
                    throw new InvalidOperationException("カメラの描画を待っています。Game Viewを表示して、もう一度取得してください。");
                read_frame(camera_target);
            }
            finally { release_camera(); }
        }

        void read_frame(RenderTexture source)
        {
            var previous = RenderTexture.active;
            var sample = RenderTexture.GetTemporary(sample_width, sample_height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, sample);
                image = new Texture2D(sample_width, sample_height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (video)
                {
                    // FFmpeg/NVENC consume native rows from the top. Texture2D UV starts at the bottom.
                    var readback = AsyncGPUReadback.Request(sample, 0, TextureFormat.RGBA32);
                    readback.WaitForCompletion();
                    if (readback.hasError) throw new InvalidOperationException("プレビュー画像をGPUから取得できませんでした。");
                    image.LoadRawTextureData(readback.GetData<byte>());
                }
                else
                {
                    RenderTexture.active = sample;
                    image.ReadPixels(new Rect(0, 0, sample_width, sample_height), 0, 0, false);
                }
                image.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(sample);
            }
        }

        internal Rect image_uv(bool flip_vertical) => video != flip_vertical ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);

        void release_camera()
        {
            Camera.onPostRender -= on_camera_rendered;
            if (attached_camera != null && commands != null)
                attached_camera.RemoveCommandBuffer(CameraEvent.AfterEverything, commands);
            commands?.Release();
            commands = null;
            attached_camera = null;
            camera_rendered = false;
            if (camera_target != null) RenderTexture.ReleaseTemporary(camera_target);
            camera_target = null;
        }

        public void Dispose()
        {
            release_camera();
            if (image != null) Object.DestroyImmediate(image);
            image = null;
        }
    }
}
