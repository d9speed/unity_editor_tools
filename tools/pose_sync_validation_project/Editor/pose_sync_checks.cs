using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityBlenderPoseSync.World;
using UnityBlenderPoseSync.World.Editor;
using UnityBlenderPoseSync.Setup;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class PoseSyncChecks
    {
        [Serializable] public sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] public sealed class Report { public bool passed; public string unity_version; public List<Check> checks = new List<Check>(); }
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public static string Output => Path.GetFullPath("Logs/pose_sync");
        private static void Require(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new Report { unity_version = Application.unityVersion };
            Action<string, Action> check = (name, action) => {
                try { action(); report.checks.Add(new Check { name = name, passed = true, detail = "passed" }); }
                catch (Exception e) { report.checks.Add(new Check { name = name, detail = e.ToString() }); Debug.LogError(e); }
            };
            check("Runtime is in Player assemblies; Editor and optional integration are excluded", () => {
                var player = CompilationPipeline.GetAssemblies(AssembliesType.Player);
                Require(player.Any(a => a.name == "D9speed.PoseSync.Runtime"), "Runtime missing");
                Require(!player.Any(a => a.name == "D9speed.PoseSync.Editor" || a.name == "D9speed.PoseSync.NDMF.Editor"), "Editor leaked");
                var runtime = typeof(PoseSyncManager).Assembly.GetReferencedAssemblies();
                Require(!runtime.Any(a => a.Name.StartsWith("MessagePack")), "External MessagePack dependency");
            });
            check("All three menus open and bundled Blender receiver resolves", () => {
                foreach (var menu in new [] { "Pose Sync Setup", "Pose Sync Manager", "Send Pose Snapshot to Blender" })
                    Require(EditorApplication.ExecuteMenuItem("D9speed/Animation/" + menu), menu);
                Require(File.Exists(PoseSyncSetupWindow.ReceiverPath), "Receiver missing");
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Namespace?.StartsWith("UnityBlenderPoseSync") == true)) window.Close();
            });
            check("Fresh project has no inherited avatars or auto-spawned sender", () => {
                var settings = PoseSyncSettingsStore.Load();
                Require(settings.avatars.Count == 0 && settings.clothes.Count == 0, "Inherited settings");
                Require(PoseSyncManagerBootstrap.Spawn() == null, "Unconfigured sender spawned");
            });
            check("Synthetic extra bones and camera produce v3 messages without changing source transforms", () => {
                var root = new GameObject("Synthetic Avatar");
                var camera = new GameObject("Synthetic Camera").AddComponent<Camera>();
                try {
                    var bone = new GameObject("検証ボーン").transform;
                    bone.SetParent(root.transform, false);
                    var manager = root.AddComponent<PoseSyncManager>();
                    manager.Configure("127.0.0.1", 39541, 1, new [] { new PoseSyncManager.AvatarEntry {
                        root_override = root.transform, avatar_name = "検証アバター", include_humanoid = false, include_all_phys_bones = false,
                        extra_roots = new [] { new PoseSyncManager.ExtraRoot { transform = bone } }
                    } }, false);
                    camera.transform.position = new Vector3(1, 2, 3);
                    camera.fieldOfView = 50;
                    manager.ConfigureCamera(camera);
                    manager.Bind();
                    Require(manager.TotalBoneCount == 1, "Wrong bone count");
                    var definitions = (AvatarDefsMessage)typeof(PoseSyncManager).GetMethod("BuildDefinitions", Private).Invoke(manager, null);
                    bone.localRotation = Quaternion.Euler(0, 30, 0);
                    typeof(PoseSyncManager).GetMethod("CaptureFrame", Private).Invoke(manager, null);
                    var frame = (PoseFrameV3)typeof(PoseSyncManager).GetField("frame_message", Private).GetValue(manager);
                    Require(definitions.avatars[0].targets[0] == "検証ボーン", "Bone name");
                    Require(Quaternion.Angle(bone.localRotation, Quaternion.Euler(0,30,0)) < .001f, "Source changed");
                    File.WriteAllBytes(Path.Combine(Output, "definitions.msgpack"), PoseSyncMessagePack.Serialize(definitions));
                    File.WriteAllBytes(Path.Combine(Output, "frame.msgpack"), PoseSyncMessagePack.Serialize(frame));
                    File.WriteAllBytes(Path.Combine(Output, "state.msgpack"), PoseSyncMessagePack.Serialize(new PoseStateV3 { schema = frame.schema, state = "stopped", frame = long.MaxValue }));
                    File.WriteAllBytes(Path.Combine(Output, "legacy.msgpack"), PoseSyncMessagePack.Serialize(new WorldPoseFrameMessage { bones = new [] { new WorldBoneMessage { target = "検証ボーン" } }, unity_time = 1234.5678 }));
                    File.WriteAllBytes(Path.Combine(Output, "list.msgpack"), PoseSyncMessagePack.Serialize(new AvatarListMessage { avatars = new [] { "日本語", new string('x',32), new string('y',256) } }));
                    File.WriteAllBytes(Path.Combine(Output, "wide_arrays.msgpack"), PoseSyncMessagePack.Serialize(new AvatarDefsMessage { avatars = Enumerable.Range(0,17).Select(i => new AvatarDefEntry { avatar_name = i.ToString(), targets = new string[17], rest_rotations = new byte[70000] }).ToArray() }));
                } finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
            });
            check("Blender-generated commands decode and malformed commands are rejected", () => {
                foreach (var command in new [] { "pause", "resume", "step", "stop" }) {
                    var bytes = File.ReadAllBytes(Path.Combine(Output, command + ".command"));
                    var decoded = PoseSyncMessagePack.DeserializeCommand(bytes, 0, bytes.Length);
                    Require(decoded.command == command && decoded.frames == 2, command);
                }
                var valid = PoseSyncMessagePack.Serialize(new PoseSyncCommand { command = "step", frames = 120 });
                Require(PoseSyncMessagePack.DeserializeCommand(valid, 0, valid.Length).frames == 120, "Upper bound");
                var invalid = new List<byte[]> {
                    valid.Take(valid.Length-1).ToArray(), valid.Concat(new byte[] { 0 }).ToArray(),
                    new byte[] { 0x81, 0xa1, 0x78, 0xdd, 0xff, 0xff, 0xff, 0xff }, new byte[4097],
                    PoseSyncMessagePack.Serialize(new PoseSyncCommand { command = "step", frames = int.MaxValue }),
                    PoseSyncMessagePack.Serialize(new PoseSyncCommand { command = "arbitrary" })
                };
                foreach (var bytes in invalid) {
                    bool rejected = false;
                    try { PoseSyncMessagePack.DeserializeCommand(bytes, 0, bytes.Length); } catch { rejected = true; }
                    Require(rejected, "Malformed command accepted");
                }
            });
            report.passed = report.checks.All(c => c.passed);
            File.WriteAllText(Path.Combine(Output, "edit_mode_results.json"), JsonUtility.ToJson(report,true));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        public static void PreparePlay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Synthetic Avatar");
            var animator = root.AddComponent<Animator>();
            var bone = new GameObject("検証ボーン").transform;
            bone.SetParent(root.transform, false);
            var camera = new GameObject("Synthetic Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(1,2,3);
            camera.fieldOfView = 50;
            var probe = new GameObject("Runtime Probe").AddComponent<pose_sync_runtime_probe>();
            probe.bone = bone; probe.root = root.transform; probe.camera_source = camera;
            EditorSceneManager.SaveScene(root.scene, "Assets/pose_sync_fixture.unity");
            var settings = new PoseSyncSettings { port = 39549, send_camera = true,
                camera_id = PoseSyncSettingsStore.IdOf(camera), camera_scene_path = PoseSyncSettingsStore.PathOf(camera), camera_scene_name = root.scene.name };
            settings.avatars.Add(new PoseSyncAvatarSetting { animator_id = PoseSyncSettingsStore.IdOf(animator),
                scene_name = root.scene.name, scene_path = PoseSyncSettingsStore.PathOf(animator), avatar_name = "検証アバター",
                include_all_phys_bones = false, extra_roots = new List<PoseSyncExtraRoot> { new PoseSyncExtraRoot { bone_name = bone.name, relative_path = bone.name } } });
            PoseSyncSettingsStore.Save(settings);
            SessionState.SetBool("D9speed.PoseSync.Validation.Play", true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void WatchPlay()
        {
            EditorApplication.playModeStateChanged += state => {
                if (!SessionState.GetBool("D9speed.PoseSync.Validation.Play", false) || state != PlayModeStateChange.EnteredEditMode) return;
                SessionState.SetBool("D9speed.PoseSync.Validation.Play", false);
                EditorApplication.delayCall += () => {
                    var path = Path.Combine(Output, "play_mode_results.json");
                    var ok = File.Exists(path) && File.ReadAllText(path).Contains("\"passed\": true") && PoseSyncManagerBootstrap.FindSpawned() == null;
                    PoseSyncSettingsStore.Save(new PoseSyncSettings());
                    File.WriteAllText(Path.Combine(Output,"play_cleanup.json"), "{\"passed\":" + ok.ToString().ToLowerInvariant() + "}");
                    EditorApplication.Exit(ok ? 0 : 1);
                };
            };
        }

        public static void BuildPlayer()
        {
            EditorSceneManager.OpenScene("Assets/pose_sync_fixture.unity");
            var probe = Object.FindObjectOfType<pose_sync_runtime_probe>();
            probe.own_manager = true;
            EditorSceneManager.SaveScene(probe.gameObject.scene);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new [] {"Assets/pose_sync_fixture.unity"}, target = BuildTarget.StandaloneWindows64,
                locationPathName = "Build/pose_sync_probe.exe", options = BuildOptions.Development
            });
            Require(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, report.summary.result.ToString());
            EditorApplication.Exit(0);
        }
    }
}
