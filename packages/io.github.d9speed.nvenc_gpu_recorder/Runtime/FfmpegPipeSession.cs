using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;

namespace D9speed.Recording
{
    /// <summary>
    /// ffmpeg.exe を起動し、stdin に rawvideo フレームを書き込むセッション。
    /// 書き込みは専用スレッドで行い、メインスレッドは TryPushFrame でキューへ積むだけ。
    /// キュー満杯時の方針(ドロップ/待機)は呼び出し側が決める。
    /// </summary>
    public sealed class FfmpegPipeSession : IDisposable
    {
        readonly Process process;
        readonly Thread writer_thread;
        readonly BlockingCollection<byte[]> frame_queue;
        readonly ConcurrentBag<byte[]> buffer_pool = new ConcurrentBag<byte[]>();
        readonly Queue<string> stderr_lines = new Queue<string>();
        readonly object stderr_lock = new object();
        readonly int frame_size;
        volatile bool faulted;
        bool closed;
        int exit_code = int.MinValue;

        public long PushedFrames { get; private set; }
        public int QueuedFrames => frame_queue.Count;

        /// <summary>ffmpeg が落ちてパイプへ書けなくなったら true(引数ミス・エンコード失敗など)。</summary>
        public bool Faulted => faulted;

        /// <summary>ffmpeg の stderr 末尾(エラー原因の表示用)。</summary>
        public string StderrTail
        {
            get { lock (stderr_lock) return string.Join("\n", stderr_lines); }
        }

        public FfmpegPipeSession(string exe_path, string arguments, int frame_size, int max_queued_frames)
        {
            this.frame_size = frame_size;
            frame_queue = new BlockingCollection<byte[]>(max_queued_frames);

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe_path,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (stderr_lock)
                {
                    stderr_lines.Enqueue(e.Data);
                    while (stderr_lines.Count > 40) stderr_lines.Dequeue();
                }
            };
            process.Start();
            process.BeginErrorReadLine();

            writer_thread = new Thread(WriterLoop) { Name = "d9speed_ffmpeg_writer", IsBackground = true };
            writer_thread.Start();
        }

        /// <summary>
        /// フレームをキューへ積む。満杯・停止済み・サイズ不一致なら false(呼び出し側でドロップ扱い)。
        /// </summary>
        public bool TryPushFrame(NativeArray<byte> data)
        {
            if (faulted || closed || data.Length != frame_size) return false;
            if (!buffer_pool.TryTake(out var buf)) buf = new byte[frame_size];
            data.CopyTo(buf);
            if (!frame_queue.TryAdd(buf))
            {
                buffer_pool.Add(buf);
                return false;
            }
            PushedFrames++;
            return true;
        }

        void WriterLoop()
        {
            var stdin = process.StandardInput.BaseStream;
            try
            {
                foreach (var buf in frame_queue.GetConsumingEnumerable())
                {
                    stdin.Write(buf, 0, frame_size);
                    buffer_pool.Add(buf);
                }
                stdin.Flush();
            }
            catch (Exception)
            {
                faulted = true;
            }
            finally
            {
                // stdin を閉じることで ffmpeg に入力終端を伝える(これがないと ffmpeg が終わらない)
                try { stdin.Close(); } catch { }
            }
        }

        /// <summary>
        /// 残フレームを書き切り、ffmpeg の終了を待って exit code を返す(タイムアウト時 int.MinValue)。
        /// </summary>
        public int Close(int timeout_ms = 30000)
        {
            if (closed) return exit_code;
            closed = true;
            frame_queue.CompleteAdding();
            if (!writer_thread.Join(timeout_ms))
            {
                faulted = true;
                try { process.Kill(); } catch { }
                // stdin 書き込み中のスレッドを解放してからキューを破棄する。
                writer_thread.Join();
            }
            try
            {
                if (process.WaitForExit(timeout_ms)) { process.WaitForExit(); exit_code = process.ExitCode; }
                else { process.Kill(); process.WaitForExit(); faulted = true; }
            }
            catch (Exception) { faulted = true; }
            return exit_code;
        }

        public void Dispose()
        {
            Close(5000);
            try { process.Dispose(); } catch { }
            frame_queue.Dispose();
        }
    }
}
