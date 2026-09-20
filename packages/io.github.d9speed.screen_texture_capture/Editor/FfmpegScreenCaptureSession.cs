using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;

namespace D9speed.ScreenTextureCapture.Editor
{
    internal enum ScreenCaptureBackend
    {
        DdaGrab,
        GdiGrab,
    }

    internal sealed class ScreenCaptureConfiguration
    {
        public string ffmpeg_path;
        public int x;
        public int y;
        public int dxgi_x;
        public int dxgi_y;
        public int width;
        public int height;
        public int fps;
        public bool draw_mouse;
        public bool flip_vertical;
        public int display_index;
        public bool dxgi_region_valid;
    }

    /// <summary>
    /// FFmpeg を別プロセスで動かし、BGRA rawvideo の最新フレームだけを保持する。
    /// Unity API はメインスレッド側の CopyLatestFrame からのみ使用する。
    /// </summary>
    internal sealed class FfmpegScreenCaptureSession : IDisposable
    {
        readonly object buffer_lock = new object();
        readonly object stderr_lock = new object();
        readonly Queue<string> stderr_tail = new Queue<string>();

        Process process;
        CancellationTokenSource cancellation;
        Task reader_task;
        byte[] read_buffer;
        byte[] latest_buffer;
        long published_version;
        long consumed_version;
        long received_frames;
        long dropped_frames;
        bool capture_ended;
        string terminal_error;

        public ScreenCaptureBackend Backend { get; private set; }
        public int FrameByteCount { get; private set; }
        public bool HasReceivedFrame => Interlocked.Read(ref received_frames) > 0;
        public long ReceivedFrames => Interlocked.Read(ref received_frames);
        public long DroppedFrames => Interlocked.Read(ref dropped_frames);
        public bool CaptureEnded => capture_ended;

        public string ErrorSummary
        {
            get
            {
                var builder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(terminal_error)) builder.Append(terminal_error.Trim());
                lock (stderr_lock)
                {
                    foreach (string line in stderr_tail)
                    {
                        if (builder.Length > 0) builder.Append(" / ");
                        builder.Append(line);
                    }
                }
                return builder.Length == 0 ? "FFmpegからフレームを受信できませんでした。" : builder.ToString();
            }
        }

        public void Start(ScreenCaptureConfiguration configuration, ScreenCaptureBackend backend)
        {
            if (process != null) throw new InvalidOperationException("キャプチャは既に開始されています。");
            Validate(configuration, backend);

            Backend = backend;
            FrameByteCount = checked(configuration.width * configuration.height * 4);
            read_buffer = new byte[FrameByteCount];
            latest_buffer = new byte[FrameByteCount];
            cancellation = new CancellationTokenSource();

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = configuration.ffmpeg_path,
                    Arguments = BuildArguments(configuration, backend),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };
            process.ErrorDataReceived += OnErrorDataReceived;

            try
            {
                if (!process.Start()) throw new InvalidOperationException("FFmpegプロセスを開始できませんでした。");
                process.BeginErrorReadLine();
                reader_task = Task.Run(() => ReadFramesAsync(process.StandardOutput.BaseStream, cancellation.Token));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool CopyLatestFrame(NativeArray<byte> destination, ref long copied_version)
        {
            if (!destination.IsCreated || destination.Length != FrameByteCount) return false;

            lock (buffer_lock)
            {
                if (published_version == 0 || published_version == copied_version) return false;
                destination.CopyFrom(latest_buffer);
                copied_version = published_version;
                consumed_version = published_version;
                return true;
            }
        }

        async Task ReadFramesAsync(Stream stream, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int offset = 0;
                    while (offset < FrameByteCount)
                    {
                        int read = await stream.ReadAsync(read_buffer, offset, FrameByteCount - offset, token);
                        if (read == 0) throw new EndOfStreamException("FFmpegの映像パイプが終了しました。");
                        offset += read;
                    }

                    lock (buffer_lock)
                    {
                        if (published_version != consumed_version) Interlocked.Increment(ref dropped_frames);
                        byte[] swap = latest_buffer;
                        latest_buffer = read_buffer;
                        read_buffer = swap;
                        published_version++;
                    }
                    Interlocked.Increment(ref received_frames);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
            catch (ObjectDisposedException)
            {
                // 停止時にパイプを閉じた場合。
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested) terminal_error = exception.Message;
            }
            finally
            {
                capture_ended = true;
            }
        }

        void OnErrorDataReceived(object sender, DataReceivedEventArgs event_args)
        {
            string line = event_args.Data;
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (stderr_lock)
            {
                stderr_tail.Enqueue(line.Trim());
                while (stderr_tail.Count > 4) stderr_tail.Dequeue();
            }
        }

        public void Dispose()
        {
            CancellationTokenSource local_cancellation = cancellation;
            Process local_process = process;
            cancellation = null;
            process = null;

            try { local_cancellation?.Cancel(); } catch { }
            try { local_process?.StandardOutput.Close(); } catch { }

            if (local_process != null)
            {
                try
                {
                    if (!local_process.HasExited)
                    {
                        local_process.Kill();
                        local_process.WaitForExit(1000);
                    }
                }
                catch { }

                try { local_process.CancelErrorRead(); } catch { }
                local_process.ErrorDataReceived -= OnErrorDataReceived;
                local_process.Dispose();
            }

            try { reader_task?.Wait(250); } catch { }
            reader_task = null;
            local_cancellation?.Dispose();
        }

        static string BuildArguments(ScreenCaptureConfiguration configuration, ScreenCaptureBackend backend)
        {
            string flip = configuration.flip_vertical ? ",vflip" : string.Empty;
            if (backend == ScreenCaptureBackend.DdaGrab)
            {
                return "-hide_banner -nostdin -loglevel warning -f lavfi " +
                       $"-i \"ddagrab=output_idx={configuration.display_index}:framerate={configuration.fps}:draw_mouse={(configuration.draw_mouse ? 1 : 0)}\" " +
                       $"-vf \"hwdownload,format=bgra,crop={configuration.width}:{configuration.height}:{configuration.dxgi_x}:{configuration.dxgi_y}{flip}\" " +
                       "-an -pix_fmt bgra -f rawvideo pipe:1";
            }

            return "-hide_banner -nostdin -loglevel warning " +
                   $"-f gdigrab -framerate {configuration.fps} -draw_mouse {(configuration.draw_mouse ? 1 : 0)} " +
                   $"-offset_x {configuration.x} -offset_y {configuration.y} " +
                   $"-video_size {configuration.width}x{configuration.height} -i desktop " +
                   $"-vf \"format=bgra{flip}\" -an -pix_fmt bgra -f rawvideo pipe:1";
        }

        static void Validate(ScreenCaptureConfiguration configuration, ScreenCaptureBackend backend)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(configuration.ffmpeg_path) || !File.Exists(configuration.ffmpeg_path))
                throw new FileNotFoundException("ffmpeg.exeが見つかりません。", configuration.ffmpeg_path);
            if (configuration.width < 16 || configuration.height < 16)
                throw new ArgumentOutOfRangeException(nameof(configuration), "幅と高さは16px以上にしてください。");
            if (configuration.width > 8192 || configuration.height > 8192)
                throw new ArgumentOutOfRangeException(nameof(configuration), "安全のため最大解像度は8192pxです。");
            if (configuration.fps != 10 && configuration.fps != 15 && configuration.fps != 30 && configuration.fps != 60)
                throw new ArgumentOutOfRangeException(nameof(configuration), "FPSは10・15・30・60のいずれかを指定してください。");
            if (backend == ScreenCaptureBackend.DdaGrab && !configuration.dxgi_region_valid)
                throw new ArgumentOutOfRangeException(nameof(configuration), "DXGIでは1台のディスプレイ内に収まる範囲を指定してください。");
            if (backend == ScreenCaptureBackend.DdaGrab && (configuration.dxgi_x < 0 || configuration.dxgi_y < 0))
                throw new ArgumentOutOfRangeException(nameof(configuration), "DXGIのディスプレイ内座標が不正です。");
            checked { _ = configuration.width * configuration.height * 4; }
        }
    }
}
