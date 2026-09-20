using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;
using UnityBlenderPoseSync.Setup;

namespace D9speed.PackageValidation
{
    // Dedicated projects only. Baseline installs 3.1.7 and reproduces the 0.1.1 error;
    // Upgrade runs after replacing the Pose Sync package with the fixed version.
    [InitializeOnLoad]
    public static class pose_sync_upgrade_checks
    {
        const string Key = "D9speed.PoseSync.UpgradeValidation.";
        const string NugetUrl = "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0";
        const string Support317 = "https://github.com/MessagePack-CSharp/MessagePack-CSharp.git?path=src/MessagePack.UnityClient/Assets/Scripts/MessagePack#v3.1.7";
        static readonly string[] Ids = { "MessagePack.Annotations", "MessagePackAnalyzer", "MessagePack" };
        static AddRequest request;
        static double next;
        static Type Bridge => Type.GetType("UnityBlenderPoseSync.Setup.PoseSyncNuGetBridge, D9speed.PoseSync.NuGet.Editor");
        static pose_sync_upgrade_checks() { EditorApplication.update += Poll; }
        static string VersionOf(string id) => (string)Bridge.GetMethod("VersionOf").Invoke(null, new object[] { id });
        static string UnityVersion => UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(p => p.name == PoseSyncDependencies.UnitySupportId)?.version ?? "";

        public static void Baseline() { Start("baseline"); Stage("nuget"); }
        public static void Upgrade()
        {
            try
            {
                Require(Ids.All(id => VersionOf(id) == "3.1.7") && UnityVersion == "3.1.7", "Expected complete 3.1.7 fixture");
                CheckVersionPolicy();
                Start("upgrade"); Stage("upgrade"); PoseSyncDependencies.Begin();
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static void Start(string mode)
        {
            SessionState.SetString(Key + "Mode", mode);
            SessionState.SetBool(Key + "Active", true);
            SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
        }
        static void Stage(string stage)
        {
            SessionState.SetString(Key + "Stage", stage);
            SessionState.SetBool(Key + "Requested", false);
        }
        static void Add(string url)
        {
            if (SessionState.GetBool(Key + "Requested", false)) return;
            SessionState.SetBool(Key + "Requested", true); request = Client.Add(url);
        }
        static void Poll()
        {
            if (!SessionState.GetBool(Key + "Active", false) || EditorApplication.timeSinceStartup < next
                || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            next = EditorApplication.timeSinceStartup + .5;
            try
            {
                Require(DateTime.UtcNow.Ticks < long.Parse(SessionState.GetString(Key + "Deadline", "0")), "Validation timeout");
                if (request != null)
                {
                    if (!request.IsCompleted) return;
                    var result = request; request = null;
                    Require(result.Status == StatusCode.Success, result.Error?.message ?? "UPM request failed");
                }
                switch (SessionState.GetString(Key + "Stage", ""))
                {
                    case "nuget":
                        if (PoseSyncDependencies.HasNuGet) Stage("core"); else Add(NugetUrl);
                        break;
                    case "core":
                        InstallOldCore(); Stage("unity"); break;
                    case "unity":
                        if (UnityVersion == "3.1.7") { Stage("baseline"); PoseSyncDependencies.Begin(); }
                        else Add(Support317);
                        break;
                    case "baseline":
                        if (PoseSyncDependencies.CurrentStep != PoseSyncDependencies.Step.Failed) return;
                        Require(PoseSyncDependencies.LastError.Contains("3.1.7"), "Unexpected baseline error");
                        Require(Ids.All(id => VersionOf(id) == "3.1.7") && UnityVersion == "3.1.7", "Baseline changed dependencies");
                        Finish(true, "Reproduced: " + PoseSyncDependencies.LastError); break;
                    case "upgrade":
                    case "repeat":
                        if (PoseSyncDependencies.CurrentStep == PoseSyncDependencies.Step.Failed)
                            throw new Exception(PoseSyncDependencies.LastError);
                        if (!PoseSyncDependencies.Ready || PoseSyncDependencies.CurrentStep != PoseSyncDependencies.Step.Done) return;
                        Require(Ids.All(id => VersionOf(id) == "3.1.9") && UnityVersion == "3.1.9", "Versions not aligned");
                        CheckCompletedUiAndMenus();
                        if (SessionState.GetString(Key + "Stage", "") == "upgrade")
                        { Stage("repeat"); PoseSyncDependencies.Begin(); }
                        else Finish(true, "3.1.7 -> 3.1.9; manually installed dependencies, Unity Support, Runtime load, green checks, five unified menus, version guards and repeat setup verified");
                        break;
                }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        static void InstallOldCore()
        {
            // Use the official API via reflection so this fixture compiles before NuGet exists.
            var identifier = Type.GetType("NugetForUnity.Models.NugetPackageIdentifier, NuGetForUnity", true);
            var installer = Type.GetType("NugetForUnity.NugetPackageInstaller, NuGetForUnity", true).GetMethod("InstallIdentifier");
            EditorApplication.LockReloadAssemblies();
            try
            {
                foreach (var id in Ids)
                {
                    var package = Activator.CreateInstance(identifier, id, "3.1.7");
                    identifier.GetProperty("IsManuallyInstalled").SetValue(package, true);
                    Require((bool)installer.Invoke(null, new[] { package, (object)false, false, true }), "Fixture installation failed: " + id);
                }
            }
            finally { EditorApplication.UnlockReloadAssemblies(); AssetDatabase.Refresh(); }
        }

        static void CheckVersionPolicy()
        {
            var method = typeof(PoseSyncDependencies).GetMethod("ValidateMessagePackUpgrade");
            Require(method != null, "Fixed setup package is missing");
            foreach (var version in new[] { "", "3.1.0", "3.1.7", "3.1.8", "3.1.9" })
                method.Invoke(null, new object[] { "MessagePack", version });
            foreach (var version in new[] { "2.5.0", "3.0.0", "3.1.10", "3.2.0", "3.1.8-preview", "unknown" })
            {
                bool rejected = false;
                try { method.Invoke(null, new object[] { "MessagePack", version }); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
                Require(rejected, "Unexpected automatic version change: " + version);
            }
        }

        static void CheckCompletedUiAndMenus()
        {
            var window = PoseSyncSetupWindow.Open(); window.CreateGUI();
            var green = new Color(.16f, .85f, .36f);
            var checks = window.rootVisualElement.Q<VisualElement>("setup_checks").Query<Label>().ToList().Where(l => l.text == "✔").ToArray();
            Require(checks.Length == 3 && checks.All(c => c.style.color.value == green), "Green checks");
            var steps = window.rootVisualElement.Q<VisualElement>("setup_steps");
            Require(steps.childCount == 4 && steps.Children().All(s => s.style.backgroundColor.value == green), "Completed step boxes");
            Require(window.rootVisualElement.Q<Button>("setup_action").text == "閉じる", "Completion action");
            foreach (var name in new[] { "PoseSync Setup", "Pose Sync Manager", "Send Pose Snapshot to Blender", "Animator Playback Preview", "Random Hand Muscle Generator" })
                Require(EditorApplication.ExecuteMenuItem("D9speed/Animations/" + name), "Missing menu: " + name);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("D9speed.")))
                foreach (var type in assembly.GetTypes())
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        Require(!method.GetCustomAttributes<MenuItem>().Any(m => m.menuItem.StartsWith("D9speed/Animation/")), "Old Animation menu remains");
            foreach (var opened in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Assembly.GetName().Name.StartsWith("D9speed."))) opened.Close();
        }

        static void Require(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        static void Finish(bool passed, string detail)
        {
            SessionState.SetBool(Key + "Active", false);
            Directory.CreateDirectory("Logs");
            var mode = SessionState.GetString(Key + "Mode", "upgrade");
            File.WriteAllText("Logs/pose_sync_" + mode + "_results.json", JsonUtility.ToJson(new result { passed = passed, detail = detail }, true));
            if (passed) Debug.Log("[PoseSync upgrade] " + detail); else Debug.LogError(detail);
            EditorApplication.Exit(passed ? 0 : 1);
        }
        [Serializable] sealed class result { public bool passed; public string detail; }
    }
}
