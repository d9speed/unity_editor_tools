using System;
using UnityEngine;

namespace D9speed.NvencGpu
{
    internal readonly struct game_view_frame : IDisposable
    {
        internal readonly RenderTexture texture;
        game_view_frame(RenderTexture texture) { this.texture = texture; }

        static game_view_frame temporary(int width, int height) => new game_view_frame(
            RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));

        // Called after WaitForEndOfFrame, including Screen Space - Overlay UI.
        internal static game_view_frame capture()
        {
            var previous = RenderTexture.active;
            var frame = temporary(Screen.width, Screen.height);
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(frame.texture);
                return frame;
            }
            catch { frame.Dispose(); throw; }
            finally { RenderTexture.active = previous; }
        }

        internal static game_view_frame capture_for_video(Vector2Int output_size)
        {
            int width = output_size.x, height = output_size.y;
            int screen_width = Screen.width, screen_height = Screen.height;
            if (screen_width == width && screen_height == height) return capture();
            double scale = Math.Min((double)width / screen_width, (double)height / screen_height);
            int content_width = Math.Max(1, (int)Math.Round(screen_width * scale));
            int content_height = Math.Max(1, (int)Math.Round(screen_height * scale));
            if (content_width == width && content_height == height) return capture();

            using var screen = capture();
            bool resize = content_width != screen_width || content_height != screen_height;
            using var scaled = resize ? temporary(content_width, content_height) : default;
            var previous = RenderTexture.active;
            var output = temporary(width, height);
            try
            {
                if (resize) Graphics.Blit(screen.texture, scaled.texture);
                RenderTexture.active = output.texture;
                GL.Clear(false, true, Color.black);
                RenderTexture.active = previous;
                Graphics.CopyTexture(resize ? scaled.texture : screen.texture, 0, 0, 0, 0, content_width, content_height,
                    output.texture, 0, 0, (width - content_width) / 2, (height - content_height) / 2);
                return output;
            }
            catch { output.Dispose(); throw; }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (texture != null) RenderTexture.ReleaseTemporary(texture);
        }
    }
}
