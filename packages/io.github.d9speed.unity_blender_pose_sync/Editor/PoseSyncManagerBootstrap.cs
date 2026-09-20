using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityBlenderPoseSync.World;

namespace UnityBlenderPoseSync.World.Editor
{
    public enum PoseSyncCameraSource
    {
        CameraObject = 0,
        SceneView = 1,
    }

    /// <summary>One configured avatar. The Animator is stored as a GlobalObjectId so
    /// the reference survives the domain reload on entering Play Mode.</summary>
    [Serializable]
    public sealed class PoseSyncAvatarSetting
    {
        public string animator_id = "";
        // Entering Play Mode clones the scene, which invalidates the GlobalObjectId
        // of scene objects, so the hierarchy path is kept as the fallback that
        // actually resolves at runtime.
        public string scene_name = "";
        public string scene_path = "";
        public string avatar_name = "";
        public bool send = true;
        public bool include_all_phys_bones = true;
        public List<PoseSyncExtraRoot> extra_roots = new List<PoseSyncExtraRoot>();
    }

    /// <summary>
    /// A hierarchy the user picked by hand, streamed on top of the automatic
    /// collection. Stored by name and by path-relative-to-the-avatar because
    /// Modular Avatar's Merge Armature reparents and renames objects when Play Mode
    /// starts, which invalidates both GlobalObjectId and the authored path.
    /// </summary>
    [Serializable]
    public sealed class PoseSyncExtraRoot
    {
        public string bone_name = "";
        public string relative_path = "";
        public bool include_descendants = true;
        public bool send = true;
    }

    /// <summary>
    /// A garment authored in Blender as its own armature and merged onto an avatar by
    /// Modular Avatar. It streams as a separate avatar entry so the Blender side can
    /// point it at the garment's own armature.
    ///
    /// Merge Armature dissolves the garment's armature root and renames whatever it
    /// keeps to "&lt;original&gt;$&lt;guid&gt;", so the authored Transform cannot be followed
    /// into Play Mode. Instead the PhysBone chain roots are recorded by name before
    /// Play and looked up again afterwards.
    /// </summary>
    [Serializable]
    public sealed class PoseSyncClothSetting
    {
        public string avatar_name = "";
        public string owner_avatar_name = "";
        public string root_name = "";
        public string root_path = "";
        public string root_scene_name = "";
        public bool send = true;
        public bool include_all_phys_bones = true;
        /// <summary>
        /// The garment is itself a Humanoid rig whose hierarchy mirrors the avatar's.
        /// Merge Armature then folds those bones into the avatar's own, so there is
        /// nothing garment-specific left to read - the avatar's Humanoid bones ARE the
        /// garment's, and we stream them under the garment's name as well.
        /// </summary>
        public bool include_humanoid = true;
        /// <summary>PhysBone chain roots captured in Edit Mode, by authored name.</summary>
        public List<string> tracked_bone_names = new List<string>();
    }

    [Serializable]
    public sealed class PoseSyncSettings
    {
        public string host = "127.0.0.1";
        public int port = 39541;
        public int send_interval_frames = 1;
        public bool auto_spawn = true;
        public bool send_camera = false;
        public PoseSyncCameraSource camera_source = PoseSyncCameraSource.CameraObject;
        public string camera_id = "";
        public string camera_scene_name = "";
        public string camera_scene_path = "";
        public List<PoseSyncAvatarSetting> avatars = new List<PoseSyncAvatarSetting>();
        public List<PoseSyncClothSetting> clothes = new List<PoseSyncClothSetting>();
    }

    /// <summary>
    /// Persists the manager configuration in EditorPrefs, so nothing has to be
    /// authored into the scene. Avatars are referenced by GlobalObjectId, which
    /// stays valid across the Play Mode domain reload.
    /// </summary>
    public static class PoseSyncSettingsStore
    {
        // EditorPrefs is shared by every project this editor opens. The settings key
        // must be project-scoped, or one project's avatar list shows up (and gets
        // overwritten) in every other project.
        private static string PrefsKey =>
            "D9speed.PoseSyncManager.Settings." + Application.dataPath.GetHashCode().ToString("X8");

        public static PoseSyncSettings Load()
        {
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return new PoseSyncSettings();
            }
            try
            {
                return JsonUtility.FromJson<PoseSyncSettings>(json) ?? new PoseSyncSettings();
            }
            catch
            {
                return new PoseSyncSettings();
            }
        }

        public static void Save(PoseSyncSettings settings)
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        /// <summary>
        /// The name an avatar entry is announced and matched under: the explicit
        /// avatar_name, else the Animator's GameObject name. Owner links from cloth
        /// entries must use THIS, not raw avatar_name, or blank-named avatars can
        /// never be linked to.
        /// </summary>
        public static string DisplayName(PoseSyncAvatarSetting avatar)
        {
            if (avatar == null)
            {
                return "";
            }
            if (!string.IsNullOrWhiteSpace(avatar.avatar_name))
            {
                return avatar.avatar_name;
            }
            var animator = Resolve(avatar);
            return animator != null ? animator.gameObject.name : "";
        }

        public static string IdOf(UnityEngine.Object obj)
        {
            return obj == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        /// <summary>Hierarchy path from the scene root, e.g. "Root/Body/Armature".</summary>
        public static string PathOf(Component component)
        {
            if (component == null)
            {
                return "";
            }
            var t = component.transform;
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        public static void Record(PoseSyncAvatarSetting setting, Animator animator)
        {
            if (animator == null)
            {
                setting.animator_id = "";
                setting.scene_name = "";
                setting.scene_path = "";
                return;
            }
            setting.animator_id = IdOf(animator);
            setting.scene_name = animator.gameObject.scene.name;
            setting.scene_path = PathOf(animator);
        }

        public static Animator Resolve(PoseSyncAvatarSetting setting)
        {
            if (setting == null)
            {
                return null;
            }
            // GlobalObjectId is exact in Edit Mode; it stops resolving once Play Mode
            // clones the scene, so fall back to the recorded hierarchy path there.
            if (!string.IsNullOrEmpty(setting.animator_id)
                && GlobalObjectId.TryParse(setting.animator_id, out var parsed))
            {
                if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) is Animator hit)
                {
                    return hit;
                }
            }
            return FindByPath(setting.scene_name, setting.scene_path);
        }

        public static void RecordCamera(PoseSyncSettings settings, Camera camera)
        {
            settings.camera_id = IdOf(camera);
            settings.camera_scene_name = camera != null ? camera.gameObject.scene.name : "";
            settings.camera_scene_path = camera != null ? PathOf(camera) : "";
        }

        public static Camera ResolveCamera(PoseSyncSettings settings)
        {
            if (settings == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(settings.camera_id)
                && GlobalObjectId.TryParse(settings.camera_id, out var parsed)
                && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) is Camera camera)
            {
                return camera;
            }
            return FindComponentByPath<Camera>(settings.camera_scene_name, settings.camera_scene_path);
        }

        /// <summary>Record an extra root: the path under the avatar, plus the bare name.</summary>
        public static void RecordExtraRoot(PoseSyncExtraRoot extra, Animator animator, Transform target)
        {
            if (target == null)
            {
                extra.bone_name = "";
                extra.relative_path = "";
                return;
            }
            extra.bone_name = PoseSyncManager.StripMangledName(target.name);
            extra.relative_path = RelativePath(animator != null ? animator.transform : null, target);
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == null)
            {
                return "";
            }
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Find the picked hierarchy again after Play Mode started.
        ///
        /// Merge Armature moves the object under the avatar's armature and renames it
        /// to "&lt;original&gt;$&lt;guid&gt;", so the authored path usually stops resolving.
        /// The path is still tried first (it is exact when MA did not touch it), then
        /// we fall back to a name search that compares against the pre-'$' part.
        /// </summary>
        public static Transform ResolveExtraRoot(Animator animator, PoseSyncExtraRoot extra)
        {
            if (animator == null || extra == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(extra.relative_path))
            {
                var byPath = animator.transform.Find(extra.relative_path);
                if (byPath != null)
                {
                    return byPath;
                }
            }

            if (string.IsNullOrEmpty(extra.bone_name))
            {
                return null;
            }
            Transform match = null;
            var ambiguous = 0;
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (PoseSyncManager.StripMangledName(t.name) != extra.bone_name)
                {
                    continue;
                }
                ambiguous++;
                if (match == null)
                {
                    match = t;
                }
            }
            if (ambiguous > 1)
            {
                Debug.LogWarning(
                    $"Pose Sync Manager: '{extra.bone_name}' matches {ambiguous} objects under " +
                    $"'{animator.gameObject.name}'; using the first one found.");
            }
            return match;
        }

        /// <summary>
        /// Record a garment: its root, and the authored names of every PhysBone chain
        /// root beneath it. Those names are what survives Merge Armature (as
        /// "&lt;name&gt;$&lt;guid&gt;"), so they are the handle used to find the bones again
        /// once Play Mode has restructured the hierarchy.
        /// </summary>
        public static void RecordCloth(PoseSyncClothSetting cloth, Transform root)
        {
            cloth.tracked_bone_names.Clear();
            if (root == null)
            {
                cloth.root_name = "";
                cloth.root_path = "";
                cloth.root_scene_name = "";
                return;
            }

            cloth.root_name = PoseSyncManager.StripMangledName(root.name);
            cloth.root_path = PathOf(root);
            cloth.root_scene_name = root.gameObject.scene.name;
            if (string.IsNullOrWhiteSpace(cloth.avatar_name))
            {
                cloth.avatar_name = cloth.root_name;
            }

            var seen = new HashSet<string>();
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "VRCPhysBone")
                {
                    continue;
                }
                var chain_root = behaviour.GetType().GetField("rootTransform")?.GetValue(behaviour) as Transform;
                if (chain_root == null)
                {
                    chain_root = behaviour.transform;
                }
                var name = PoseSyncManager.StripMangledName(chain_root.name);
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                {
                    cloth.tracked_bone_names.Add(name);
                }
            }
        }

        /// <summary>Resolve the authored garment, including inactive scene objects.</summary>
        public static Transform ResolveClothRoot(PoseSyncClothSetting cloth)
        {
            return cloth == null ? null
                : FindComponentByPath<Transform>(cloth.root_scene_name, cloth.root_path);
        }

        /// <summary>
        /// Refresh derived chain names before MA moves the bones. A garment may have
        /// gained or lost PhysBones since its Transform was picked in the window.
        /// Never re-record from the already merged Play Mode hierarchy.
        /// </summary>
        public static void RefreshClothTracking(PoseSyncSettings settings, bool log_warnings = false)
        {
            if (EditorApplication.isPlaying || settings?.clothes == null)
            {
                return;
            }
            foreach (var cloth in settings.clothes)
            {
                if (cloth == null || !cloth.send || string.IsNullOrEmpty(cloth.root_path))
                {
                    continue;
                }
                var root = ResolveClothRoot(cloth);
                if (root == null)
                {
                    if (log_warnings)
                    {
                        Debug.LogWarning($"Pose Sync Manager: clothing '{cloth.avatar_name}' could not be " +
                                         $"refreshed before Play: '{cloth.root_path}' was not found. Re-pick its Transform.");
                    }
                    continue;
                }
                RecordCloth(cloth, root);
            }
        }

        /// <summary>
        /// Find the garment's bones after Merge Armature ran. Every recorded chain root
        /// is searched for by its authored name under the owning avatar, comparing
        /// against the pre-'$' part of each object's name.
        /// </summary>
        public static List<Transform> ResolveClothRoots(Transform search_scope, PoseSyncClothSetting cloth,
                                                        out List<string> missing)
        {
            var found = new List<Transform>();
            missing = new List<string>();
            if (search_scope == null || cloth == null)
            {
                return found;
            }

            var all = search_scope.GetComponentsInChildren<Transform>(true);
            foreach (var wanted in cloth.tracked_bone_names)
            {
                Transform hit = null;
                foreach (var t in all)
                {
                    if (PoseSyncManager.StripMangledName(t.name) == wanted)
                    {
                        hit = t;
                        break;
                    }
                }
                if (hit != null)
                {
                    found.Add(hit);
                }
                else
                {
                    missing.Add(wanted);
                }
            }
            return found;
        }

        public static Animator FindByPath(string scene_name, string path)
        {
            return FindComponentByPath<Animator>(scene_name, path);
        }

        private static T FindComponentByPath<T>(string scene_name, string path) where T : Component
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var parts = path.Split('/');
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(scene_name) && scene.name != scene_name)
                {
                    continue;
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0])
                    {
                        continue;
                    }
                    var t = root.transform;
                    var ok = true;
                    for (var p = 1; p < parts.Length; p++)
                    {
                        // Transform.Find also walks inactive children.
                        t = t.Find(parts[p]);
                        if (t == null)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok && t != null)
                    {
                        var component = t.GetComponent<T>();
                        if (component != null)
                        {
                            return component;
                        }
                    }
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Spawns a throwaway GameObject carrying <see cref="PoseSyncManager"/> when Play
    /// Mode starts, configured from <see cref="PoseSyncSettingsStore"/>, and removes it
    /// on exit. This keeps the scene free of sync components entirely.
    /// </summary>
    [InitializeOnLoad]
    public static class PoseSyncManagerBootstrap
    {
        public const string SpawnedObjectName = "PoseSyncManager (auto)";
        private static readonly List<BlenderPoseSenderWorld> DisabledLegacySenders =
            new List<BlenderPoseSenderWorld>();
        private static PoseSyncManager scene_view_manager;
        private static Camera scene_view_proxy;
        private static readonly EditorApplication.CallbackFunction SceneViewUpdate = UpdateSceneViewCamera;

        static PoseSyncManagerBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                var settings = PoseSyncSettingsStore.Load();
                if (settings.auto_spawn)
                {
                    PoseSyncSettingsStore.RefreshClothTracking(settings, true);
                    PoseSyncSettingsStore.Save(settings);
                }
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Spawn();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopSceneViewSync();
                RestoreLegacySendersBeforeEditMode();
                // With "Enter Play Mode without domain reload" the object would
                // otherwise survive into the next session.
                DestroySpawned();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                ReleaseLegacySenders();
            }
        }

        public static PoseSyncManager FindSpawned()
        {
            return FindSpawnedManagers().OrderByDescending(manager => manager.ActiveAvatarCount).FirstOrDefault();
        }

        private static IEnumerable<PoseSyncManager> FindSpawnedManagers()
        {
            // FindObjectsByType excludes DontSave objects, including our own manager.
            // Include them so the status UI and Play Mode cleanup can find it.
            return Resources.FindObjectsOfTypeAll<PoseSyncManager>().Where(manager =>
                manager != null
                && !EditorUtility.IsPersistent(manager)
                && manager.gameObject.name == SpawnedObjectName
                && (manager.gameObject.hideFlags & HideFlags.DontSave) != 0);
        }

        /// <summary>Create and configure the runtime manager. Camera-only streaming
        /// is valid even when no avatar is configured.</summary>
        public static PoseSyncManager Spawn()
        {
            var settings = PoseSyncSettingsStore.Load();
            if (!settings.auto_spawn
                || ((settings.avatars == null || settings.avatars.Count == 0) && !settings.send_camera))
            {
                return null;
            }

            var entries = new List<PoseSyncManager.AvatarEntry>();
            var unresolved = 0;
            foreach (var avatar in settings.avatars ?? new List<PoseSyncAvatarSetting>())
            {
                if (!avatar.send)
                {
                    continue;
                }
                var animator = PoseSyncSettingsStore.Resolve(avatar);
                if (animator == null)
                {
                    unresolved++;
                    continue;
                }
                var extras = new List<PoseSyncManager.ExtraRoot>();
                var unresolved_roots = new List<string>();
                foreach (var extra in avatar.extra_roots ?? new List<PoseSyncExtraRoot>())
                {
                    if (extra == null || !extra.send)
                    {
                        continue;
                    }
                    var target = PoseSyncSettingsStore.ResolveExtraRoot(animator, extra);
                    if (target == null)
                    {
                        unresolved_roots.Add(extra.bone_name);
                        continue;
                    }
                    extras.Add(new PoseSyncManager.ExtraRoot
                    {
                        transform = target,
                        include_descendants = extra.include_descendants,
                    });
                }
                if (unresolved_roots.Count > 0)
                {
                    Debug.LogWarning(
                        $"Pose Sync Manager: could not find {string.Join(", ", unresolved_roots)} " +
                        $"under '{animator.gameObject.name}'.");
                }

                entries.Add(new PoseSyncManager.AvatarEntry
                {
                    animator = animator,
                    send = true,
                    avatar_name = avatar.avatar_name,
                    include_all_phys_bones = avatar.include_all_phys_bones,
                    extra_roots = extras.ToArray(),
                });
            }

            if (unresolved > 0)
            {
                Debug.LogWarning(
                    $"Pose Sync Manager: {unresolved} avatar(s) could not be resolved in this scene. " +
                    "Re-pick them in D9speed/Animation/Pose Sync Manager.");
            }

            // Garments merged in by Modular Avatar: their bones now live under the
            // avatar, so search there and stream them as their own entry.
            foreach (var cloth in settings.clothes ?? new List<PoseSyncClothSetting>())
            {
                if (cloth == null || !cloth.send)
                {
                    continue;
                }
                // No Transform picked (or it was cleared): nothing to stream. Without
                // this the entry would still be announced and the Blender panel would
                // offer a slot for a garment that is not in the scene.
                if (string.IsNullOrEmpty(cloth.root_name) || string.IsNullOrWhiteSpace(cloth.avatar_name))
                {
                    continue;
                }

                // Match by display name, not raw avatar_name: for avatars whose name
                // field is blank, the announced name is the Animator's object name.
                var owner = settings.avatars.Find(a =>
                    PoseSyncSettingsStore.DisplayName(a) == cloth.owner_avatar_name);
                var owner_animator = owner != null ? PoseSyncSettingsStore.Resolve(owner) : null;
                var scope = owner_animator != null ? owner_animator.transform : null;
                if (scope == null)
                {
                    Debug.LogWarning(
                        $"Pose Sync Manager: clothing '{cloth.avatar_name}' has no resolvable owner avatar " +
                        $"('{cloth.owner_avatar_name}'); skipped.");
                    continue;
                }

                var roots = PoseSyncSettingsStore.ResolveClothRoots(scope, cloth, out var missing);
                if (missing.Count > 0)
                {
                    Debug.LogWarning(
                        $"Pose Sync Manager: clothing '{cloth.avatar_name}' — could not find " +
                        $"{string.Join(", ", missing)} under '{scope.name}'.");
                }
                // Chains were recorded but none of them are in the scene: the garment
                // is not equipped on this avatar. Announcing it anyway would create a
                // slot in Blender for something that is never posed.
                if (cloth.tracked_bone_names.Count > 0 && roots.Count == 0)
                {
                    Debug.LogWarning(
                        $"Pose Sync Manager: clothing '{cloth.avatar_name}' is not present under " +
                        $"'{scope.name}'; not announcing it.");
                    continue;
                }

                var cloth_roots = new List<PoseSyncManager.ExtraRoot>();
                foreach (var t in roots)
                {
                    cloth_roots.Add(new PoseSyncManager.ExtraRoot { transform = t, include_descendants = true });
                }

                entries.Add(new PoseSyncManager.AvatarEntry
                {
                    // When the garment is a Humanoid rig mirroring the avatar, its bones
                    // no longer exist separately after the merge - read them from the
                    // avatar's Animator and send them under the garment's name.
                    animator = cloth.include_humanoid ? owner_animator : null,
                    // Rotations share the avatar's space - that is where MA put these bones.
                    root_override = scope,
                    send = true,
                    avatar_name = cloth.avatar_name,
                    include_humanoid = cloth.include_humanoid,
                    include_all_phys_bones = false,
                    extra_roots = cloth_roots.ToArray(),
                });
            }
            if (entries.Count == 0 && !settings.send_camera)
            {
                return null;
            }

            var duplicate_names = entries
                .Select(entry => entry.ResolveName())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (duplicate_names.Length > 0)
            {
                Debug.LogError(
                    "Pose Sync Manager: Blender側の名前が重複しています: " +
                    string.Join(", ", duplicate_names) +
                    ". D9speed/Animation/Pose Sync Manager で一意の名前に変更してください。送信は開始しません。");
                return null;
            }

            DestroySpawned();

            DisableLegacySenders();

            var go = new GameObject(SpawnedObjectName) { hideFlags = HideFlags.DontSave };
            var manager = go.AddComponent<PoseSyncManager>();
            // Configure before Start runs, so Bind() sees the final configuration.
            manager.Configure(settings.host, settings.port, settings.send_interval_frames, entries, true);
            ConfigureCamera(settings, manager, go);
            Debug.Log($"Pose Sync Manager: streaming {entries.Count} avatar(s) to " +
                      $"{settings.host}:{settings.port}.");
            return manager;
        }

        private static void ConfigureCamera(PoseSyncSettings settings, PoseSyncManager manager, GameObject owner)
        {
            StopSceneViewSync();
            if (!settings.send_camera)
            {
                return;
            }

            if (settings.camera_source == PoseSyncCameraSource.SceneView)
            {
                scene_view_manager = manager;
                scene_view_proxy = owner.AddComponent<Camera>();
                scene_view_proxy.enabled = false;
                EditorApplication.update += SceneViewUpdate;
                UpdateSceneViewCamera();
                return;
            }

            var camera = PoseSyncSettingsStore.ResolveCamera(settings);
            if (camera != null)
            {
                manager.ConfigureCamera(camera);
            }
            else
            {
                Debug.LogWarning("Pose Sync Manager: 送信Cameraを解決できません。Manager画面で選び直してください。");
            }
        }

        private static void UpdateSceneViewCamera()
        {
            if (scene_view_manager == null || scene_view_proxy == null)
            {
                StopSceneViewSync();
                return;
            }
            var source = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera
                : null;
            if (source == null)
            {
                return;
            }

            scene_view_proxy.CopyFrom(source);
            scene_view_proxy.enabled = false;
            scene_view_proxy.transform.SetPositionAndRotation(
                source.transform.position, source.transform.rotation);
            var scene_view = SceneView.lastActiveSceneView;
            scene_view_manager.ConfigureSceneView(
                scene_view_proxy,
                scene_view.pivot,
                Vector3.Distance(source.transform.position, scene_view.pivot),
                scene_view.size);
        }

        private static void StopSceneViewSync()
        {
            EditorApplication.update -= SceneViewUpdate;
            scene_view_manager = null;
            scene_view_proxy = null;
        }

        /// <summary>
        /// Switch off any per-avatar BlenderPoseSenderWorld for this Play session.
        /// The receiver serves one client at a time, so a legacy sender would race the
        /// manager for the connection and win or lose at random. Play Mode only - the
        /// scene asset is untouched.
        /// </summary>
        private static void DisableLegacySenders()
        {
            DisabledLegacySenders.Clear();
#if UNITY_2022_2_OR_NEWER
            var senders = UnityEngine.Object.FindObjectsByType<BlenderPoseSenderWorld>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
#else
            var senders = UnityEngine.Object.FindObjectsOfType<BlenderPoseSenderWorld>(true);
#endif
            var disabled = 0;
            foreach (var sender in senders)
            {
                if (sender.enabled)
                {
                    sender.ManagedExternally = true;
                    sender.enabled = false;
                    DisabledLegacySenders.Add(sender);
                    disabled++;
                }
            }
            if (disabled > 0)
            {
                Debug.Log($"Pose Sync Manager: disabled {disabled} legacy BlenderPoseSenderWorld " +
                          "component(s) for this Play session (the manager owns the connection).");
            }
        }

        private static void RestoreLegacySendersBeforeEditMode()
        {
            foreach (var sender in DisabledLegacySenders)
            {
                if (sender == null)
                {
                    continue;
                }
                // Re-enable while still marked external so OnEnable cannot open a
                // second connection during ExitingPlayMode.
                sender.ManagedExternally = true;
                sender.enabled = true;
            }
        }

        private static void ReleaseLegacySenders()
        {
            foreach (var sender in DisabledLegacySenders)
            {
                if (sender != null)
                {
                    sender.ManagedExternally = false;
                }
            }
            DisabledLegacySenders.Clear();
        }

        public static void DestroySpawned()
        {
            StopSceneViewSync();
            foreach (var existing in FindSpawnedManagers().ToArray())
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }
    }
}
