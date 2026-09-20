using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace D9speed.Recording
{
    /// <summary>
    /// 対象カメラまたは描画済み RenderTexture を録画する(ゲームビューの表示はそのまま)。
    /// AsyncGPUReadback で読み戻して FfmpegPipeSession に流し込む。
    /// カメラ指定はBuilt-in RP専用。URP/HDRPは描画済みRTを渡す。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class AlphaCaptureRecorder : MonoBehaviour
    {
        [Serializable]
        public struct Config
        {
            public Camera target_camera;
            public RenderTexture source_texture;
            public bool manual_capture; // 描画後に CaptureFrame() を明示的に呼ぶ場合。
            public int width;
            public int height;
            public int fps;
            public bool alpha;         // 背景を α0 の Solid Color でクリアして録画する
            public bool offline_mode;  // Time.captureFramerate 固定 + 同期読み戻し(取りこぼしゼロ・非リアルタイム)
            public bool force_ldr;     // 録画レンダリング時のみ HDR を無効化(アルファの安定用)
            public bool flip_vertical; // 読み戻し前に GPU 側 Blit で上下反転する(D3D11 では通常 true)。
                                       // ffmpeg 側の vflip は Vulkan ProRes 経路をクラッシュさせるため使わない
            public int max_queue;      // パイプ前段のフレームキュー上限
        }

        public bool IsRecording { get; private set; }
        public long FramesPushed => session?.PushedFrames ?? final_frames;
        public long RawReadbackBytes { get; private set; }
        public int PendingReadbacks { get; private set; }
        public long FramesDropped { get; private set; }
        public int QueuedFrames => session?.QueuedFrames ?? 0;
        public string OutputPath { get; private set; }
        public float StartRealtime { get; private set; }
        public int ExitCode { get; private set; }
        public string LastError { get; private set; }
        static readonly List<AlphaCaptureRecorder> active = new List<AlphaCaptureRecorder>();
        public static int ActiveSessions => active.Count;
        public static void StopAll()
        {
            foreach (var recorder in active.ToArray())
                try { if (recorder != null) recorder.StopRecording(); }
                catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>録画終了時(正常・異常とも)。引数: ffmpeg exit code / 出力パス / stderr 末尾。</summary>
        public event Action<int, string, string> Finished;

        Config config;
        FfmpegPipeSession session;
        RenderTexture render_rt;   // カメラの描き先(MSAA あり得る)
        RenderTexture resolve_rt;  // 読み戻し用(MSAA なし。AsyncGPUReadback は MSAA サーフェスを直接読めない)
        Coroutine capture_loop;
        int prev_capture_framerate;
        bool capturing;
        bool use_source_texture;
        long final_frames;
        string arguments;

        public void StartRecording(Config cfg, string ffmpeg_path, string ffmpeg_args, string output_path)
        {
            if (IsRecording) throw new InvalidOperationException("既に録画中です");
            if (cfg.target_camera == null && cfg.source_texture == null) throw new ArgumentException("カメラまたはRenderTextureを指定してください。");
            if (cfg.source_texture == null && GraphicsSettings.currentRenderPipeline != null)
                throw new NotSupportedException("URP/HDRPでは描画済みRenderTextureを指定してください。");
            if (cfg.width < 16 || cfg.height < 16 || cfg.width % 2 != 0 || cfg.height % 2 != 0 || cfg.fps < 1 || cfg.fps > 240)
                throw new ArgumentException("幅・高さは16以上の偶数、FPSは1～240を指定してください。");
            if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("GPU読み戻し非対応です。");
            if (File.Exists(output_path)) throw new IOException("出力ファイルが既に存在します: " + output_path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output_path)));
            cfg.max_queue = Mathf.Clamp(cfg.max_queue, 2, 32);
            config = cfg;
            use_source_texture = cfg.source_texture != null;
            OutputPath = output_path;
            arguments = ffmpeg_args;
            final_frames = FramesDropped = RawReadbackBytes = 0;
            PendingReadbacks = 0; LastError = ""; ExitCode = int.MinValue;

            int frame_size = checked(cfg.width * cfg.height * 4);
            try
            {
                if (!use_source_texture) render_rt = CreateRt(24, Mathf.Max(1, QualitySettings.antiAliasing));
                resolve_rt = CreateRt(0, 1);
                session = new FfmpegPipeSession(ffmpeg_path, ffmpeg_args, frame_size, cfg.max_queue);
            }
            catch
            {
                ReleaseRt(ref render_rt); ReleaseRt(ref resolve_rt);
                throw;
            }

            prev_capture_framerate = Time.captureFramerate;
            if (cfg.offline_mode) Time.captureFramerate = cfg.fps;

            StartRealtime = Time.realtimeSinceStartup;
            FramesDropped = 0;
            IsRecording = true;
            capturing = true;
            active.Add(this);
            if (!cfg.manual_capture) capture_loop = StartCoroutine(CaptureLoop());
        }

        RenderTexture CreateRt(int depth, int samples)
        {
            // Linear色空間でも見た目どおりのガンマ値を取得する。
            var rt = new RenderTexture(config.width, config.height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "d9speed_alpha_capture", antiAliasing = samples };
            if (rt.Create()) return rt;
            ReleaseRt(ref rt);
            throw new InvalidOperationException("録画用RenderTextureを作成できませんでした。");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            capturing = false;
            if (capture_loop != null) { StopCoroutine(capture_loop); capture_loop = null; }

            int code = -1;
            string stderr = "";
            try
            {
                // 発行済みreadbackを回収してからstdinを閉じる。
                AsyncGPUReadback.WaitAllRequests();
                code = session.Close(); final_frames = session.PushedFrames; stderr = session.StderrTail;
            }
            catch (Exception e) { LastError = e.Message; }
            finally
            {
                try { session?.Dispose(); } catch (Exception e) { LastError = e.Message; }
                session = null;
                if (config.offline_mode) Time.captureFramerate = prev_capture_framerate;
                ReleaseRt(ref render_rt); ReleaseRt(ref resolve_rt);
                active.Remove(this);
            }
            if (code != 0) LastError = "FFmpeg exit=" + code + "\n" + stderr;
            if (!string.IsNullOrEmpty(LastError) && code == 0) code = -1;
            ExitCode = code;
            try
            {
                File.WriteAllText(OutputPath + ".ffmpeg.log", arguments + "\n" + stderr);
                File.WriteAllText(OutputPath + ".capture.json", JsonUtility.ToJson(new capture_report {
                    unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                    width = config.width, height = config.height, fps = config.fps, alpha = config.alpha,
                    frames = final_frames, dropped = FramesDropped, raw_readback_bytes = RawReadbackBytes,
                    exit_code = code, error = LastError, wall_seconds = Time.realtimeSinceStartup - StartRealtime
                }, true));
            }
            catch (Exception e) { Debug.LogWarning("[ProRes] ログ保存: " + e.Message); }
            Finished?.Invoke(code, OutputPath, string.IsNullOrEmpty(LastError) ? stderr : LastError);
        }

        void OnDisable() => StopRecording();

        void OnDestroy()
        {
            // プレイモード終了などで破棄された場合の保険
            if (IsRecording) StopRecording();
        }

        static void ReleaseRt(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt);
            rt = null;
        }

        IEnumerator CaptureLoop()
        {
            var wait = new WaitForEndOfFrame();
            double next = Time.realtimeSinceStartupAsDouble;
            while (capturing)
            {
                yield return wait;
                if (!capturing) yield break;
                if (session == null || session.Faulted)
                {
                    StopRecording(); // ffmpeg 側が落ちた。stderr を添えて Finished が飛ぶ
                    yield break;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                if (!config.offline_mode && now < next) continue;
                next = Math.Max(next + 1.0 / config.fps, now);
                try { CaptureFrame(); }
                catch (Exception e) { LastError = e.Message; StopRecording(); }
            }
        }

        public void CaptureFrame()
        {
            if (!IsRecording) throw new InvalidOperationException("録画を開始してください。");
            if (session.Faulted) throw new IOException("FFmpegへの書き込みが中断しました。" + session.StderrTail);
            if (!config.offline_mode && PendingReadbacks + QueuedFrames >= config.max_queue) { FramesDropped++; return; }
            RenderToCaptureRt();
            if (config.offline_mode)
            {
                // ゲーム時間は captureFramerate で固定済み。同期読み戻し+満杯待ちで取りこぼしを許さない
                var req = AsyncGPUReadback.Request(resolve_rt, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError) throw new IOException("GPU読み戻しに失敗しました。");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (session.QueuedFrames >= config.max_queue && !session.Faulted)
                {
                    if (timer.ElapsedMilliseconds > 30000) throw new TimeoutException("FFmpegへの送信がタイムアウトしました。");
                    System.Threading.Thread.Sleep(1);
                }
                OnReadback(req);
            }
            else
            {
                PendingReadbacks++;
                AsyncGPUReadback.Request(resolve_rt, 0, TextureFormat.RGBA32, req => { PendingReadbacks--; OnReadback(req); });
            }
        }

        void RenderToCaptureRt()
        {
            if (use_source_texture)
            {
                if (config.source_texture == null || !config.source_texture.IsCreated()) throw new InvalidOperationException("入力RenderTextureが失われたか作成されていません。");
                BlitSource(config.source_texture);
                return;
            }
            var cam = config.target_camera;
            if (cam == null) throw new InvalidOperationException("録画対象カメラが失われました。");
            var prev_target = cam.targetTexture;
            var prev_flags = cam.clearFlags;
            var prev_bg = cam.backgroundColor;
            bool prev_hdr = cam.allowHDR;
            float prev_aspect = cam.aspect;

            try
            {
                cam.targetTexture = render_rt;
                if (config.alpha)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.clear;
                }
                if (config.force_ldr) cam.allowHDR = false;

                cam.Render();
            }
            finally
            {
                cam.targetTexture = prev_target;
                cam.clearFlags = prev_flags;
                cam.backgroundColor = prev_bg;
                cam.allowHDR = prev_hdr;
                cam.aspect = prev_aspect;
            }
            BlitSource(render_rt);
        }

        void BlitSource(RenderTexture source)
        {
            // MSAA の resolve を兼ねる。上下反転もここ(GPU 内)で行う
            if (config.flip_vertical)
                Graphics.Blit(source, resolve_rt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else
                Graphics.Blit(source, resolve_rt);
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            if (session == null) return; // 停止処理後に届いた分
            if (req.hasError) { FramesDropped++; return; }
            RawReadbackBytes += req.GetData<byte>().Length;
            if (!session.TryPushFrame(req.GetData<byte>()))
                FramesDropped++; // リアルタイムモードでキュー満杯 → ドロップ
        }

        [Serializable] sealed class capture_report
        {
            public string unity, gpu, error;
            public int width, height, fps, exit_code;
            public bool alpha;
            public long frames, dropped, raw_readback_bytes;
            public float wall_seconds;
        }
    }
}
