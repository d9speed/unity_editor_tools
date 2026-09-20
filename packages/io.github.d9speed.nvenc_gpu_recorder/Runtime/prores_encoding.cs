using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace D9speed.Recording
{
    /// <summary>統合レコーダーの ProRes 設定。上下反転は Unity 側で行う。</summary>
    public static class prores_encoding
    {
        // RGBA のままアップロードする。YUVA upload / vflip / dithering は旧実装で不具合を確認済み。
        // α=1.0 が失われる libplacebo 経路の回避として入力αを254/255相当に制限する。
        public const string vulkan_filter = "colorchannelmixer=aa=0.996,hwupload,libplacebo=format=yuva444p10le:dithering=none";
        // FFmpeg 8.1 / RTX 5090では既定tilingでY面が間欠的に失われる。linear_imagesで回避。
        public const string vulkan_device = "-init_hw_device vulkan=vk:0,linear_images=1 -filter_hw_device vk ";
        public const string vulkan_codec = "-c:v prores_ks_vulkan -profile:v 4444 -alpha_bits 16";
        static readonly List<Task> exports = new List<Task>();

        public static string arguments(int width, int height, int fps, bool vulkan, string output)
        {
            string device = vulkan ? vulkan_device : "";
            string filter = vulkan ? vulkan_filter : "format=yuva444p10le";
            string codec = vulkan ? vulkan_codec : "-c:v prores_ks -profile:v 4444 -alpha_bits 16";
            return $"-hide_banner -n {device}-f rawvideo -pixel_format rgba -video_size {width}x{height} -framerate {fps} -i - " +
                   $"-an -vf \"{filter}\" {codec} {quote(output)}";
        }

        public static string png_arguments(string movie, string directory, bool premultiplied)
        {
            // FFmpeg 8.1 は ProRes の入力を straight と判定することがある。
            // 旧 unpremultiply だけでは変換されないため、実際の入力モードを明示して変換する。
            string filter = premultiplied ? "setparams=alpha_mode=premultiplied,format=rgba:alpha_modes=straight" : "format=rgba";
            return $"-hide_banner -loglevel warning -nostdin -n -i {quote(movie)} -vf \"{filter}\" {quote(Path.Combine(directory, "frame_%05d.png"))}";
        }

        public static Task<string> export_png(string executable, string movie, bool premultiplied)
        {
            // 一度作成した連番フォルダーには書き込まない。
            string directory = Path.Combine(Path.GetDirectoryName(movie), Path.GetFileNameWithoutExtension(movie) + "_png");
            if (Directory.Exists(directory)) throw new IOException("PNG出力先が既に存在します: " + directory);
            Directory.CreateDirectory(directory);
            var task = Task.Run(() =>
            {
                string args = png_arguments(movie, directory, premultiplied);
                using var process = new Process { StartInfo = new ProcessStartInfo(executable, args) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } };
                process.Start();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000))
                {
                    process.Kill(); process.WaitForExit();
                    File.WriteAllText(movie + ".png.log", args + "\n" + stderr.Result + "\nPNG export timeout (120 seconds)");
                    throw new TimeoutException("PNG出力が120秒で完了しませんでした。MOVは保存済みです。");
                }
                File.WriteAllText(movie + ".png.log", args + "\n" + stderr.Result);
                if (process.ExitCode != 0) throw new IOException("PNG出力に失敗しました: " + stderr.Result);
                return directory;
            });
            exports.RemoveAll(t => t.IsCompleted);
            exports.Add(task);
            return task;
        }

        public static void finish_exports()
        {
            try { Task.WaitAll(exports.ToArray()); }
            catch (AggregateException e) { UnityEngine.Debug.LogError("[ProRes PNG] " + e.GetBaseException().Message); }
            exports.Clear();
        }

        static string quote(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new ArgumentException("出力パスが不正です。");
            return "\"" + path + "\"";
        }
    }
}
