using System;
using System.IO;
using System.Threading.Tasks;
using D9speed.Recording;
using D9speed.Recording.Editor;
using UnityEditor;
using UnityEngine;

namespace D9speed.NvencGpu.Editor
{
    public sealed class gpu_recorder_window : EditorWindow
    {
        const string key = "d9_nvenc_gpu_";
        const string window_title = "FFmpeg Recorder";
        [SerializeField] Camera camera_source;
        [SerializeField] RenderTexture texture_source;
        [SerializeField] gpu_recorder_options options = new gpu_recorder_options();
        [SerializeField] string output_directory, status = "", last_movie, last_ffmpeg, alpha_ffmpeg_path;
        [SerializeField] bool offline, with_alpha, export_png, last_premultiplied;
        [SerializeField] bool prefer_vulkan = true;
        gpu_camera_recorder recorder;
        AlphaCaptureRecorder alpha_recorder;
        Task<prepared_prores> preparation;
        Task<string> png_task;
        Vector2 scroll;
        bool recording => recorder != null && recorder.is_recording || alpha_recorder != null && alpha_recorder.IsRecording;
        bool finishing => png_task != null && !png_task.IsCompleted || recorder != null && !recorder.mux_task.IsCompleted;
        bool busy => recording || preparation != null || finishing;
        string current_ffmpeg { get => with_alpha ? alpha_ffmpeg_path : options.ffmpeg_path; set { if (with_alpha) alpha_ffmpeg_path = value; else options.ffmpeg_path = value; } }

        [MenuItem("D9speed/Recorder/FFmpeg Recorder")]
        public static void open() => GetWindow<gpu_recorder_window>(window_title);
        void OnEnable()
        {
            titleContent = new GUIContent(window_title);
            minSize = new Vector2(390, 570);
            options.ffmpeg_path = EditorPrefs.GetString(key+"ffmpeg", "");
            if (string.IsNullOrWhiteSpace(options.ffmpeg_path)) options.ffmpeg_path = FfmpegEnvProbe.FindOnPath() ?? "";
            alpha_ffmpeg_path = EditorPrefs.GetString(key+"alpha_ffmpeg", EditorPrefs.GetString("D9speed.Recorder.FfmpegPath", ""));
            if (string.IsNullOrWhiteSpace(alpha_ffmpeg_path)) alpha_ffmpeg_path = FfmpegEnvProbe.FindOnPath() ?? options.ffmpeg_path;
            output_directory = EditorPrefs.GetString(key+"output", Path.GetFullPath("Recordings/nvenc_gpu"));
            AssemblyReloadEvents.beforeAssemblyReload += stop;
            EditorApplication.playModeStateChanged += on_play_state;
            EditorApplication.quitting += stop;
        }
        void OnDisable()
        {
            stop(); prores_encoding.finish_exports();
            AssemblyReloadEvents.beforeAssemblyReload -= stop;
            EditorApplication.playModeStateChanged -= on_play_state;
            EditorApplication.quitting -= stop;
        }
        void on_play_state(PlayModeStateChange value) { if (value == PlayModeStateChange.ExitingPlayMode) stop(); }
        void Update()
        {
            if (preparation != null && preparation.IsCompleted)
            {
                var ready = preparation; preparation = null;
                try { var prepared = ready.GetAwaiter().GetResult(); if (EditorApplication.isPlaying) begin_alpha(prepared); }
                catch (Exception e) { fail(e); }
                Repaint();
            }
            if (png_task != null && png_task.IsCompleted)
            {
                try { status = "MOV・透過PNGの保存完了: " + png_task.GetAwaiter().GetResult(); }
                catch (Exception e) { fail(e); }
                png_task = null;
                Repaint();
            }
            if (recorder != null && recorder.session != null && !recorder.is_recording)
            {
                if (recorder.mux_task.IsFaulted) status = recorder.mux_task.Exception.GetBaseException().Message;
                else if (recorder.mux_task.IsCompleted)
                    status = string.IsNullOrEmpty(recorder.last_error) ? "MP4保存完了: " + recorder.options.output_path : recorder.last_error;
            }
            if (busy) Repaint();
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(window_title, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(busy))
                with_alpha = EditorGUILayout.Popup("出力形式", with_alpha ? 1 : 0,
                    new[] { "HEVC / MP4（アルファなし）", "ProRes 4444 / MOV（アルファ付き）" }) == 1;
            EditorGUILayout.HelpBox(with_alpha
                ? "透過背景をMOVに保存します。Vulkanが使えない場合はCPUへ切り替えます。画像はCPUへの読み戻しを経由します。音声なし。"
                : "NVIDIA / D3D11専用。画像をGPU内でNVENCへ渡し、停止後にMP4へ格納します。アルファなし・音声なし。", MessageType.Info);
            EditorGUILayout.LabelField(SystemInfo.graphicsDeviceName);
            using (new EditorGUI.DisabledScope(busy))
            {
                texture_source = (RenderTexture)EditorGUILayout.ObjectField("描画済み RenderTexture", texture_source, typeof(RenderTexture), false);
                if (texture_source == null) camera_source = (Camera)EditorGUILayout.ObjectField("カメラ (Built-in)", camera_source, typeof(Camera), true);
                options.width = EditorGUILayout.IntField("幅", options.width);
                options.height = EditorGUILayout.IntField("高さ", options.height);
                options.fps = EditorGUILayout.IntField("FPS", options.fps);
                if (with_alpha)
                {
                    prefer_vulkan = EditorGUILayout.Toggle("Vulkanを優先", prefer_vulkan);
                    export_png = EditorGUILayout.Toggle("停止後に透過PNG連番も出力", export_png);
                }
                else
                {
                    options.bitrate = Mathf.Max(0, EditorGUILayout.IntField("目標ビットレート (bps)", options.bitrate));
                    options.cq = EditorGUILayout.IntSlider("CQ (0: ビットレート優先)", options.cq, 0, 51);
                }
                options.flip_vertical = EditorGUILayout.Toggle("上下反転", options.flip_vertical);
                offline = EditorGUILayout.Toggle("ゲーム時間をFPS固定", offline);
                current_ffmpeg = EditorGUILayout.TextField("ffmpeg.exe", current_ffmpeg);
                output_directory = EditorGUILayout.TextField("出力フォルダー", output_directory);
            }
            if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Playモードで録画を開始してください。", MessageType.None);
            if (with_alpha) EditorGUILayout.HelpBox("ProResは大容量です。VulkanのMOVはプリマルチプライド、CPUはストレートアルファ。PNG出力時にストレートへ変換します。", MessageType.None);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || busy))
                if (GUILayout.Button(preparation != null ? "ProResの動作を確認中…" : "録画開始")) EditorApplication.delayCall += start;
            using (new EditorGUI.DisabledScope(!recording))
                if (GUILayout.Button(with_alpha ? "停止してMOVを保存" : "停止してMP4を保存")) EditorApplication.delayCall += stop;
            if (alpha_recorder != null)
            {
                EditorGUILayout.LabelField("フレーム", $"送信 {alpha_recorder.FramesPushed} / ドロップ {alpha_recorder.FramesDropped}");
                EditorGUILayout.LabelField("待機中", $"読戻し {alpha_recorder.PendingReadbacks} / 送信 {alpha_recorder.QueuedFrames}");
                EditorGUILayout.LabelField("生画像CPU読み戻し", $"{alpha_recorder.RawReadbackBytes / 1048576.0:F1} MiB");
            }
            else if (recorder != null && recorder.session != null)
            {
                var stats = recorder.session.stats;
                EditorGUILayout.LabelField("フレーム", $"取得 {stats.captured} / 圧縮 {stats.encoded} / 保存 {stats.written}");
                EditorGUILayout.LabelField("GPUバッファ待機", stats.backpressure.ToString());
                EditorGUILayout.LabelField("生画像CPU読み戻し", $"{stats.raw_readback_bytes} bytes");
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
            using (new EditorGUI.DisabledScope(busy || string.IsNullOrEmpty(last_movie) || !File.Exists(last_movie)))
                if (GUILayout.Button("直前のMOVから透過PNG連番を出力")) begin_png();
            if (GUILayout.Button("出力フォルダーを開く"))
            {
                try { Directory.CreateDirectory(output_directory); EditorUtility.RevealInFinder(output_directory); }
                catch (Exception e) { fail(e); }
            }
            EditorGUILayout.EndScrollView();
        }
        void start()
        {
            if (this == null || busy || !EditorApplication.isPlaying) return;
            try
            {
                if (gpu_recorder_session.active_sessions != 0 || AlphaCaptureRecorder.ActiveSessions != 0)
                    throw new InvalidOperationException("他の録画を停止してから開始してください。");
                if (options.width < 16 || options.height < 16 || options.width % 2 != 0 || options.height % 2 != 0 || options.fps < 1 || options.fps > 240)
                    throw new ArgumentException("幅・高さは16以上の偶数、FPSは1～240を指定してください。");
                if (texture_source == null && camera_source == null && Camera.main == null)
                    throw new ArgumentException("カメラまたはRenderTextureを指定してください。");
                if (!File.Exists(current_ffmpeg)) throw new FileNotFoundException("ffmpeg.exeを指定してください。");
                clear_recorders();
                EditorPrefs.SetString(key+"ffmpeg", options.ffmpeg_path);
                EditorPrefs.SetString(key+"alpha_ffmpeg", alpha_ffmpeg_path);
                EditorPrefs.SetString(key+"output", output_directory);
                if (with_alpha)
                {
                    string exe = current_ffmpeg; int width = options.width, height = options.height;
                    bool vulkan = prefer_vulkan;
                    status = "ProResの動作を確認しています…";
                    preparation = Task.Run(() => prepare_alpha(exe, width, height, vulkan));
                }
                else
                {
                    options.output_path = make_output("gpu_", ".mp4");
                    recorder = create_recorder<gpu_camera_recorder>();
                    recorder.target_camera = source_camera; recorder.source_texture = texture_source;
                    recorder.options = options; recorder.offline_mode = offline;
                    recorder.begin(); status = "録画中: HEVC / GPU直接入力";
                }
            }
            catch (Exception e) { clear_recorders(); fail(e); }
            Repaint();
        }
        public static prepared_prores prepare_alpha(string exe, int width, int height, bool prefer_gpu)
        {
            var probe = FfmpegEnvProbe.Run(exe, false, prefer_gpu);
            if (!probe.Found) throw new FileNotFoundException(probe.error);
            bool vulkan = false; string reason = probe.error;
            if (prefer_gpu && probe.vulkan_prores_ok)
                vulkan = FfmpegEnvProbe.PreflightVulkanProres(probe.resolved_path, width, height, out reason);
            if (!vulkan && !probe.has_prores_ks) throw new NotSupportedException("使用できるProResエンコーダーがありません。 " + reason);
            return new prepared_prores { executable = probe.resolved_path, vulkan = vulkan,
                note = vulkan ? "Vulkan" : prefer_gpu ? "CPU（Vulkanを利用できないため切り替え） " + reason : "CPU（指定）" };
        }
        public sealed class prepared_prores { public string executable, note; public bool vulkan; }
        Camera source_camera => camera_source != null ? camera_source : Camera.main;
        string make_output(string prefix, string suffix) => Path.Combine(Path.GetFullPath(output_directory), prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + suffix);
        static T create_recorder<T>() where T : Component => new GameObject("d9_capture") { hideFlags = HideFlags.DontSave }.AddComponent<T>();
        void begin_alpha(prepared_prores prepared)
        {
            try
            {
                last_movie = make_output("alpha_", ".mov"); last_ffmpeg = prepared.executable; last_premultiplied = prepared.vulkan;
                alpha_recorder = create_recorder<AlphaCaptureRecorder>();
                alpha_recorder.Finished += on_alpha_finished;
                alpha_recorder.StartRecording(new AlphaCaptureRecorder.Config {
                    target_camera = source_camera, source_texture = texture_source, width = options.width, height = options.height,
                    fps = options.fps, alpha = true, offline_mode = offline, force_ldr = true, flip_vertical = options.flip_vertical, max_queue = 8
                }, prepared.executable, prores_encoding.arguments(options.width, options.height, options.fps, prepared.vulkan, last_movie), last_movie);
                File.WriteAllText(last_movie + ".format.json", JsonUtility.ToJson(new alpha_format { encoder = prepared.vulkan ? "prores_ks_vulkan" : "prores_ks", premultiplied = prepared.vulkan, ffmpeg = prepared.executable }, true));
                status = "録画中: ProRes 4444 / " + prepared.note;
            }
            catch (Exception e) { clear_recorders(); fail(e); }
            Repaint();
        }
        [Serializable] sealed class alpha_format { public string encoder, ffmpeg; public bool premultiplied; }
        void on_alpha_finished(int code, string path, string stderr)
        {
            status = code == 0 ? "MOV保存完了: " + path : "ProRes録画に失敗しました: " + stderr;
            if (code == 0 && export_png && alpha_recorder.FramesPushed > 0) begin_png();
            Repaint();
        }
        void begin_png()
        {
            try { png_task = prores_encoding.export_png(last_ffmpeg, last_movie, last_premultiplied); status = "MOV保存済み。透過PNG連番を出力中…"; }
            catch (Exception e) { fail(e); }
        }
        void stop()
        {
            preparation = null;
            try
            {
                if (recorder != null && recorder.is_recording) { recorder.stop(); status = "MP4に格納中: " + recorder.options.output_path; }
                if (alpha_recorder != null && alpha_recorder.IsRecording) alpha_recorder.StopRecording();
            }
            catch (Exception e) { fail(e); }
            Repaint();
        }
        void clear_recorders()
        {
            stop();
            if (recorder != null) DestroyImmediate(recorder.gameObject);
            if (alpha_recorder != null) { alpha_recorder.Finished -= on_alpha_finished; DestroyImmediate(alpha_recorder.gameObject); }
            recorder = null; alpha_recorder = null;
        }
        void fail(Exception e) { status = e.GetBaseException().Message; Debug.LogError("[GPU / Alpha Recorder] " + status); Repaint(); }
    }
}
