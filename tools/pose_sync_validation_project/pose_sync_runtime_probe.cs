using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityBlenderPoseSync.World;

namespace D9speed.PackageValidation
{
    public sealed class pose_sync_runtime_probe : MonoBehaviour
    {
        public Transform root;
        public Transform bone;
        public Camera camera_source;
        public bool own_manager;
        private float started;
        private bool done;
        private void Awake()
        {
            started = Time.realtimeSinceStartup;
            if (!own_manager) return;
            var manager = new GameObject("Standalone Manager").AddComponent<PoseSyncManager>();
            manager.Configure("127.0.0.1", 39549, 1, new [] { new PoseSyncManager.AvatarEntry {
                root_override = root, avatar_name = "検証アバター", include_humanoid = false, include_all_phys_bones = false,
                extra_roots = new [] { new PoseSyncManager.ExtraRoot { transform = bone } }
            } }, false);
            manager.ConfigureCamera(camera_source);
        }
        private void Update()
        {
            if (done) return;
            var elapsed = Time.realtimeSinceStartup - started;
            if (elapsed > 1) bone.localRotation = Quaternion.Euler(0,30,0);
            if (elapsed < 5) return;
            done = true;
            var managers = Resources.FindObjectsOfTypeAll<PoseSyncManager>();
            bool ok = managers.Length == 1 && managers[0].TotalBoneCount == 1 && managers[0].IsStreaming;
            var args = Environment.GetCommandLineArgs();
            var output_index = Array.IndexOf(args, "-poseSyncOutput");
            var output = output_index >= 0 ? args[output_index+1] : Path.GetFullPath("Logs/pose_sync/play_mode_results.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, "{\"passed\": " + ok.ToString().ToLowerInvariant() + ", \"manager_count\": " + managers.Length + "}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit(ok ? 0 : 1);
#endif
        }
    }
}
