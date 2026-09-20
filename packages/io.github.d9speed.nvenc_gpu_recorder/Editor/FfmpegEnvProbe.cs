using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace D9speed.Recording.Editor
{
    /// <summary>ffmpeg 実行環境の検出結果。</summary>
    public sealed class FfmpegProbeResult
    {
        public string resolved_path;   // 実際に使う ffmpeg のフルパス(null = 見つからない)
        public string version_line;    // "ffmpeg version 8.1-full_build..." の要約
        public bool has_prores_ks_vulkan;
        public bool has_prores_ks;
        public bool has_hevc_nvenc;
        public bool has_hevc_amf;
        public bool has_hevc_qsv;
        public bool has_libx264;

        /// <summary>本番と同一経路(hwupload→libplacebo→prores_ks_vulkan)の 1 フレーム実エンコードが通ったか。</summary>
        public bool vulkan_prores_ok;

        /// <summary>
        /// 実働確認済みの非アルファ用エンコーダ名(nvenc→amf→qsv→x264 の順で 1 フレーム実エンコードに
        /// 最初に成功したもの)。null = 全滅。「存在するが動かない」(ドライバ API 不足等)を除外するための検証。
        /// </summary>
        public string hevc_verified;

        public string error;

        public bool Found => !string.IsNullOrEmpty(resolved_path);
    }

    /// <summary>
    /// ffmpeg の所在・バージョン・エンコーダ・Vulkan 実働を確認する。
    /// Run はプロセス起動を伴い数秒かかるので、呼び出し側で Task.Run すること。
    /// </summary>
    public static class FfmpegEnvProbe
    {
        /// <summary>PATH から ffmpeg.exe を探す(見つからなければ null)。</summary>
        public static string FindOnPath()
        {
            try
            {
                var (code, stdout, _) = Exec("where.exe", "ffmpeg", 5000);
                if (code != 0) return null;
                var first = stdout.Replace("\r", "").Split('\n')[0].Trim();
                return first.Length > 0 ? first : null;
            }
            catch { return null; }
        }

        /// <summary>override_path が空なら PATH から解決してプローブする(同期・数秒かかる)。</summary>
        public static FfmpegProbeResult Run(string override_path, bool test_hevc = true, bool test_vulkan = true)
        {
            var r = new FfmpegProbeResult();
            try
            {
                r.resolved_path = string.IsNullOrWhiteSpace(override_path) ? FindOnPath() : override_path.Trim();
                if (r.resolved_path != null && !File.Exists(r.resolved_path)) r.resolved_path = null;
                if (!r.Found)
                {
                    r.error = "ffmpeg.exe が見つかりません(PATH またはパス指定を確認)";
                    return r;
                }

                var (vcode, vout, verr) = Exec(r.resolved_path, "-version", 10000);
                if (vcode != 0)
                {
                    r.error = "ffmpeg -version が失敗: " + Tail(verr);
                    return r;
                }
                r.version_line = FirstLine(vout).Split(new[] { " Copyright" }, StringSplitOptions.None)[0];

                var (ecode, eout, _) = Exec(r.resolved_path, "-hide_banner -encoders", 15000);
                if (ecode == 0)
                {
                    r.has_prores_ks_vulkan = eout.Contains(" prores_ks_vulkan ");
                    r.has_prores_ks = eout.Contains(" prores_ks ");
                    r.has_hevc_nvenc = eout.Contains(" hevc_nvenc ");
                    r.has_hevc_amf = eout.Contains(" hevc_amf ");
                    r.has_hevc_qsv = eout.Contains(" hevc_qsv ");
                    r.has_libx264 = eout.Contains(" libx264 ");
                }

                // HEVC/H.264 系は存在と実働が乖離する(例: NVENC がドライバの API バージョン不足で失敗)ため、
                // 候補を実働順にテストして最初に通ったものを採用する
                var candidates = new System.Collections.Generic.List<(string name, string args)>();
                if (r.has_hevc_nvenc) candidates.Add(("hevc_nvenc", "-vf format=rgba -c:v hevc_nvenc -preset p5 -rc vbr -cq 23 -b:v 0"));
                if (r.has_hevc_amf) candidates.Add(("hevc_amf", "-pix_fmt yuv420p -c:v hevc_amf"));
                if (r.has_hevc_qsv) candidates.Add(("hevc_qsv", "-pix_fmt yuv420p -c:v hevc_qsv"));
                if (r.has_libx264) candidates.Add(("libx264", "-pix_fmt yuv420p -c:v libx264 -preset veryfast"));
                foreach (var (name, enc_args) in candidates)
                {
                    if (!test_hevc) break;
                    var (c, _, _) = Exec(r.resolved_path,
                        $"-hide_banner -v error -f lavfi -i testsrc2=size=256x256:rate=30:duration=0.1 {enc_args} -f null -",
                        20000);
                    if (c == 0) { r.hevc_verified = name; break; }
                }

                if (r.has_prores_ks_vulkan && test_vulkan)
                {
                    // 本番同等経路の 1 フレーム実エンコード。初回はシェーダ初期化で数秒かかることがある
                    r.vulkan_prores_ok = PreflightVulkanProres(r.resolved_path, 64, 64, out string perr, 30000);
                    if (!r.vulkan_prores_ok) r.error = "Vulkan ProRes プローブ失敗: " + perr;
                }
            }
            catch (Exception ex)
            {
                r.error = ex.Message;
            }
            return r;
        }

        /// <summary>
        /// 指定解像度で本番と同一の Vulkan ProRes 経路(hwupload→libplacebo→prores_ks_vulkan)が動くかを
        /// 1 フレームで検証する。未知の条件で ffmpeg ごとクラッシュする例が確認されているため、
        /// 録画開始前に実解像度で呼ぶこと(注意: vflip は経路をクラッシュさせるため本番共々使わない)。
        /// </summary>
        public static bool PreflightVulkanProres(string exe_path, int width, int height, out string error_tail, int timeout_ms = 20000)
        {
            // 本番と同じ共有フィルタ・コーデック設定を使用する。
            var (code, _, stderr) = Exec(exe_path,
                "-hide_banner -v error " + prores_encoding.vulkan_device +
                $"-f lavfi -i color=black:size={width}x{height}:duration=0.05:rate=30 " +
                "-vf \"format=rgba," + prores_encoding.vulkan_filter + "\" " +
                prores_encoding.vulkan_codec + " -frames:v 1 -f null -",
                timeout_ms);
            error_tail = code == 0 ? null : $"exit={code} {Tail(stderr)}";
            return code == 0;
        }

        static string FirstLine(string s) => s.Replace("\r", "").Split('\n')[0].Trim();

        static string Tail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(出力なし)";
            var lines = s.Replace("\r", "").Split('\n');
            int n = Math.Min(3, lines.Length);
            return string.Join(" / ", lines, lines.Length - n, n).Trim();
        }

        static (int code, string stdout, string stderr) Exec(string exe, string args, int timeout_ms)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };
            p.Start();
            // 同期 ReadToEnd はパイプ詰まりでデッドロックするため非同期で読む
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout_ms))
            {
                try { p.Kill(); } catch { }
                return (-1, "", "timeout");
            }
            return (p.ExitCode, so.Result, se.Result);
        }
    }
}
