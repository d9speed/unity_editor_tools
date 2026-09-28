using System;
using System.IO;
using System.Reflection;
using UnityEditor.PackageManager;
using UnityEngine;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    [Serializable]
    internal sealed class guide_board_settings
    {
        public string text = "SETUP GUIDE\n\n説明文をここに入力します。";
        public string font_name = string.Empty;
    }

    internal sealed class guide_board_preview : IDisposable
    {
        private const string package_path = "Packages/io.github.d9speed.editor_core";
        private static MethodInfo render_method;
        private static MethodInfo font_list_method;
        private static FieldInfo png_field;
        private static FieldInfo overflow_field;

        private Texture2D preview_texture;
        private byte[] png;

        public Texture texture => preview_texture;
        public int render_count { get; private set; }
        public bool text_overflows { get; private set; }

        public void render(guide_board_settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ensure_renderer();

            object result;
            try
            {
                result = render_method.Invoke(null, new object[] { settings.text ?? string.Empty, settings.font_name });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("説明画像を生成できませんでした: " + exception.InnerException?.Message,
                    exception.InnerException ?? exception);
            }

            var next_png = (byte[])png_field.GetValue(result);
            if (next_png == null || next_png.Length == 0)
                throw new InvalidOperationException("描画DLLが空の画像を返しました。");
            var next_texture = new Texture2D(2, 2, TextureFormat.RGB24, false, false)
            {
                name = "guide_board_preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                if (!next_texture.LoadImage(next_png))
                    throw new InvalidOperationException("描画DLLから受け取ったPNGを読み込めませんでした。");
                if (next_texture.width != 1024 || next_texture.height != 512)
                    throw new InvalidOperationException("描画DLLが予期しない解像度を返しました。");
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(next_texture);
                throw;
            }

            if (preview_texture != null) UnityEngine.Object.DestroyImmediate(preview_texture);
            preview_texture = next_texture;
            png = next_png;
            text_overflows = (bool)overflow_field.GetValue(result);
            render_count++;
        }

        public byte[] encode_png()
        {
            if (png == null) throw new InvalidOperationException("プレビューを先に生成してください。");
            return (byte[])png.Clone();
        }

        public static string[] get_font_families()
        {
            ensure_renderer();
            try
            {
                return (string[])font_list_method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("Windowsのフォント一覧を取得できませんでした: " + exception.InnerException?.Message,
                    exception.InnerException ?? exception);
            }
        }

        private static void ensure_renderer()
        {
            if (render_method != null) return;
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException("説明画像の生成はWindows版Unity Editor専用です。");

            var package = PackageInfo.FindForAssetPath(package_path + "/package.json");
            var package_directory = package != null ? package.resolvedPath : Path.GetFullPath(package_path);
            var dll_path = Path.Combine(package_directory, "Renderer~", "guide_board_renderer.dll");
            if (!File.Exists(dll_path))
                throw new FileNotFoundException("説明画像の描画DLLが見つかりません。Editor Coreを再導入してください。", dll_path);

            var assembly = Assembly.Load(File.ReadAllBytes(dll_path));
            var renderer_type = assembly.GetType("D9speed.GuideBoardRenderer.Renderer", true);
            var result_type = assembly.GetType("D9speed.GuideBoardRenderer.RenderedImage", true);
            var method = renderer_type.GetMethod("RenderText", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string) }, null);
            var list_method = renderer_type.GetMethod("GetFontFamilies", BindingFlags.Public | BindingFlags.Static);
            var image_field = result_type.GetField("Png", BindingFlags.Public | BindingFlags.Instance);
            var clipped_field = result_type.GetField("Overflow", BindingFlags.Public | BindingFlags.Instance);
            if (method == null || list_method == null || image_field == null || clipped_field == null)
                throw new MissingMemberException("説明画像の描画DLLがEditor Coreと互換性がありません。");
            render_method = method;
            font_list_method = list_method;
            png_field = image_field;
            overflow_field = clipped_field;
        }

        public void Dispose()
        {
            if (preview_texture != null) UnityEngine.Object.DestroyImmediate(preview_texture);
            preview_texture = null;
            png = null;
        }
    }
}
