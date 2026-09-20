using D9speed_BaseEditorUtils;
using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace D9speed.HumanoidRandomHandPose.Editor
{
    [Serializable]
    public sealed class HumanoidRandomHandPoseResult
    {
        public bool success;
        public string animator_name = "";
        public int finger_muscle_count;
        public int session_id;
        public string message = "";
    }

    /// <summary>
    /// Public entry point for editor automation, uloop dynamic code, and Unity CLI.
    /// All methods must be called from Unity's main thread.
    /// </summary>
    public static class HumanoidRandomHandPoseApi
    {
        private sealed class PeriodicSession
        {
            public HumanoidRandomHandPoseDriver driver;
            public EditorApplication.CallbackFunction update_callback;
        }

        private static readonly Dictionary<int, PeriodicSession> sessions =
            new Dictionary<int, PeriodicSession>();
        private static int next_session_id = 1;

        [InitializeOnLoadMethod]
        private static void RegisterLifecycleCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static HandPoseRandomSettings DefaultSettings => new HandPoseRandomSettings
        {
            randomize_left = true,
            randomize_right = true,
            finger_cluster_range = new Vector2(-1f, 1f),
            thumb_range = new Vector2(-1f, 1f),
            spread_range = new Vector2(-0.25f, 0.25f),
            variation = 0.12f,
            interval_min = 0.8f,
            interval_max = 2f,
            blend_duration = 0.35f,
            random_seed = 0,
        };

        /// <summary>Apply one randomized hand pose and keep it.</summary>
        public static HumanoidRandomHandPoseResult ApplyOnce(
            Animator animator,
            HandPoseRandomSettings settings,
            bool record_undo = true)
        {
            if (!TryApplyOnce(animator, settings, record_undo, out HumanoidRandomHandPoseResult result))
            {
                throw new InvalidOperationException(result.message);
            }
            return result;
        }

        public static bool TryApplyOnce(
            Animator animator,
            HandPoseRandomSettings settings,
            bool record_undo,
            out HumanoidRandomHandPoseResult result)
        {
            result = CreateResult(animator);
            if (!TryCreateDriver(
                    animator,
                    settings,
                    Application.isPlaying,
                    record_undo && !Application.isPlaying,
                    out HumanoidRandomHandPoseDriver driver,
                    out string error))
            {
                result.message = error;
                return false;
            }

            result.finger_muscle_count = driver.BoundMuscleCount;
            driver.SnapToTarget();
            DestroyDriver(driver, false);
            MarkPoseDirty(animator);
            result.success = true;
            result.message = "ランダムな手ポーズを1回適用しました。";
            return true;
        }

        /// <summary>
        /// Start periodic randomization. In Edit Mode the API owns an
        /// EditorApplication.update callback; in Play Mode the driver runs in
        /// LateUpdate at execution order 9000, before PoseSync's order 10000.
        /// </summary>
        public static int StartPeriodic(
            Animator animator,
            HandPoseRandomSettings settings,
            bool record_undo = true)
        {
            if (!TryStartPeriodic(animator, settings, record_undo, out int session_id, out string error))
            {
                throw new InvalidOperationException(error);
            }
            return session_id;
        }

        public static bool TryStartPeriodic(
            Animator animator,
            HandPoseRandomSettings settings,
            bool record_undo,
            out int session_id,
            out string error)
        {
            session_id = 0;
            bool play_mode = Application.isPlaying;
            if (!TryCreateDriver(
                    animator,
                    settings,
                    play_mode,
                    record_undo && !play_mode,
                    out HumanoidRandomHandPoseDriver driver,
                    out error))
            {
                return false;
            }

            session_id = next_session_id++;
            var session = new PeriodicSession { driver = driver };
            if (!play_mode)
            {
                int captured_session_id = session_id;
                session.update_callback = () => TickEditModeSession(captured_session_id);
                EditorApplication.update += session.update_callback;
            }
            sessions.Add(session_id, session);
            return true;
        }

        public static bool GenerateNow(int session_id)
        {
            if (!TryGetSession(session_id, out PeriodicSession session))
            {
                return false;
            }
            session.driver.GenerateImmediately();
            return true;
        }

        public static bool UpdateSettings(int session_id, HandPoseRandomSettings settings)
        {
            if (!TryGetSession(session_id, out PeriodicSession session))
            {
                return false;
            }
            session.driver.UpdateSettings(settings);
            return true;
        }

        public static bool StopPeriodic(int session_id, bool restore_original = true)
        {
            if (!sessions.TryGetValue(session_id, out PeriodicSession session))
            {
                return false;
            }

            sessions.Remove(session_id);
            if (session.update_callback != null)
            {
                EditorApplication.update -= session.update_callback;
            }
            DestroyDriver(session.driver, restore_original);
            return true;
        }

        public static void StopAll(bool restore_original = true)
        {
            int[] session_ids = sessions.Keys.ToArray();
            foreach (int session_id in session_ids)
            {
                StopPeriodic(session_id, restore_original);
            }
        }

        /// <summary>
        /// Unity CLI adapter. Invoke with:
        /// -executeMethod D9speed.HumanoidRandomHandPose.Editor.HumanoidRandomHandPoseApi.RunFromCommandLine
        /// </summary>
        public static void RunFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string scene_path = GetOption(args, "--hand-pose-scene");
                string animator_path = GetOption(args, "--hand-pose-animator-path");
                Scene scene = OpenRequestedScene(scene_path);
                Animator animator = ResolveAnimator(scene, animator_path);
                HandPoseRandomSettings settings = ReadSettings(args);

                HumanoidRandomHandPoseResult result = ApplyOnce(animator, settings, false);
                if (HasFlag(args, "--hand-pose-save-scene"))
                {
                    if (string.IsNullOrEmpty(scene.path))
                    {
                        throw new InvalidOperationException("保存対象シーンにAssetパスがありません。");
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                    {
                        throw new InvalidOperationException($"シーンを保存できませんでした: {scene.path}");
                    }
                    result.message += $" シーンを保存しました: {scene.path}";
                }

                Debug.Log("HAND_POSE_RESULT " + JsonUtility.ToJson(result));
            }
            catch (Exception exception)
            {
                var result = new HumanoidRandomHandPoseResult
                {
                    success = false,
                    message = exception.Message,
                };
                Debug.LogError("HAND_POSE_RESULT " + JsonUtility.ToJson(result));
                throw;
            }
        }

        internal static bool TryCreateDriver(
            Animator animator,
            HandPoseRandomSettings settings,
            bool play_mode,
            bool record_undo,
            out HumanoidRandomHandPoseDriver driver,
            out string error)
        {
            driver = null;
            if (!ValidateAnimator(animator, out error))
            {
                return false;
            }

            if (record_undo)
            {
                RecordHumanoidUndo(animator);
            }

            var helper_object = new GameObject("Humanoid Random Hand Pose (Temporary)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (play_mode)
            {
                Object.DontDestroyOnLoad(helper_object);
            }

            driver = helper_object.AddComponent<HumanoidRandomHandPoseDriver>();
            if (driver.Initialize(animator, settings, play_mode, out error))
            {
                return true;
            }

            Object.DestroyImmediate(helper_object);
            driver = null;
            return false;
        }

        internal static void DestroyDriver(
            HumanoidRandomHandPoseDriver driver,
            bool restore_original)
        {
            if (driver == null)
            {
                return;
            }
            if (restore_original)
            {
                driver.RestoreOriginalPose();
            }
            Object.DestroyImmediate(driver.gameObject);
        }

        private static void TickEditModeSession(int session_id)
        {
            if (!TryGetSession(session_id, out PeriodicSession session))
            {
                return;
            }
            session.driver.Tick(EditorApplication.timeSinceStartup);
            SceneView.RepaintAll();
        }

        private static void OnBeforeAssemblyReload()
        {
            StopAll(true);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode
                || state == PlayModeStateChange.ExitingPlayMode)
            {
                StopAll(true);
            }
        }

        private static bool TryGetSession(int session_id, out PeriodicSession session)
        {
            if (sessions.TryGetValue(session_id, out session)
                && session.driver != null
                && session.driver.IsInitialized)
            {
                return true;
            }

            if (session != null && session.update_callback != null)
            {
                EditorApplication.update -= session.update_callback;
            }
            sessions.Remove(session_id);
            session = null;
            return false;
        }

        private static HumanoidRandomHandPoseResult CreateResult(Animator animator)
        {
            return new HumanoidRandomHandPoseResult
            {
                animator_name = animator != null ? animator.name : "",
            };
        }

        private static bool ValidateAnimator(Animator animator, out string error)
        {
            if (animator == null)
            {
                error = "対象Animatorがnullです。";
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

        private static void RecordHumanoidUndo(Animator animator)
        {
            var targets = new List<Object> { animator.transform };
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null && !targets.Contains(bone))
                {
                    targets.Add(bone);
                }
            }
            Undo.RegisterCompleteObjectUndo(targets.ToArray(), "Randomize Humanoid Hand Muscles");
        }

        private static void MarkPoseDirty(Animator animator)
        {
            if (Application.isPlaying || animator == null)
            {
                return;
            }

            EditorUtility.SetDirty(animator.transform);
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    EditorUtility.SetDirty(bone);
                }
            }
            if (animator.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
            }
        }

        private static Scene OpenRequestedScene(string scene_path)
        {
            if (!string.IsNullOrWhiteSpace(scene_path))
            {
                return EditorSceneManager.OpenScene(scene_path, OpenSceneMode.Single);
            }

            Scene active_scene = SceneManager.GetActiveScene();
            if (!active_scene.IsValid() || !active_scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "シーンが開かれていません。--hand-pose-scene を指定してください。");
            }
            return active_scene;
        }

        private static Animator ResolveAnimator(Scene scene, string hierarchy_path)
        {
            Animator[] humanoids = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Animator>(true))
                .Where(animator => animator != null
                                   && animator.isHuman
                                   && animator.avatar != null
                                   && animator.avatar.isValid)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(hierarchy_path))
            {
                Animator hit = humanoids.FirstOrDefault(
                    animator => HierarchyPathHelper.GetHierarchyPath(animator.transform) == hierarchy_path);
                if (hit == null)
                {
                    throw new InvalidOperationException(
                        $"指定されたAnimatorが見つかりません: {hierarchy_path}");
                }
                return hit;
            }

            if (humanoids.Length == 1)
            {
                return humanoids[0];
            }
            if (humanoids.Length == 0)
            {
                throw new InvalidOperationException("シーン内に有効なHumanoid Animatorがありません。");
            }
            throw new InvalidOperationException(
                "Humanoid Animatorが複数あります。--hand-pose-animator-path を指定してください。候補: " +
                string.Join(", ", humanoids.Select(animator => HierarchyPathHelper.GetHierarchyPath(animator.transform))));
        }

        private static HandPoseRandomSettings ReadSettings(string[] args)
        {
            HandPoseRandomSettings settings = DefaultSettings;
            settings.randomize_left = GetBoolOption(args, "--hand-pose-left", settings.randomize_left);
            settings.randomize_right = GetBoolOption(args, "--hand-pose-right", settings.randomize_right);
            settings.finger_cluster_range = GetRangeOption(
                args, "--hand-pose-finger-range", settings.finger_cluster_range);
            settings.thumb_range = GetRangeOption(args, "--hand-pose-thumb-range", settings.thumb_range);
            settings.spread_range = GetRangeOption(args, "--hand-pose-spread-range", settings.spread_range);
            settings.variation = GetFloatOption(args, "--hand-pose-variation", settings.variation);
            settings.random_seed = GetIntOption(args, "--hand-pose-seed", settings.random_seed);
            return settings.Sanitized();
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return "";
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool GetBoolOption(string[] args, string name, bool fallback)
        {
            string value = GetOption(args, name);
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static int GetIntOption(string[] args, string name, int fallback)
        {
            string value = GetOption(args, name);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static float GetFloatOption(string[] args, string name, float fallback)
        {
            string value = GetOption(args, name);
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static Vector2 GetRangeOption(string[] args, string name, Vector2 fallback)
        {
            string value = GetOption(args, name);
            string[] parts = value.Split(',');
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float min)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float max))
            {
                return fallback;
            }
            return new Vector2(min, max);
        }

    }
}
