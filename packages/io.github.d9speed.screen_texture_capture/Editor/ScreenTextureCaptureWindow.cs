using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace D9speed.ScreenTextureCapture.Editor
{
    /// <summary>外部のエディタコードから現在のライブTextureを参照するための窓口。</summary>
    public static class ScreenTextureCaptureBridge
    {
        public static Texture2D CurrentTexture { get; internal set; }
    }

    /// <summary>
    /// Unityエディタが非フォーカスのときはメッセージ待ちでメインループが止まり、
    /// EditorApplication.updateが呼ばれなくなる(Interaction Modeでは回避不可)。
    /// バックグラウンドスレッドからWM_NULLを定期送信してループを起こし続ける。
    /// </summary>
    sealed class EditorBackgroundPump : IDisposable
    {
        const uint wm_null = 0x0000;

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

        readonly Thread thread;
        volatile bool running = true;

        public EditorBackgroundPump(int interval_milliseconds)
        {
            int interval = Math.Max(4, interval_milliseconds);
            thread = new Thread(() =>
            {
                IntPtr window_handle = IntPtr.Zero;
                while (running)
                {
                    if (window_handle == IntPtr.Zero)
                    {
                        try { window_handle = Process.GetCurrentProcess().MainWindowHandle; }
                        catch { }
                    }
                    if (window_handle != IntPtr.Zero)
                        PostMessage(window_handle, wm_null, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(interval);
                }
            })
            {
                IsBackground = true,
                Name = "D9speed Editor Background Pump",
            };
            thread.Start();
        }

        public void Dispose()
        {
            running = false;
            try { thread.Join(200); } catch { }
        }
    }

    public sealed class ScreenTextureCaptureWindow : EditorWindow
    {
        const string menu_path = "D9speed/Tools/Screen Texture Capture";
        const string pref_prefix = "D9speed.ScreenTextureCapture.";
        const string default_ffmpeg = "";
        const double first_frame_timeout_seconds = 3.0;
        const double fps_evaluation_seconds = 5.0;

        static readonly List<string> backend_labels = new List<string>
        {
            "自動（DXGI → GDI）",
            "DXGI Desktop Duplication",
            "GDI",
        };
        static readonly List<string> fps_labels = new List<string> { "10", "15", "30", "60" };

        TextField ffmpeg_field;
        DropdownField backend_field;
        IntegerField display_field;
        IntegerField x_field;
        IntegerField y_field;
        IntegerField width_field;
        IntegerField height_field;
        DropdownField fps_field;
        Toggle mouse_field;
        Toggle flip_field;
        Toggle auto_fps_field;
        Label environment_label;
        Label status_label;
        Label metrics_label;
        Label point_capture_label;
        Button start_button;
        Button stop_button;
        Button assign_button;
        Button drag_region_button;
        Button top_left_button;
        Button bottom_right_button;
        Image preview_image;
        ObjectField material_field;
        TextField property_field;

        FfmpegScreenCaptureSession session;
        EditorBackgroundPump background_pump;
        ScreenCaptureConfiguration active_configuration;
        ScreenCaptureBackend active_backend;
        Texture2D live_texture;
        long copied_version;
        bool automatic_backend_fallback;
        bool automatic_fps_fallback_used;
        double first_frame_deadline;
        double fps_evaluation_deadline;
        long evaluation_received;
        long evaluation_dropped;
        double metric_started_at;
        int metric_applied_frames;
        string persistent_status = "停止中";
        MessageType persistent_status_type = MessageType.Info;

        Material bound_material;
        string bound_property;
        Texture previous_texture;

        EditorApplication.CallbackFunction update_callback;
        double point_capture_deadline;
        bool capture_bottom_right;
        Process region_picker_process;
        Task<string> region_picker_stdout_task;
        Task<string> region_picker_stderr_task;

        [MenuItem(menu_path)]
        static void Open()
        {
            var window = GetWindow<ScreenTextureCaptureWindow>();
            window.titleContent = new GUIContent("Screen Texture Capture");
            window.minSize = new Vector2(520f, 660f);
        }

        void OnEnable()
        {
            update_callback = OnEditorUpdate;
            EditorApplication.update += update_callback;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        void OnDisable()
        {
            EditorApplication.update -= update_callback;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            Shutdown();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10;
            rootVisualElement.style.paddingRight = 10;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            rootVisualElement.Add(scroll);

            var title = new Label("画像編集ソフト → Unity ライブTexture");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.marginBottom = 6;
            scroll.Add(title);

            scroll.Add(CreateEnvironmentSection());
            scroll.Add(CreateCaptureSection());
            scroll.Add(CreateOutputSection());
            scroll.Add(CreateControlSection());

            LoadPreferences();
            RefreshEnvironmentLabel();
            RefreshControls();
        }

        VisualElement CreateEnvironmentSection()
        {
            var section = CreateSection("FFmpeg");
            ffmpeg_field = new TextField("ffmpeg.exe");
            ffmpeg_field.RegisterValueChangedCallback(_ => { SavePreferences(); RefreshEnvironmentLabel(); });
            section.Add(ffmpeg_field);

            var row = CreateRow();
            var browse = new Button(BrowseFfmpeg) { text = "参照..." };
            var check = new Button(RefreshEnvironmentLabel) { text = "存在確認" };
            row.Add(browse);
            row.Add(check);
            section.Add(row);

            environment_label = new Label();
            environment_label.style.whiteSpace = WhiteSpace.Normal;
            environment_label.style.marginTop = 3;
            section.Add(environment_label);
            return section;
        }

        VisualElement CreateCaptureSection()
        {
            var section = CreateSection("キャプチャ設定");
            backend_field = new DropdownField("方式", backend_labels, 0);
            display_field = new IntegerField("DXGIディスプレイ番号");
            section.Add(backend_field);
            section.Add(display_field);

            var region = CreateRow();
            x_field = CreateCompactIntegerField("X", 0);
            y_field = CreateCompactIntegerField("Y", 0);
            width_field = CreateCompactIntegerField("幅", 1024);
            height_field = CreateCompactIntegerField("高さ", 1024);
            region.Add(x_field);
            region.Add(y_field);
            region.Add(width_field);
            region.Add(height_field);
            section.Add(region);

            drag_region_button = new Button(StartRegionPicker) { text = "画面上でドラッグして矩形選択" };
            drag_region_button.style.height = 28;
            drag_region_button.style.marginTop = 4;
            drag_region_button.style.marginBottom = 3;
            section.Add(drag_region_button);

            var point_row = CreateRow();
            top_left_button = new Button(() => BeginPointCapture(false)) { text = "3秒後のマウス位置を左上に設定" };
            bottom_right_button = new Button(() => BeginPointCapture(true)) { text = "3秒後のマウス位置を右下に設定" };
            point_row.Add(top_left_button);
            point_row.Add(bottom_right_button);
            section.Add(point_row);

            point_capture_label = new Label("座標はWindowsデスクトップの物理ピクセルです。");
            point_capture_label.style.whiteSpace = WhiteSpace.Normal;
            section.Add(point_capture_label);

            fps_field = new DropdownField("FPS", fps_labels, fps_labels.Count - 1);
            mouse_field = new Toggle("マウスカーソルを含める") { value = false };
            flip_field = new Toggle("Unity向けに上下反転") { value = true };
            auto_fps_field = new Toggle("高負荷時に60 → 30 FPSへ安全に切替") { value = true };
            section.Add(fps_field);
            section.Add(mouse_field);
            section.Add(flip_field);
            section.Add(auto_fps_field);

            RegisterSaveCallback(backend_field);
            RegisterSaveCallback(display_field);
            RegisterSaveCallback(x_field);
            RegisterSaveCallback(y_field);
            RegisterSaveCallback(width_field);
            RegisterSaveCallback(height_field);
            RegisterSaveCallback(fps_field);
            RegisterSaveCallback(mouse_field);
            RegisterSaveCallback(flip_field);
            RegisterSaveCallback(auto_fps_field);
            return section;
        }

        VisualElement CreateOutputSection()
        {
            var section = CreateSection("出力");
            var help = new Label("ライブTextureはエディタ内の一時オブジェクトです。停止時に破棄されます。");
            help.style.whiteSpace = WhiteSpace.Normal;
            section.Add(help);

            material_field = new ObjectField("対象Material")
            {
                objectType = typeof(Material),
                allowSceneObjects = true,
            };
            property_field = new TextField("Textureプロパティ") { value = "_MainTex" };
            assign_button = new Button(AssignTextureToMaterial) { text = "ライブTextureを一時割り当て" };
            section.Add(material_field);
            section.Add(property_field);
            section.Add(assign_button);

            preview_image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
            };
            preview_image.style.height = 300;
            preview_image.style.marginTop = 6;
            preview_image.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            section.Add(preview_image);
            return section;
        }

        VisualElement CreateControlSection()
        {
            var section = CreateSection("実行");
            var row = CreateRow();
            start_button = new Button(StartCapture) { text = "キャプチャ開始" };
            stop_button = new Button(() => StopCapture("停止しました。", MessageType.Info)) { text = "停止" };
            start_button.style.flexGrow = 1;
            stop_button.style.flexGrow = 1;
            row.Add(start_button);
            row.Add(stop_button);
            section.Add(row);

            status_label = new Label();
            status_label.style.whiteSpace = WhiteSpace.Normal;
            status_label.style.marginTop = 5;
            metrics_label = new Label();
            metrics_label.style.whiteSpace = WhiteSpace.Normal;
            section.Add(status_label);
            section.Add(metrics_label);
            return section;
        }

        static VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.style.marginBottom = 8;
            section.style.paddingLeft = 7;
            section.style.paddingRight = 7;
            section.style.paddingTop = 5;
            section.style.paddingBottom = 6;
            section.style.borderBottomWidth = 1;
            section.style.borderTopWidth = 1;
            section.style.borderLeftWidth = 1;
            section.style.borderRightWidth = 1;
            var border = new Color(0.3f, 0.3f, 0.3f, 1f);
            section.style.borderBottomColor = border;
            section.style.borderTopColor = border;
            section.style.borderLeftColor = border;
            section.style.borderRightColor = border;

            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4;
            section.Add(label);
            return section;
        }

        static VisualElement CreateRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 3;
            row.style.marginBottom = 3;
            return row;
        }

        static IntegerField CreateCompactIntegerField(string label, int value)
        {
            var field = new IntegerField(label) { value = value };
            field.style.flexGrow = 1;
            field.style.minWidth = 100;
            field.style.marginRight = 4;
            return field;
        }

        void StartCapture()
        {
            if (session != null) return;
            if (!TryBuildConfiguration(out ScreenCaptureConfiguration configuration, out string error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            automatic_backend_fallback = backend_field.index == 0;
            automatic_fps_fallback_used = false;
            ScreenCaptureBackend backend = SelectInitialBackend(configuration);
            StartSession(configuration, backend, "キャプチャを開始しています...");
        }

        void StartSession(ScreenCaptureConfiguration configuration, ScreenCaptureBackend backend, string status)
        {
            DisposeSessionOnly();
            active_configuration = configuration;
            active_backend = backend;
            copied_version = 0;

            try
            {
                EnsureTexture(configuration.width, configuration.height);
                session = new FfmpegScreenCaptureSession();
                session.Start(configuration, backend);
                background_pump = new EditorBackgroundPump(1000 / (configuration.fps * 2));
                first_frame_deadline = EditorApplication.timeSinceStartup + first_frame_timeout_seconds;
                fps_evaluation_deadline = EditorApplication.timeSinceStartup + fps_evaluation_seconds;
                evaluation_received = 0;
                evaluation_dropped = 0;
                metric_started_at = EditorApplication.timeSinceStartup;
                metric_applied_frames = 0;
                SetStatus(status, MessageType.Info);
            }
            catch (Exception exception)
            {
                DisposeSessionOnly();
                SetStatus("開始失敗: " + exception.Message, MessageType.Error);
            }
            RefreshControls();
        }

        void OnEditorUpdate()
        {
            UpdateRegionPicker();
            UpdatePointCapture();
            if (session == null) return;

            if (session.CopyLatestFrame(live_texture.GetRawTextureData<byte>(), ref copied_version))
            {
                live_texture.Apply(false, false);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                metric_applied_frames++;
                Repaint();
            }

            double now = EditorApplication.timeSinceStartup;
            if (!session.HasReceivedFrame && (session.CaptureEnded || now >= first_frame_deadline))
            {
                HandleBackendFailure(session.ErrorSummary);
                return;
            }

            if (session.HasReceivedFrame && now >= fps_evaluation_deadline)
            {
                EvaluateAutomaticFpsFallback(now);
            }

            UpdateMetrics(now);
        }

        void HandleBackendFailure(string error)
        {
            if (automatic_backend_fallback && active_backend == ScreenCaptureBackend.DdaGrab)
            {
                var fallback_configuration = CloneConfiguration(active_configuration);
                StartSession(fallback_configuration, ScreenCaptureBackend.GdiGrab,
                    "DXGIを利用できないためGDIへ切り替えました。\n" + error);
                return;
            }

            StopCapture("キャプチャ失敗: " + error, MessageType.Error);
        }

        void EvaluateAutomaticFpsFallback(double now)
        {
            long received = session.ReceivedFrames;
            long dropped = session.DroppedFrames;
            long received_delta = received - evaluation_received;
            long dropped_delta = dropped - evaluation_dropped;

            evaluation_received = received;
            evaluation_dropped = dropped;
            fps_evaluation_deadline = now + fps_evaluation_seconds;

            if (!auto_fps_field.value || automatic_fps_fallback_used || active_configuration.fps != 60) return;
            if (received_delta < 60 || dropped_delta * 4 <= received_delta) return;

            automatic_fps_fallback_used = true;
            var fallback_configuration = CloneConfiguration(active_configuration);
            fallback_configuration.fps = 30;
            StartSession(fallback_configuration, active_backend,
                "フレーム破棄が多いため30 FPSへ安全に切り替えました。");
        }

        void UpdateMetrics(double now)
        {
            double elapsed = now - metric_started_at;
            if (elapsed < 0.5) return;
            double applied_fps = metric_applied_frames / Math.Max(0.001, elapsed);
            metrics_label.text =
                $"方式: {BackendName(active_backend)} / 入力: {active_configuration.fps} FPS / " +
                $"Unity更新: {applied_fps:F1} FPS / 受信: {session.ReceivedFrames} / 破棄: {session.DroppedFrames}";
            if (elapsed >= 1.0)
            {
                metric_started_at = now;
                metric_applied_frames = 0;
            }
        }

        void StopCapture(string message, MessageType type)
        {
            DisposeSessionOnly();
            RestoreBoundMaterial();
            DisposeTexture();
            SetStatus(message, type);
            if (metrics_label != null) metrics_label.text = string.Empty;
            RefreshControls();
        }

        void DisposeSessionOnly()
        {
            background_pump?.Dispose();
            background_pump = null;
            session?.Dispose();
            session = null;
        }

        void EnsureTexture(int width, int height)
        {
            if (live_texture != null && live_texture.width == width && live_texture.height == height) return;

            RestoreBoundMaterial();
            DisposeTexture();
            live_texture = new Texture2D(width, height, TextureFormat.BGRA32, false, false)
            {
                name = "D9speed Live Screen Capture",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            ScreenTextureCaptureBridge.CurrentTexture = live_texture;
            if (preview_image != null) preview_image.image = live_texture;
        }

        void DisposeTexture()
        {
            if (preview_image != null) preview_image.image = null;
            if (ScreenTextureCaptureBridge.CurrentTexture == live_texture) ScreenTextureCaptureBridge.CurrentTexture = null;
            if (live_texture != null) DestroyImmediate(live_texture);
            live_texture = null;
        }

        void AssignTextureToMaterial()
        {
            if (live_texture == null)
            {
                SetStatus("キャプチャ開始後に割り当ててください。", MessageType.Warning);
                return;
            }

            var material = material_field.value as Material;
            string property_name = property_field.value?.Trim();
            if (material == null || string.IsNullOrEmpty(property_name) || !material.HasProperty(property_name))
            {
                SetStatus("Materialと実在するTextureプロパティを指定してください。", MessageType.Warning);
                return;
            }

            RestoreBoundMaterial();
            Undo.RecordObject(material, "Assign Live Screen Texture");
            bound_material = material;
            bound_property = property_name;
            previous_texture = material.GetTexture(property_name);
            material.SetTexture(property_name, live_texture);
            EditorUtility.SetDirty(material);
            SetStatus($"{material.name} の {property_name} に一時割り当てしました。停止時に元へ戻します。", MessageType.Info);
        }

        void RestoreBoundMaterial()
        {
            if (bound_material != null && !string.IsNullOrEmpty(bound_property))
            {
                try
                {
                    Undo.RecordObject(bound_material, "Restore Screen Texture");
                    bound_material.SetTexture(bound_property, previous_texture);
                    EditorUtility.SetDirty(bound_material);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[D9speed Screen Capture] Materialの復元に失敗: " + exception.Message);
                }
            }
            bound_material = null;
            bound_property = null;
            previous_texture = null;
        }

        bool TryBuildConfiguration(out ScreenCaptureConfiguration configuration, out string error)
        {
            configuration = null;
            error = null;
            string ffmpeg_path = ffmpeg_field.value?.Trim();
            if (string.IsNullOrEmpty(ffmpeg_path) || !File.Exists(ffmpeg_path))
            {
                error = "指定されたffmpeg.exeが見つかりません。";
                return false;
            }
            if (width_field.value < 16 || height_field.value < 16 || width_field.value > 8192 || height_field.value > 8192)
            {
                error = "幅と高さは16～8192pxで指定してください。";
                return false;
            }

            try { checked { _ = width_field.value * height_field.value * 4; } }
            catch (OverflowException)
            {
                error = "解像度が大きすぎます。";
                return false;
            }

            configuration = new ScreenCaptureConfiguration
            {
                ffmpeg_path = ffmpeg_path,
                x = x_field.value,
                y = y_field.value,
                dxgi_x = x_field.value,
                dxgi_y = y_field.value,
                width = width_field.value,
                height = height_field.value,
                fps = int.TryParse(fps_field.value, out int selected_fps) ? selected_fps : 60,
                draw_mouse = mouse_field.value,
                flip_vertical = flip_field.value,
                display_index = Math.Max(0, display_field.value),
            };

            if (TryResolveDxgiRegion(configuration.x, configuration.y, configuration.width, configuration.height,
                    out int display_index, out int local_x, out int local_y))
            {
                configuration.display_index = display_index;
                configuration.dxgi_x = local_x;
                configuration.dxgi_y = local_y;
                configuration.dxgi_region_valid = true;
                display_field.SetValueWithoutNotify(display_index);
                SavePreferences();
            }
            return true;
        }

        ScreenCaptureBackend SelectInitialBackend(ScreenCaptureConfiguration configuration)
        {
            if (backend_field.index == 2) return ScreenCaptureBackend.GdiGrab;
            if (backend_field.index == 1) return ScreenCaptureBackend.DdaGrab;
            return configuration.dxgi_region_valid
                ? ScreenCaptureBackend.DdaGrab
                : ScreenCaptureBackend.GdiGrab;
        }

        static ScreenCaptureConfiguration CloneConfiguration(ScreenCaptureConfiguration source)
        {
            return new ScreenCaptureConfiguration
            {
                ffmpeg_path = source.ffmpeg_path,
                x = source.x,
                y = source.y,
                dxgi_x = source.dxgi_x,
                dxgi_y = source.dxgi_y,
                width = source.width,
                height = source.height,
                fps = source.fps,
                draw_mouse = source.draw_mouse,
                flip_vertical = source.flip_vertical,
                display_index = source.display_index,
                dxgi_region_valid = source.dxgi_region_valid,
            };
        }

        void BeginPointCapture(bool bottom_right)
        {
            if (region_picker_process != null || session != null) return;
            capture_bottom_right = bottom_right;
            point_capture_deadline = EditorApplication.timeSinceStartup + 3.0;
            point_capture_label.text = bottom_right
                ? "3秒以内にキャプチャ範囲の右下へマウスを移動してください。"
                : "3秒以内にキャプチャ範囲の左上へマウスを移動してください。";
        }

        void UpdatePointCapture()
        {
            if (point_capture_deadline <= 0 || point_capture_label == null) return;
            double remaining = point_capture_deadline - EditorApplication.timeSinceStartup;
            if (remaining > 0)
            {
                point_capture_label.text = $"マウス位置を取得するまで {Math.Ceiling(remaining)} 秒...";
                return;
            }

            point_capture_deadline = 0;
            if (!GetCursorPos(out NativePoint point))
            {
                point_capture_label.text = "マウス位置を取得できませんでした。";
                return;
            }

            if (capture_bottom_right)
            {
                int new_width = point.x - x_field.value;
                int new_height = point.y - y_field.value;
                if (new_width < 16 || new_height < 16)
                {
                    point_capture_label.text = "右下は左上より右かつ下へ指定してください。";
                    return;
                }
                width_field.SetValueWithoutNotify(new_width);
                height_field.SetValueWithoutNotify(new_height);
                point_capture_label.text = $"右下を ({point.x}, {point.y}) に設定しました。";
            }
            else
            {
                x_field.SetValueWithoutNotify(point.x);
                y_field.SetValueWithoutNotify(point.y);
                point_capture_label.text = $"左上を ({point.x}, {point.y}) に設定しました。";
            }
            SavePreferences();
        }

        void StartRegionPicker()
        {
            if (region_picker_process != null || session != null) return;
            point_capture_deadline = 0;

            string script_path = ResolveRegionPickerScriptPath();
            if (string.IsNullOrEmpty(script_path) || !File.Exists(script_path))
            {
                SetStatus("矩形選択ヘルパーが見つかりません。", MessageType.Error);
                return;
            }

            string powershell_path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell_path)) powershell_path = "powershell.exe";

            region_picker_process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = powershell_path,
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Sta -File \"{script_path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };

            try
            {
                if (!region_picker_process.Start())
                    throw new InvalidOperationException("矩形選択プロセスを開始できませんでした。");
                region_picker_stdout_task = region_picker_process.StandardOutput.ReadToEndAsync();
                region_picker_stderr_task = region_picker_process.StandardError.ReadToEndAsync();
                SetStatus("画面上をドラッグして範囲を選択してください。Escまたは右クリックでキャンセルできます。", MessageType.Info);
            }
            catch (Exception exception)
            {
                DisposeRegionPicker(true);
                SetStatus("矩形選択を開始できません: " + exception.Message, MessageType.Error);
            }
            RefreshControls();
        }

        void UpdateRegionPicker()
        {
            if (region_picker_process == null) return;

            bool exited;
            try { exited = region_picker_process.HasExited; }
            catch (InvalidOperationException) { exited = true; }
            if (!exited || region_picker_stdout_task == null || !region_picker_stdout_task.IsCompleted ||
                region_picker_stderr_task == null || !region_picker_stderr_task.IsCompleted) return;

            string stdout = string.Empty;
            string stderr = string.Empty;
            int exit_code = -1;
            try
            {
                stdout = region_picker_stdout_task.Result?.Trim();
                stderr = region_picker_stderr_task.Result?.Trim();
                exit_code = region_picker_process.ExitCode;
            }
            catch (Exception exception)
            {
                stderr = exception.GetBaseException().Message;
            }
            DisposeRegionPicker(false);

            RegionPickerResult result = null;
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                string[] lines = stdout.Replace("\r", string.Empty).Split('\n');
                string json = lines[lines.Length - 1].Trim();
                try { result = JsonUtility.FromJson<RegionPickerResult>(json); }
                catch (Exception exception) { stderr = exception.Message; }
            }

            if (result?.cancelled == true)
            {
                SetStatus("矩形選択をキャンセルしました。", MessageType.Info);
            }
            else if (exit_code != 0 || result == null || !string.IsNullOrEmpty(result.error))
            {
                string detail = !string.IsNullOrEmpty(result?.error) ? result.error : Tail(stderr);
                SetStatus("矩形選択に失敗しました: " + detail, MessageType.Error);
            }
            else if (result.width < 16 || result.height < 16)
            {
                SetStatus("選択範囲が小さすぎます。16px以上の矩形を選択してください。", MessageType.Warning);
            }
            else
            {
                x_field.SetValueWithoutNotify(result.x);
                y_field.SetValueWithoutNotify(result.y);
                width_field.SetValueWithoutNotify(result.width);
                height_field.SetValueWithoutNotify(result.height);
                string display_detail = string.Empty;
                if (TryResolveDxgiRegion(result.x, result.y, result.width, result.height,
                        out int display_index, out int local_x, out int local_y))
                {
                    display_field.SetValueWithoutNotify(display_index);
                    display_detail = $" / DXGI画面{display_index}内 X={local_x}, Y={local_y}";
                }
                SavePreferences();
                SetStatus($"範囲を設定しました: X={result.x}, Y={result.y}, {result.width}×{result.height}px{display_detail}", MessageType.Info);
            }
            RefreshControls();
        }

        string ResolveRegionPickerScriptPath()
        {
            MonoScript mono_script = MonoScript.FromScriptableObject(this);
            string asset_path = AssetDatabase.GetAssetPath(mono_script);
            string asset_directory = Path.GetDirectoryName(asset_path);
            if (string.IsNullOrEmpty(asset_directory)) return null;
            string project_root = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(project_root)
                ? null
                : Path.GetFullPath(Path.Combine(project_root, asset_directory, "ScreenRegionPicker.ps1"));
        }

        void DisposeRegionPicker(bool terminate)
        {
            Process picker = region_picker_process;
            region_picker_process = null;
            region_picker_stdout_task = null;
            region_picker_stderr_task = null;
            if (picker == null) return;

            if (terminate)
            {
                try
                {
                    if (!picker.HasExited)
                    {
                        picker.Kill();
                        picker.WaitForExit(500);
                    }
                }
                catch { }
            }
            picker.Dispose();
        }

        static string Tail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "詳細なし";
            string[] lines = value.Replace("\r", string.Empty).Split('\n');
            return lines[lines.Length - 1].Trim();
        }

        void BrowseFfmpeg()
        {
            string current = ffmpeg_field.value;
            string directory = !string.IsNullOrEmpty(current) && File.Exists(current)
                ? Path.GetDirectoryName(current)
                : Application.dataPath;
            string selected = EditorUtility.OpenFilePanel("ffmpeg.exeを選択", directory, "exe");
            if (!string.IsNullOrEmpty(selected)) ffmpeg_field.value = selected;
        }

        void RefreshEnvironmentLabel()
        {
            if (environment_label == null || ffmpeg_field == null) return;
            string path = ffmpeg_field.value?.Trim();
            bool found = !string.IsNullOrEmpty(path) && File.Exists(path);
            environment_label.text = found
                ? "✓ FFmpegを確認しました。DXGIが利用できない環境ではGDIへ自動切替します。"
                : "✗ ffmpeg.exeが見つかりません。";
            environment_label.style.color = found
                ? new Color(0.45f, 0.85f, 0.5f)
                : new Color(1f, 0.45f, 0.4f);
        }

        void SetStatus(string message, MessageType type)
        {
            persistent_status = message;
            persistent_status_type = type;
            if (status_label == null) return;
            status_label.text = message;
            status_label.style.color = type == MessageType.Error
                ? new Color(1f, 0.45f, 0.4f)
                : type == MessageType.Warning
                    ? new Color(1f, 0.75f, 0.3f)
                    : Color.white;
        }

        void RefreshControls()
        {
            bool running = session != null;
            bool picking_region = region_picker_process != null;
            start_button?.SetEnabled(!running && !picking_region);
            stop_button?.SetEnabled(running);
            assign_button?.SetEnabled(running && live_texture != null);
            drag_region_button?.SetEnabled(!running && !picking_region);
            top_left_button?.SetEnabled(!running && !picking_region);
            bottom_right_button?.SetEnabled(!running && !picking_region);
            ffmpeg_field?.SetEnabled(!running && !picking_region);
            backend_field?.SetEnabled(!running && !picking_region);
            display_field?.SetEnabled(!running && !picking_region);
            x_field?.SetEnabled(!running && !picking_region);
            y_field?.SetEnabled(!running && !picking_region);
            width_field?.SetEnabled(!running && !picking_region);
            height_field?.SetEnabled(!running && !picking_region);
            fps_field?.SetEnabled(!running && !picking_region);
            mouse_field?.SetEnabled(!running && !picking_region);
            flip_field?.SetEnabled(!running && !picking_region);
            if (status_label != null) SetStatus(persistent_status, persistent_status_type);
        }

        void LoadPreferences()
        {
            ffmpeg_field.SetValueWithoutNotify(EditorPrefs.GetString(pref_prefix + "Ffmpeg", default_ffmpeg));
            backend_field.index = Mathf.Clamp(EditorPrefs.GetInt(pref_prefix + "Backend", 0), 0, backend_labels.Count - 1);
            display_field.SetValueWithoutNotify(EditorPrefs.GetInt(pref_prefix + "Display", 0));
            x_field.SetValueWithoutNotify(EditorPrefs.GetInt(pref_prefix + "X", 0));
            y_field.SetValueWithoutNotify(EditorPrefs.GetInt(pref_prefix + "Y", 0));
            width_field.SetValueWithoutNotify(EditorPrefs.GetInt(pref_prefix + "Width", 1024));
            height_field.SetValueWithoutNotify(EditorPrefs.GetInt(pref_prefix + "Height", 1024));
            int saved_fps_index = fps_labels.IndexOf(EditorPrefs.GetInt(pref_prefix + "FpsValue", 60).ToString());
            fps_field.index = saved_fps_index >= 0 ? saved_fps_index : fps_labels.Count - 1;
            mouse_field.SetValueWithoutNotify(EditorPrefs.GetBool(pref_prefix + "Mouse", false));
            flip_field.SetValueWithoutNotify(EditorPrefs.GetBool(pref_prefix + "Flip", true));
            auto_fps_field.SetValueWithoutNotify(EditorPrefs.GetBool(pref_prefix + "AutoFps", true));
        }

        void SavePreferences()
        {
            if (ffmpeg_field == null) return;
            EditorPrefs.SetString(pref_prefix + "Ffmpeg", ffmpeg_field.value ?? string.Empty);
            EditorPrefs.SetInt(pref_prefix + "Backend", backend_field.index);
            EditorPrefs.SetInt(pref_prefix + "Display", display_field.value);
            EditorPrefs.SetInt(pref_prefix + "X", x_field.value);
            EditorPrefs.SetInt(pref_prefix + "Y", y_field.value);
            EditorPrefs.SetInt(pref_prefix + "Width", width_field.value);
            EditorPrefs.SetInt(pref_prefix + "Height", height_field.value);
            if (int.TryParse(fps_field.value, out int fps_value)) EditorPrefs.SetInt(pref_prefix + "FpsValue", fps_value);
            EditorPrefs.SetBool(pref_prefix + "Mouse", mouse_field.value);
            EditorPrefs.SetBool(pref_prefix + "Flip", flip_field.value);
            EditorPrefs.SetBool(pref_prefix + "AutoFps", auto_fps_field.value);
        }

        void RegisterSaveCallback<T>(BaseField<T> field)
        {
            field.RegisterValueChangedCallback(_ => SavePreferences());
        }

        void Shutdown()
        {
            point_capture_deadline = 0;
            DisposeRegionPicker(true);
            DisposeSessionOnly();
            RestoreBoundMaterial();
            DisposeTexture();
        }

        static string BackendName(ScreenCaptureBackend backend)
        {
            return backend == ScreenCaptureBackend.DdaGrab ? "DXGI" : "GDI";
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out NativePoint point);

        static bool TryResolveDxgiRegion(int x, int y, int width, int height,
            out int display_index, out int local_x, out int local_y)
        {
            display_index = 0;
            local_x = x;
            local_y = y;
            if (width <= 0 || height <= 0) return false;

            NativeRect region;
            try
            {
                region = new NativeRect
                {
                    left = x,
                    top = y,
                    right = checked(x + width),
                    bottom = checked(y + height),
                };
            }
            catch (OverflowException)
            {
                return false;
            }

            IntPtr monitor = MonitorFromRect(ref region, 0);
            if (monitor == IntPtr.Zero) return false;

            var monitor_info = new MonitorInfoEx { size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref monitor_info)) return false;
            if (region.left < monitor_info.monitor.left || region.top < monitor_info.monitor.top ||
                region.right > monitor_info.monitor.right || region.bottom > monitor_info.monitor.bottom) return false;
            if (!TryParseDisplayIndex(monitor_info.device_name, out display_index)) return false;

            local_x = region.left - monitor_info.monitor.left;
            local_y = region.top - monitor_info.monitor.top;
            return true;
        }

        static bool TryParseDisplayIndex(string device_name, out int display_index)
        {
            display_index = 0;
            const string display_prefix = @"\\.\DISPLAY";
            if (string.IsNullOrEmpty(device_name) ||
                !device_name.StartsWith(display_prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string number = device_name.Substring(display_prefix.Length);
            if (!int.TryParse(number, out int display_number) || display_number < 1) return false;
            display_index = display_number - 1;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MonitorInfoEx
        {
            public int size;
            public NativeRect monitor;
            public NativeRect work;
            public uint flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string device_name;
        }

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitor_info);

        [Serializable]
        sealed class RegionPickerResult
        {
            public bool cancelled;
            public int x;
            public int y;
            public int width;
            public int height;
            public string error;
        }
    }
}
