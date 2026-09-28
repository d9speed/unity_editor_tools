using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace D9speed.GuideBoardRenderer
{
    public sealed class RenderedImage
    {
        public byte[] Png;
        public bool Overflow;
    }

    public static class Renderer
    {
        public static RenderedImage RenderText(string text)
        {
            return RenderText(text, null);
        }

        public static RenderedImage RenderText(string text, string font_name)
        {
            const int width = 1024;
            const int height = 512;
            const int padding = 40;
            var fonts = GetFontFamilies();
            if (fonts.Length == 0) throw new InvalidOperationException("Windowsのシステムフォントを取得できません。");
            if (string.IsNullOrWhiteSpace(font_name))
            {
                font_name = Array.Find(fonts, name =>
                    string.Equals(name, "Meiryo", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "メイリオ", StringComparison.OrdinalIgnoreCase));
                if (font_name == null) font_name = SystemFonts.DefaultFont.FontFamily.Name;
            }
            else
            {
                var selected_font = Array.Find(fonts, name =>
                    string.Equals(name, font_name, StringComparison.OrdinalIgnoreCase));
                if (selected_font == null)
                    throw new ArgumentException("指定フォントがWindowsに見つかりません: " + font_name, "font_name");
                font_name = selected_font;
            }

            int low = 12;
            int high = 48;
            int chosen_size = low;
            using (var probe = new Bitmap(1, 1))
            using (var graphics = Graphics.FromImage(probe))
            using (var format = new StringFormat(StringFormat.GenericDefault))
            {
                while (low <= high)
                {
                    int candidate = (low + high) / 2;
                    using (var font = new Font(font_name, candidate, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        var measured = graphics.MeasureString(text ?? string.Empty, font,
                            new SizeF(width - padding * 2, 100000f), format);
                        if (measured.Height <= height - padding * 2)
                        {
                            chosen_size = candidate;
                            low = candidate + 1;
                        }
                        else high = candidate - 1;
                    }
                }
            }

            return Render(text, font_name, width, height, chosen_size, padding,
                0, Color.FromArgb(255, 20, 20, 20).ToArgb(), Color.White.ToArgb());
        }

        public static string[] GetFontFamilies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fonts = new InstalledFontCollection())
            {
                foreach (var family in fonts.Families) names.Add(family.Name);
            }
            var result = new string[names.Count];
            names.CopyTo(result);
            Array.Sort(result, StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        public static RenderedImage Render(string text, string font_name, int width, int height,
            int font_size, int padding, int alignment, int text_argb, int background_argb)
        {
            if (width < 64 || width > 2048 || height < 64 || height > 2048)
                throw new ArgumentOutOfRangeException("width", "画像の各辺は64〜2048 pxにしてください。");
            if (font_size < 8 || font_size > 256)
                throw new ArgumentOutOfRangeException("font_size", "文字サイズは8〜256 pxにしてください。");
            if (padding < 0 || padding * 2 >= Math.Min(width, height))
                throw new ArgumentOutOfRangeException("padding", "余白が画像サイズを超えています。");
            if (alignment < 0 || alignment > 2)
                throw new ArgumentOutOfRangeException("alignment", "文字揃えが不正です。");
            if (text != null && text.Length > 32768)
                throw new ArgumentException("文章が長すぎます。", "text");
            if (string.IsNullOrWhiteSpace(font_name))
                throw new ArgumentException("Windowsにインストール済みのフォントを選んでください。", "font_name");

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font(font_name, font_size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(text_argb)))
            using (var format = new StringFormat(StringFormat.GenericDefault))
            {
                if (!string.Equals(font.FontFamily.Name, font_name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("指定フォントがWindowsに見つかりません: " + font_name, "font_name");

                graphics.Clear(Color.FromArgb(background_argb));
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.Alignment = alignment == 1 ? StringAlignment.Center :
                    alignment == 2 ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.None;
                var content = text ?? string.Empty;
                var content_width = width - padding * 2;
                var content_height = height - padding * 2;
                var measured = graphics.MeasureString(content, font,
                    new SizeF(content_width, 100000f), format);
                graphics.DrawString(content, font, brush,
                    new RectangleF(padding, padding, content_width, content_height), format);

                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return new RenderedImage
                    {
                        Png = stream.ToArray(),
                        Overflow = measured.Height > content_height + 1f
                    };
                }
            }
        }
    }
}
