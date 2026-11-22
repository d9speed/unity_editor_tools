using System;
using UnityEngine;

namespace D9speed.NvencGpu
{
    internal readonly struct game_view_frame : IDisposable
    {
        internal readonly RenderTexture texture;
        game_view_frame(RenderTexture texture) { this.texture = texture; }

        // Called after WaitForEndOfFrame, including Screen Space - Overlay UI.
        internal static game_view_frame capture()
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                return new game_view_frame(target);
            }
            catch { RenderTexture.ReleaseTemporary(target); throw; }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (texture != null) RenderTexture.ReleaseTemporary(texture);
        }
    }
}
