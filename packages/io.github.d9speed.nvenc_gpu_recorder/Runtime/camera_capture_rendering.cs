using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("D9speed.NvencGpu.Editor")]

namespace D9speed.NvencGpu
{
    internal static class camera_capture_rendering
    {
        internal static CommandBuffer attach_camera_copy(Camera camera, RenderTexture target)
        {
            var commands = new CommandBuffer { name = "d9_nvenc_camera_copy" };
            commands.Blit(BuiltinRenderTextureType.CameraTarget, target);
            camera.AddCommandBuffer(CameraEvent.AfterEverything, commands);
            return commands;
        }

        internal static void render_fallback(Camera source, RenderTexture target, Camera fallback)
        {
            fallback.CopyFrom(source);
            fallback.enabled = false;
            fallback.targetTexture = target;
            fallback.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            fallback.Render();
        }

        internal static void render_offscreen(Camera camera, RenderTexture target, bool alpha, bool force_ldr)
        {
            var previous_target = camera.targetTexture;
            var previous_flags = camera.clearFlags;
            var previous_background = camera.backgroundColor;
            bool previous_hdr = camera.allowHDR;
            float previous_aspect = camera.aspect;
            try
            {
                camera.targetTexture = target;
                if (alpha)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.clear;
                }
                if (force_ldr) camera.allowHDR = false;
                camera.Render();
            }
            finally
            {
                if (camera != null)
                {
                    camera.targetTexture = previous_target;
                    camera.clearFlags = previous_flags;
                    camera.backgroundColor = previous_background;
                    camera.allowHDR = previous_hdr;
                    if (camera.aspect != previous_aspect) camera.aspect = previous_aspect;
                }
            }
        }
    }
}
