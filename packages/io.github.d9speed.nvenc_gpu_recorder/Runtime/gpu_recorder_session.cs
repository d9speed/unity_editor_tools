using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace D9speed.NvencGpu
{
    [Serializable]
    public sealed class gpu_recorder_options
    {
        public int width = 1920, height = 1080, fps = 60, bitrate = 0, cq = 23, buffers = 8;
        public bool flip_vertical = true;
        public string ffmpeg_path, output_path;
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct gpu_recorder_stats
    {
        public int state, api_version, width, height;
        public long captured, submitted, encoded, written, bytes, backpressure, gpu_copies, raw_readback_bytes;
    }

    /// <summary>
    /// Fixed-rate, opaque HEVC capture. Must be called on Unity's main thread.
    /// Capture queues a GPU blit and native render callback in the same CommandBuffer.
    /// The session owns the staging RT; the caller retains ownership of the source RT.
    /// </summary>
    public sealed class gpu_recorder_session : IDisposable
    {
        static readonly List<gpu_recorder_session> active = new List<gpu_recorder_session>();
        static readonly List<Task> pending_mux = new List<Task>();
        public static int active_sessions => active.Count;
        static gpu_recorder_session() { Application.quitting += stop_all; }
        public static void stop_all()
        {
            foreach (var item in active.ToArray())
                try { item.stop(); item.mux_task.GetAwaiter().GetResult(); }
                catch (Exception e) { UnityEngine.Debug.LogError("[NVENC GPU] " + e.Message); }
            foreach(var task in pending_mux.ToArray())
                try { task.GetAwaiter().GetResult(); }
                catch(Exception e) { UnityEngine.Debug.LogError("[NVENC GPU] " + e.Message); }
            pending_mux.Clear();
        }
        static class native
        {
            const string dll = native_build.dll_name;
            [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ng_create")]
            internal static extern int create(IntPtr texture, int width, int height, int fps, int bitrate, int cq,
                [MarshalAs(UnmanagedType.LPWStr)] string path, int buffers, StringBuilder error, int capacity);
            [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ng_render_event")]
            internal static extern IntPtr render_event();
            [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ng_status")]
            internal static extern int status(int id, out gpu_recorder_stats stats, StringBuilder error, int capacity);
            [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ng_forget")]
            internal static extern int forget(int id);
        }
        readonly gpu_recorder_options options;
        readonly StringBuilder error_buffer = new StringBuilder(4096);
        readonly CommandBuffer commands;
        RenderTexture staging;
        int id;
        readonly IntPtr callback;
        bool close_requested;
        gpu_recorder_stats final_stats;
        string failure = "";
        readonly string unity_version, gpu_name;
        readonly Stopwatch wall = Stopwatch.StartNew();
        public string raw_path { get; }
        public string output_path => options.output_path;
        public bool is_closed => id == 0;
        public string error => failure;
        public Task mux_task { get; private set; } = Task.CompletedTask;
        public long requested_frames { get; private set; }
        public gpu_recorder_stats stats
        {
            get
            {
                if (id == 0) return final_stats;
                error_buffer.Clear();
                native.status(id, out final_stats, error_buffer, error_buffer.Capacity);
                if (error_buffer.Length > 0) failure = error_buffer.ToString();
                return final_stats;
            }
        }
        public gpu_recorder_session(gpu_recorder_options settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
                throw new NotSupportedException("Windows x64 が必要です。");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || SystemInfo.graphicsDeviceVendorID != 0x10de)
                throw new NotSupportedException("NVIDIA GPU の Direct3D11 を使用してください。");
            if (settings.width < 16 || settings.height < 16 || (settings.width & 1) != 0 || (settings.height & 1) != 0
                || settings.fps < 1 || settings.fps > 240 || settings.bitrate < 0 || settings.cq < 0 || settings.cq > 51
                || settings.buffers < 4 || settings.buffers > 32)
                throw new ArgumentException("解像度は16以上の偶数、FPSは1〜240、CQは0〜51、バッファは4〜32で指定してください。");
            options = JsonUtility.FromJson<gpu_recorder_options>(JsonUtility.ToJson(settings));
            unity_version = Application.unityVersion; gpu_name = SystemInfo.graphicsDeviceName;
            options.output_path = Path.GetFullPath(options.output_path);
            options.ffmpeg_path = Path.GetFullPath(options.ffmpeg_path);
            if (!File.Exists(options.ffmpeg_path)) throw new FileNotFoundException("MP4格納用の ffmpeg.exe が見つかりません。",options.ffmpeg_path);
            if (!string.Equals(Path.GetExtension(options.output_path),".mp4",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("出力先には .mp4 を指定してください。");
            raw_path = Path.ChangeExtension(options.output_path,".hevc");
            if (File.Exists(options.output_path) || File.Exists(raw_path)) throw new IOException("出力ファイルが既に存在します。");
            Directory.CreateDirectory(Path.GetDirectoryName(options.output_path));
            commands = new CommandBuffer { name = "d9_nvenc_gpu_capture" };
            try
            {
                staging = new RenderTexture(options.width,options.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                { name = "nvenc_gpu_staging", antiAliasing = 1, useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.HideAndDontSave };
                if (!staging.Create()) throw new InvalidOperationException("録画用RenderTextureを作成できません。");
                callback = native.render_event();
                id = native.create(staging.GetNativeTexturePtr(), options.width,options.height,options.fps,
                    options.bitrate,options.cq,raw_path,options.buffers,error_buffer,error_buffer.Capacity);
                if (id == 0) throw new InvalidOperationException(error_buffer.ToString());
                active.Add(this);
            }
            catch { release_unity_resources(); throw; }
        }
        public void capture(RenderTexture source)
        {
            if (id == 0 || close_requested) throw new InvalidOperationException("録画は停止済みです。");
            if (source == null || !source.IsCreated()) throw new ArgumentException("有効な描画済みRenderTextureを渡してください。");
            if (stats.state == 4) throw new InvalidOperationException(failure);
            commands.Clear();
            commands.Blit(source,staging,options.flip_vertical ? new Vector2(1,-1) : Vector2.one,
                options.flip_vertical ? new Vector2(0,1) : Vector2.zero);
            commands.IssuePluginEventAndData(callback,1,new IntPtr(id));
            Graphics.ExecuteCommandBuffer(commands);
            requested_frames++;
        }
        public void request_stop()
        {
            if (id == 0 || close_requested) return;
            close_requested = true;
            commands.Clear(); commands.IssuePluginEventAndData(callback,3,new IntPtr(id));
            Graphics.ExecuteCommandBuffer(commands); GL.Flush();
        }
        // Poll on the main thread. Native shared ownership protects callbacks still returning.
        public bool finish_if_ready()
        {
            if (id == 0) return true;
            if (!close_requested) return false;
            final_stats = stats;
            if (final_stats.state < 3) return false;
            if (native.forget(id) == 0) return false;
            id = 0; active.Remove(this); release_unity_resources();
            bool complete = final_stats.state == 3 && final_stats.written == requested_frames;
            if (!complete && string.IsNullOrEmpty(failure)) failure = "全フレームを書き出せませんでした。";
            if (complete && requested_frames > 0)
            {
                mux_task = Task.Run(() => mux(raw_path,options.output_path,options.ffmpeg_path,options.fps));
                pending_mux.RemoveAll(task => task.IsCompletedSuccessfully);
                pending_mux.Add(mux_task);
            }
            var report = new capture_report { unity=unity_version,gpu=gpu_name,options=options,stats=final_stats,
                requested_frames=requested_frames,wall_seconds=wall.Elapsed.TotalSeconds,error=failure };
            File.WriteAllText(Path.ChangeExtension(options.output_path,".capture.json"),JsonUtility.ToJson(report,true));
            return true;
        }
        public void stop()
        {
            request_stop();
            var timer = Stopwatch.StartNew();
            while (!finish_if_ready())
            {
                if (timer.Elapsed.TotalSeconds > 20)
                    throw new TimeoutException("GPU録画の停止がタイムアウトしました。処理中のテクスチャは保持しています。");
                Thread.Sleep(1);
            }
        }
        void release_unity_resources()
        {
            commands?.Release();
            if (staging == null) return;
            staging.Release();
            if (Application.isPlaying) Object.Destroy(staging); else Object.DestroyImmediate(staging);
            staging = null;
        }
        public void Dispose() { stop(); }
        [Serializable] sealed class capture_report
        {
            public string unity,gpu,error;
            public gpu_recorder_options options;
            public gpu_recorder_stats stats;
            public long requested_frames;
            public double wall_seconds;
        }
        static string quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                throw new ArgumentException("パスに使用できない文字があります。");
            return "\"" + value + "\"";
        }
        static void mux(string source, string output, string exe, int fps)
        {
            // Only already-compressed HEVC reaches FFmpeg. No video re-encoding or GPU initialization.
            string args = "-hide_banner -loglevel warning -nostdin -n -fflags +genpts -r " + fps
                + " -i " + quote(source) + " -map 0:v:0 -c:v copy -bsf:v "
                + quote("setts=pts=N:dts=N:duration=1:time_base=1/"+fps)
                + " -tag:v hvc1 -movflags +faststart " + quote(output);
            using (var process = new Process { StartInfo = new ProcessStartInfo(exe,args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } })
            {
                var log = new StringBuilder();
                process.ErrorDataReceived += (_,e) => { if (e.Data != null) lock(log) { if(log.Length < 32000) log.AppendLine(e.Data); } };
                process.Start(); process.BeginErrorReadLine();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("MP4格納がタイムアウトしました。HEVCファイルは保存されています。"); }
                process.WaitForExit();
                File.WriteAllText(Path.ChangeExtension(output,".mux.log"),args + Environment.NewLine + log);
                if (process.ExitCode != 0) throw new IOException("MP4格納に失敗しました。HEVCファイルは保存されています。" + log);
            }
        }
    }
}
