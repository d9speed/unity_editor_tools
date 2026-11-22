using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace D9speed.NvencGpu
{
    [AddComponentMenu("")]
    public sealed class single_frame_png_capture : MonoBehaviour
    {
        public Camera target_camera;
        public RenderTexture source_texture;
        public bool capture_game_view;
        public int width, height;
        public bool alpha, flip_vertical;
        public string output_path;

        public event Action<string, Exception> finished;

        public void begin()
        {
            bool use_camera = !capture_game_view && source_texture == null;
            if (use_camera && target_camera == null)
                throw new ArgumentException("カメラまたはRenderTextureを指定してください。");
            if (use_camera && GraphicsSettings.currentRenderPipeline != null)
                throw new NotSupportedException("URP/HDRPでは描画済みRenderTextureを指定してください。");
            if (width < 1 || height < 1) throw new ArgumentException("PNGの幅・高さは1以上にしてください。");
            if (File.Exists(output_path)) throw new IOException("出力ファイルが既に存在します: " + output_path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output_path)));
            StartCoroutine(capture());
        }

        IEnumerator capture()
        {
            yield return new WaitForEndOfFrame();
            Exception error = null;
            try { save_png(); }
            catch (Exception e) { error = e; }
            finished?.Invoke(output_path, error);
            Destroy(gameObject);
        }

        void save_png()
        {
            RenderTexture camera_rt = null;
            RenderTexture output_rt = null;
            Texture2D texture = null;
            var previous_active = RenderTexture.active;
            try
            {
                using var frame = capture_game_view ? game_view_frame.capture() : default;
                RenderTexture source = capture_game_view ? frame.texture : source_texture;
                if (source == null)
                {
                    if (target_camera == null) throw new InvalidOperationException("録画対象カメラが失われました。");
                    camera_rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    camera_capture_rendering.render_offscreen(target_camera, camera_rt, alpha, alpha);
                    source = camera_rt;
                }
                if (!source.IsCreated()) throw new InvalidOperationException("入力RenderTextureが作成されていません。");

                output_rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                // ScreenCapture supplies native screen rows; PNG ReadPixels uses bottom-left coordinates.
                bool screen_starts_at_top = capture_game_view && SystemInfo.graphicsUVStartsAtTop;
                if (flip_vertical != screen_starts_at_top)
                    Graphics.Blit(source, output_rt, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else
                    Graphics.Blit(source, output_rt);

                RenderTexture.active = output_rt;
                texture = new Texture2D(width, height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false);
                File.WriteAllBytes(output_path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous_active;
                if (texture != null) Destroy(texture);
                if (output_rt != null) RenderTexture.ReleaseTemporary(output_rt);
                if (camera_rt != null) RenderTexture.ReleaseTemporary(camera_rt);
            }
        }
    }
}
