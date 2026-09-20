using D9speed_BaseEditorUtils;
using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace D9speed.HumanoidRandomHandPose.Editor
{
    [Serializable]
    public struct HandPoseRandomSettings
    {
        public bool randomize_left;
        public bool randomize_right;
        public Vector2 finger_cluster_range;
        public Vector2 thumb_range;
        public Vector2 spread_range;
        public float variation;
        public float interval_min;
        public float interval_max;
        public float blend_duration;
        public int random_seed;

        public HandPoseRandomSettings Sanitized()
        {
            var result = this;
            result.finger_cluster_range = SortAndClamp(result.finger_cluster_range);
            result.thumb_range = SortAndClamp(result.thumb_range);
            result.spread_range = SortAndClamp(result.spread_range);
            result.variation = Mathf.Clamp(result.variation, 0f, 1f);
            result.interval_min = Mathf.Max(0.05f, result.interval_min);
            result.interval_max = Mathf.Max(result.interval_min, result.interval_max);
            result.blend_duration = Mathf.Max(0f, result.blend_duration);
            return result;
        }

        private static Vector2 SortAndClamp(Vector2 range)
        {
            float min = Mathf.Clamp(Mathf.Min(range.x, range.y), -1f, 1f);
            float max = Mathf.Clamp(Mathf.Max(range.x, range.y), -1f, 1f);
            return new Vector2(min, max);
        }
    }

    [ExecuteAlways]
    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    internal sealed class HumanoidRandomHandPoseDriver : MonoBehaviour
    {
        private enum MuscleGroup
        {
            None,
            FingerCurl,
            ThumbCurl,
            FingerSpread,
            ThumbSpread,
        }

        private struct MuscleBinding
        {
            public int index;
            public bool is_left;
            public MuscleGroup group;
        }

        private Animator target_animator;
        private HumanPoseHandler pose_handler;
        private HandPoseRandomSettings settings;
        private readonly List<MuscleBinding> muscle_bindings = new List<MuscleBinding>();
        private float[] original_muscles;
        private float[] start_muscles;
        private float[] current_muscles;
        private float[] target_muscles;
        private Vector3 body_position;
        private Quaternion body_rotation;
        private System.Random random;
        private double blend_started_at;
        private double next_target_at;
        private bool initialized;
        private bool run_in_play_mode;

        public Animator TargetAnimator => target_animator;
        public bool IsInitialized => initialized;
        public int BoundMuscleCount => muscle_bindings.Count;

        public bool Initialize(
            Animator animator,
            HandPoseRandomSettings new_settings,
            bool play_mode,
            out string error)
        {
            DisposeHandler();
            initialized = false;
            target_animator = animator;
            settings = new_settings.Sanitized();
            run_in_play_mode = play_mode;

            if (!ValidateAnimator(animator, out error))
            {
                return false;
            }

            try
            {
                pose_handler = new HumanPoseHandler(animator.avatar, animator.transform);
                HumanPose original_pose = default;
                pose_handler.GetHumanPose(ref original_pose);
                if (original_pose.muscles == null || original_pose.muscles.Length != HumanTrait.MuscleCount)
                {
                    error = "Humanoidマッスルを取得できませんでした。Avatar設定を確認してください。";
                    DisposeHandler();
                    return false;
                }

                original_muscles = (float[])original_pose.muscles.Clone();
                start_muscles = new float[HumanTrait.MuscleCount];
                current_muscles = new float[HumanTrait.MuscleCount];
                target_muscles = new float[HumanTrait.MuscleCount];
                body_position = original_pose.bodyPosition;
                body_rotation = original_pose.bodyRotation;
                BuildMuscleBindings();

                if (muscle_bindings.Count == 0)
                {
                    error = "指のHumanoidマッスルが見つかりませんでした。指ボーンのマッピングを確認してください。";
                    DisposeHandler();
                    return false;
                }

                int seed = settings.random_seed != 0
                    ? settings.random_seed
                    : unchecked(Environment.TickCount ^ animator.GetHashCode());
                random = new System.Random(seed);
                double now = CurrentTime();
                GenerateTarget(now, true);
                ApplyPose();
                initialized = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"初期化に失敗しました: {exception.Message}";
                DisposeHandler();
                return false;
            }
        }

        public void UpdateSettings(HandPoseRandomSettings new_settings)
        {
            settings = new_settings.Sanitized();
        }

        public void Tick(double now)
        {
            if (!initialized || target_animator == null || pose_handler == null)
            {
                return;
            }

            UpdateInterpolatedPose(now);
            if (now >= next_target_at)
            {
                GenerateTarget(now, false);
                UpdateInterpolatedPose(now);
            }
            ApplyPose();
        }

        public void GenerateImmediately()
        {
            if (!initialized)
            {
                return;
            }

            double now = CurrentTime();
            UpdateInterpolatedPose(now);
            GenerateTarget(now, false);
            UpdateInterpolatedPose(now);
            ApplyPose();
        }

        public void SnapToTarget()
        {
            if (!initialized)
            {
                return;
            }

            Array.Copy(target_muscles, current_muscles, target_muscles.Length);
            Array.Copy(target_muscles, start_muscles, target_muscles.Length);
            ApplyPose();
        }

        public void RestoreOriginalPose()
        {
            if (pose_handler == null || original_muscles == null || target_animator == null)
            {
                return;
            }

            var pose = new HumanPose
            {
                bodyPosition = body_position,
                bodyRotation = body_rotation,
                muscles = original_muscles,
            };
            pose_handler.SetHumanPose(ref pose);
        }

        private void LateUpdate()
        {
            if (run_in_play_mode && Application.isPlaying)
            {
                Tick(Time.realtimeSinceStartupAsDouble);
            }
        }

        private void OnDestroy()
        {
            DisposeHandler();
        }

        private void BuildMuscleBindings()
        {
            muscle_bindings.Clear();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string muscle_name = HumanTrait.MuscleName[i];
                if (string.IsNullOrEmpty(muscle_name))
                {
                    continue;
                }

                string lower_name = muscle_name.ToLowerInvariant();
                bool is_left = lower_name.Contains("left");
                bool is_right = lower_name.Contains("right");
                if (!is_left && !is_right)
                {
                    continue;
                }

                MuscleGroup group = GetMuscleGroup(lower_name);
                if (group == MuscleGroup.None)
                {
                    continue;
                }

                muscle_bindings.Add(new MuscleBinding
                {
                    index = i,
                    is_left = is_left,
                    group = group,
                });
            }
        }

        private static MuscleGroup GetMuscleGroup(string lower_name)
        {
            bool is_thumb = lower_name.Contains("thumb");
            bool is_cluster_finger = lower_name.Contains("index")
                                     || lower_name.Contains("middle")
                                     || lower_name.Contains("ring")
                                     || lower_name.Contains("little");
            bool is_stretched = lower_name.Contains("stretched");
            bool is_spread = lower_name.Contains("spread");

            if (is_thumb && is_stretched)
            {
                return MuscleGroup.ThumbCurl;
            }
            if (is_thumb && is_spread)
            {
                return MuscleGroup.ThumbSpread;
            }
            if (is_cluster_finger && is_stretched)
            {
                return MuscleGroup.FingerCurl;
            }
            if (is_cluster_finger && is_spread)
            {
                return MuscleGroup.FingerSpread;
            }
            return MuscleGroup.None;
        }

        private void GenerateTarget(double now, bool from_rest)
        {
            if (from_rest)
            {
                Array.Clear(start_muscles, 0, start_muscles.Length);
                Array.Clear(current_muscles, 0, current_muscles.Length);
            }
            else
            {
                Array.Copy(current_muscles, start_muscles, current_muscles.Length);
            }
            Array.Clear(target_muscles, 0, target_muscles.Length);

            float left_cluster = RandomRange(settings.finger_cluster_range);
            float right_cluster = RandomRange(settings.finger_cluster_range);
            float left_thumb = RandomRange(settings.thumb_range);
            float right_thumb = RandomRange(settings.thumb_range);
            float left_spread = RandomRange(settings.spread_range);
            float right_spread = RandomRange(settings.spread_range);

            foreach (MuscleBinding binding in muscle_bindings)
            {
                bool enabled = binding.is_left ? settings.randomize_left : settings.randomize_right;
                if (!enabled)
                {
                    target_muscles[binding.index] = 0f;
                    continue;
                }

                float base_value;
                switch (binding.group)
                {
                    case MuscleGroup.FingerCurl:
                        base_value = binding.is_left ? left_cluster : right_cluster;
                        break;
                    case MuscleGroup.ThumbCurl:
                        base_value = binding.is_left ? left_thumb : right_thumb;
                        break;
                    case MuscleGroup.FingerSpread:
                    case MuscleGroup.ThumbSpread:
                        base_value = binding.is_left ? left_spread : right_spread;
                        break;
                    default:
                        base_value = 0f;
                        break;
                }

                target_muscles[binding.index] = Mathf.Clamp(
                    base_value + RandomRange(-settings.variation, settings.variation),
                    -1f,
                    1f);
            }

            blend_started_at = now;
            next_target_at = now + RandomRange(settings.interval_min, settings.interval_max);
        }

        private void UpdateInterpolatedPose(double now)
        {
            float t = settings.blend_duration <= 0f
                ? 1f
                : Mathf.Clamp01((float)((now - blend_started_at) / settings.blend_duration));
            t = t * t * (3f - 2f * t);

            for (int i = 0; i < current_muscles.Length; i++)
            {
                current_muscles[i] = Mathf.LerpUnclamped(start_muscles[i], target_muscles[i], t);
            }
        }

        private void ApplyPose()
        {
            var pose = new HumanPose
            {
                bodyPosition = body_position,
                bodyRotation = body_rotation,
                muscles = current_muscles,
            };
            pose_handler.SetHumanPose(ref pose);
        }

        private float RandomRange(Vector2 range)
        {
            return RandomRange(range.x, range.y);
        }

        private float RandomRange(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        private double CurrentTime()
        {
            return run_in_play_mode && Application.isPlaying
                ? Time.realtimeSinceStartupAsDouble
                : EditorApplication.timeSinceStartup;
        }

        private static bool ValidateAnimator(Animator animator, out string error)
        {
            if (animator == null)
            {
                error = "対象Animatorを指定してください。";
                return false;
            }
            if (animator.avatar == null || !animator.isHuman || !animator.avatar.isHuman)
            {
                error = "Humanoid Avatarが設定されたAnimatorのみ使用できます。";
                return false;
            }
            if (!animator.avatar.isValid)
            {
                error = "Avatarが無効です。Humanoidボーンマッピングを確認してください。";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void DisposeHandler()
        {
            if (pose_handler != null)
            {
                pose_handler.Dispose();
                pose_handler = null;
            }
        }
    }

    public sealed class HumanoidRandomHandPoseWindow : EditorWindow
    {
        private const string MenuPath = "D9speed/Animations/Random Hand Muscle Generator";

        [SerializeField] private Animator target_animator;
        [SerializeField] private string target_animator_id = "";
        [SerializeField] private string target_scene_name = "";
        [SerializeField] private string target_scene_path = "";
        [SerializeField] private bool edit_mode_enabled;
        [SerializeField] private bool play_mode_enabled = true;
        [SerializeField] private HandPoseRandomSettings settings =
            HumanoidRandomHandPoseApi.DefaultSettings;

        private HumanoidRandomHandPoseDriver driver;
        private Label status_label;
        private Button start_edit_button;
        private Button stop_keep_button;
        private Button stop_restore_button;
        private Button generate_button;
        private EditorApplication.CallbackFunction editor_update_callback;

        [MenuItem(MenuPath, false, 30)]
        private static void Open()
        {
            var window = GetWindow<HumanoidRandomHandPoseWindow>("Random Hand Muscles");
            window.minSize = new Vector2(430f, 520f);
        }

        public void CreateGUI()
        {
            UnregisterCallbacks();
            if (target_animator == null)
            {
                target_animator = ResolveTargetAnimator();
            }
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            var title = new Label("Humanoid Random Hand Muscle Generator");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14f;
            title.style.marginBottom = 6f;
            root.Add(title);

            var description = new HelpBox(
                "人差し指～小指を1クラスタ、親指を別クラスタとしてランダム化します。" +
                "指以外のHumanoidマッスルは0に固定され、Tポーズ／基準姿勢を維持します。",
                HelpBoxMessageType.Info);
            description.style.marginBottom = 8f;
            root.Add(description);

            var animator_field = new ObjectField("対象Animator")
            {
                objectType = typeof(Animator),
                allowSceneObjects = true,
                value = target_animator,
            };
            animator_field.RegisterValueChangedCallback(evt =>
            {
                if (driver != null)
                {
                    StopDriver(true);
                }
                target_animator = evt.newValue as Animator;
                RecordTargetAnimator(target_animator);
                RefreshUiState();
            });
            root.Add(animator_field);

            var mode_box = CreateBox();
            var edit_toggle = new Toggle("エディターモードで定期更新") { value = edit_mode_enabled };
            edit_toggle.RegisterValueChangedCallback(evt =>
            {
                edit_mode_enabled = evt.newValue;
                if (!EditorApplication.isPlaying)
                {
                    if (edit_mode_enabled)
                    {
                        StartDriver(false);
                    }
                    else
                    {
                        StopDriver(false);
                    }
                }
                RefreshUiState();
            });
            mode_box.Add(edit_toggle);

            var play_toggle = new Toggle("プレイモードで定期更新（PoseSync向け）")
            {
                value = play_mode_enabled,
            };
            play_toggle.RegisterValueChangedCallback(evt =>
            {
                play_mode_enabled = evt.newValue;
                if (EditorApplication.isPlaying)
                {
                    if (play_mode_enabled)
                    {
                        StartDriver(true);
                    }
                    else
                    {
                        StopDriver(true);
                    }
                }
                RefreshUiState();
            });
            mode_box.Add(play_toggle);
            root.Add(mode_box);

            var hand_box = CreateBox();
            AddSectionTitle(hand_box, "対象とマッスル値域");
            var left_toggle = new Toggle("左手") { value = settings.randomize_left };
            left_toggle.RegisterValueChangedCallback(evt => UpdateSettings(value =>
            {
                value.randomize_left = evt.newValue;
                return value;
            }));
            hand_box.Add(left_toggle);
            var right_toggle = new Toggle("右手") { value = settings.randomize_right };
            right_toggle.RegisterValueChangedCallback(evt => UpdateSettings(value =>
            {
                value.randomize_right = evt.newValue;
                return value;
            }));
            hand_box.Add(right_toggle);
            hand_box.Add(CreateRangeField(
                "4本クラスタ", settings.finger_cluster_range,
                value => UpdateSettings(data =>
                {
                    data.finger_cluster_range = value;
                    return data;
                })));
            hand_box.Add(CreateRangeField(
                "親指", settings.thumb_range,
                value => UpdateSettings(data =>
                {
                    data.thumb_range = value;
                    return data;
                })));
            hand_box.Add(CreateRangeField(
                "指の開き", settings.spread_range,
                value => UpdateSettings(data =>
                {
                    data.spread_range = value;
                    return data;
                })));
            hand_box.Add(CreateFloatField(
                "関節ごとの揺らぎ", settings.variation, 0f, 1f,
                value => UpdateSettings(data =>
                {
                    data.variation = value;
                    return data;
                })));
            root.Add(hand_box);

            var timing_box = CreateBox();
            AddSectionTitle(timing_box, "タイミング");
            timing_box.Add(CreateFloatField(
                "最短間隔（秒）", settings.interval_min, 0.05f, 60f,
                value => UpdateSettings(data =>
                {
                    data.interval_min = value;
                    return data;
                })));
            timing_box.Add(CreateFloatField(
                "最長間隔（秒）", settings.interval_max, 0.05f, 60f,
                value => UpdateSettings(data =>
                {
                    data.interval_max = value;
                    return data;
                })));
            timing_box.Add(CreateFloatField(
                "補間時間（秒）", settings.blend_duration, 0f, 60f,
                value => UpdateSettings(data =>
                {
                    data.blend_duration = value;
                    return data;
                })));
            var seed_field = new IntegerField("固定シード（0=毎回ランダム）") { value = settings.random_seed };
            seed_field.RegisterValueChangedCallback(evt => UpdateSettings(data =>
            {
                data.random_seed = evt.newValue;
                return data;
            }));
            timing_box.Add(seed_field);
            root.Add(timing_box);

            var button_row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            start_edit_button = new Button(() =>
            {
                edit_mode_enabled = true;
                edit_toggle.SetValueWithoutNotify(true);
                StartDriver(false);
                RefreshUiState();
            }) { text = "開始" };
            button_row.Add(start_edit_button);

            generate_button = new Button(() =>
            {
                if (driver == null)
                {
                    StartDriver(EditorApplication.isPlaying);
                }
                driver?.GenerateImmediately();
            }) { text = "今すぐランダム化" };
            button_row.Add(generate_button);
            root.Add(button_row);

            var stop_row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4f } };
            stop_keep_button = new Button(() =>
            {
                edit_mode_enabled = false;
                edit_toggle.SetValueWithoutNotify(false);
                StopDriver(false);
                RefreshUiState();
            }) { text = "停止（現在姿勢を保持）" };
            stop_row.Add(stop_keep_button);

            stop_restore_button = new Button(() =>
            {
                edit_mode_enabled = false;
                edit_toggle.SetValueWithoutNotify(false);
                StopDriver(true);
                RefreshUiState();
            }) { text = "停止して開始前へ戻す" };
            stop_row.Add(stop_restore_button);
            root.Add(stop_row);

            status_label = new Label();
            status_label.style.whiteSpace = WhiteSpace.Normal;
            status_label.style.marginTop = 8f;
            root.Add(status_label);

            RegisterCallbacks();
            RefreshUiState();
            EditorApplication.delayCall += RestoreRequestedMode;
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            StopDriver(true);
        }

        private void RestoreRequestedMode()
        {
            if (this == null || driver != null)
            {
                return;
            }
            if (EditorApplication.isPlaying ? play_mode_enabled : edit_mode_enabled)
            {
                StartDriver(EditorApplication.isPlaying);
            }
        }

        private void RegisterCallbacks()
        {
            editor_update_callback = OnEditorUpdate;
            EditorApplication.update += editor_update_callback;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void UnregisterCallbacks()
        {
            if (editor_update_callback != null)
            {
                EditorApplication.update -= editor_update_callback;
                editor_update_callback = null;
            }
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnEditorUpdate()
        {
            if (driver == null)
            {
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                driver.Tick(EditorApplication.timeSinceStartup);
                SceneView.RepaintAll();
            }
            RefreshUiState();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    RecordTargetAnimator(target_animator);
                    StopDriver(true);
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    target_animator = ResolveTargetAnimator();
                    if (play_mode_enabled)
                    {
                        StartDriver(true);
                    }
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    StopDriver(true);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    target_animator = ResolveTargetAnimator();
                    if (edit_mode_enabled)
                    {
                        StartDriver(false);
                    }
                    break;
            }
            RefreshUiState();
        }

        private void StartDriver(bool play_mode)
        {
            if (driver != null)
            {
                if (driver.TargetAnimator == target_animator)
                {
                    driver.UpdateSettings(settings);
                    return;
                }
                StopDriver(true);
            }

            if (target_animator == null)
            {
                SetStatus("対象Animatorを指定してください。", true);
                return;
            }

            if (!HumanoidRandomHandPoseApi.TryCreateDriver(
                    target_animator,
                    settings,
                    play_mode,
                    !play_mode,
                    out driver,
                    out string error))
            {
                driver = null;
                SetStatus(error, true);
                return;
            }

            SetStatus(
                $"{(play_mode ? "プレイ" : "エディター")}モードで実行中（指マッスル {driver.BoundMuscleCount} 個）。",
                false);
        }

        private void StopDriver(bool restore_original)
        {
            if (driver == null)
            {
                return;
            }
            HumanoidRandomHandPoseDriver stopping_driver = driver;
            driver = null;
            HumanoidRandomHandPoseApi.DestroyDriver(stopping_driver, restore_original);
            SetStatus(restore_original ? "停止し、開始前の姿勢へ戻しました。" : "停止しました。現在姿勢を保持しています。", false);
        }

        private void UpdateSettings(Func<HandPoseRandomSettings, HandPoseRandomSettings> update)
        {
            settings = update(settings).Sanitized();
            driver?.UpdateSettings(settings);
        }

        private void RecordTargetAnimator(Animator animator)
        {
            if (animator == null)
            {
                target_animator_id = "";
                target_scene_name = "";
                target_scene_path = "";
                return;
            }

            target_animator_id = GlobalObjectId.GetGlobalObjectIdSlow(animator).ToString();
            target_scene_name = animator.gameObject.scene.name;
            target_scene_path = HierarchyPathHelper.GetHierarchyPath(animator.transform);
        }

        private Animator ResolveTargetAnimator()
        {
            if (!string.IsNullOrEmpty(target_animator_id)
                && GlobalObjectId.TryParse(target_animator_id, out GlobalObjectId parsed)
                && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) is Animator resolved)
            {
                return resolved;
            }

            if (string.IsNullOrEmpty(target_scene_path))
            {
                return null;
            }

            for (int scene_index = 0; scene_index < SceneManager.sceneCount; scene_index++)
            {
                Scene scene = SceneManager.GetSceneAt(scene_index);
                if (!scene.isLoaded
                    || (!string.IsNullOrEmpty(target_scene_name) && scene.name != target_scene_name))
                {
                    continue;
                }

                Animator animator = FindAnimatorByPath(scene, target_scene_path);
                if (animator != null)
                {
                    return animator;
                }
            }
            return null;
        }

        private static Animator FindAnimatorByPath(Scene scene, string hierarchy_path)
        {
            int separator = hierarchy_path.IndexOf('/');
            string root_name = separator >= 0 ? hierarchy_path.Substring(0, separator) : hierarchy_path;
            string child_path = separator >= 0 ? hierarchy_path.Substring(separator + 1) : "";

            foreach (GameObject root_object in scene.GetRootGameObjects())
            {
                if (root_object.name != root_name)
                {
                    continue;
                }
                Transform target = string.IsNullOrEmpty(child_path)
                    ? root_object.transform
                    : root_object.transform.Find(child_path);
                if (target != null && target.TryGetComponent(out Animator animator))
                {
                    return animator;
                }
            }
            return null;
        }

        

        private void RefreshUiState()
        {
            bool running = driver != null && driver.IsInitialized;
            bool can_start_edit = !EditorApplication.isPlaying && !running;
            start_edit_button?.SetEnabled(can_start_edit);
            stop_keep_button?.SetEnabled(running && !EditorApplication.isPlaying);
            stop_restore_button?.SetEnabled(running);
            generate_button?.SetEnabled(target_animator != null);

            if (status_label != null && string.IsNullOrEmpty(status_label.text))
            {
                status_label.text = EditorApplication.isPlaying
                    ? (play_mode_enabled ? "プレイモード開始後に自動実行します。" : "プレイモード更新は無効です。")
                    : (edit_mode_enabled ? "エディターモード更新を開始します。" : "待機中です。");
            }
        }

        private void SetStatus(string message, bool is_error)
        {
            if (status_label == null)
            {
                return;
            }
            status_label.text = message;
            status_label.style.color = is_error ? new Color(1f, 0.45f, 0.4f) : StyleKeyword.Null;
        }

        private static VisualElement CreateBox()
        {
            return new VisualElement
            {
                style =
                {
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftColor = Color.gray,
                    borderRightColor = Color.gray,
                    borderTopColor = Color.gray,
                    borderBottomColor = Color.gray,
                    paddingLeft = 7f,
                    paddingRight = 7f,
                    paddingTop = 6f,
                    paddingBottom = 6f,
                    marginBottom = 7f,
                },
            };
        }

        private static void AddSectionTitle(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 3f;
            parent.Add(label);
        }

        private static VisualElement CreateRangeField(
            string label,
            Vector2 initial_value,
            Action<Vector2> changed)
        {
            var field = new MinMaxSlider(label, initial_value.x, initial_value.y, -1f, 1f);
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return field;
        }

        private static VisualElement CreateFloatField(
            string label,
            float initial_value,
            float min,
            float max,
            Action<float> changed)
        {
            var field = new FloatField(label) { value = initial_value };
            field.RegisterValueChangedCallback(evt => changed(Mathf.Clamp(evt.newValue, min, max)));
            return field;
        }
    }
}
