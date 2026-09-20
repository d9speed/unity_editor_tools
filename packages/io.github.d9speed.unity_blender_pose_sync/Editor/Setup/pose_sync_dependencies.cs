using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityBlenderPoseSync.Setup
{
    [InitializeOnLoad]
    public static class PoseSyncDependencies
    {
        public const string Symbol = "POSESYNC_HAS_MESSAGEPACK";
        public const string MessagePackVersion = "3.1.9";
        public const string NuGetId = "com.github-glitchenzo.nugetforunity";
        public const string UnitySupportId = "com.github.messagepack-csharp";
        private const string NuGetUrl = "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0";
        private const string UnitySupportUrl = "https://github.com/MessagePack-CSharp/MessagePack-CSharp.git?path=src/MessagePack.UnityClient/Assets/Scripts/MessagePack#v3.1.9";
        private const string Key = "D9speed.PoseSync.Setup.Vpm.";
        public enum Step { Idle, InstallingNuGet, InstallingMessagePack, InstallingMessagePackUnity, Compiling, Done, Failed }
        public static Step CurrentStep { get => (Step)SessionState.GetInt(Key+"Step",0); private set => SessionState.SetInt(Key+"Step",(int)value); }
        public static string LastError { get => SessionState.GetString(Key+"Error", ""); private set => SessionState.SetString(Key+"Error",value); }
        public static bool IsBusy => CurrentStep >= Step.InstallingNuGet && CurrentStep <= Step.Compiling;
        private static AddRequest request;
        private static double next_check;
        private static Type Bridge => Type.GetType("UnityBlenderPoseSync.Setup.PoseSyncNuGetBridge, D9speed.PoseSync.NuGet.Editor");
        public static event Action Changed;

        static PoseSyncDependencies()
        {
            EditorApplication.update += Tick;
            EditorApplication.delayCall += () => {
                if (!Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode
                    && !Ready && !SessionState.GetBool(Key+"Shown",false))
                { SessionState.SetBool(Key+"Shown",true); PoseSyncSetupWindow.Open(); }
            };
        }

        public static bool HasNuGet => Bridge != null;
        public static bool HasCore => InvokeBool("CoreInstalled");
        public static bool HasUnitySupport => PackageVersion(UnitySupportId) == MessagePackVersion;
        public static bool RuntimeLoaded => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "D9speed.PoseSync.Runtime");
        public static bool DependenciesReady => HasNuGet && HasCore && HasUnitySupport && InvokeBool("AnalyzerReady");
        public static bool Ready => DependenciesReady && RuntimeLoaded;

        public static string ReceiverPath
        {
            get {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PoseSyncDependencies).Assembly);
                return package == null ? "" : Path.Combine(package.resolvedPath,"Blender~","blender_pose_receiver_world.py");
            }
        }

        private static string PackageVersion(string id) =>
            UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == id)?.version ?? "";
        private static bool InvokeBool(string method)
        {
            try { return Bridge?.GetMethod(method)?.Invoke(null, null) is bool value && value; }
            catch { return false; }
        }

        public static void Begin()
        {
            if (IsBusy) return;
            LastError = "";
            SetStep(Step.InstallingNuGet);
            next_check = 0;
        }

        private static void SetStep(Step step)
        {
            CurrentStep = step;
            Debug.Log("[PoseSync Setup] " + step);
            SessionState.SetString(Key+"Started", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key+"Requested",false);
            Changed?.Invoke();
        }
        private static void Fail(string error) { LastError = error; SetStep(Step.Failed); }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next_check || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            next_check = EditorApplication.timeSinceStartup + .5;
            try
            {
                if (request != null)
                {
                    if (!request.IsCompleted) { CheckTimeout(); return; }
                    var completed = request; request = null;
                    if (completed.Status != StatusCode.Success) { Fail(completed.Error?.message ?? "Package Managerの導入に失敗しました。"); return; }
                }
                // This also disables main assemblies after dependencies are removed,
                // and enables the selected build target after a target switch.
                var ready = DependenciesReady;
                if (SyncSymbol(ready)) return;
                if (!IsBusy) { Changed?.Invoke(); return; }
                if (CheckTimeout()) return;
                switch (CurrentStep)
                {
                    case Step.InstallingNuGet:
                        if (HasNuGet) { SetStep(Step.InstallingMessagePack); return; }
                        if (PackageVersion(NuGetId).Length > 0 && !SessionState.GetBool(Key+"Requested",false))
                        { Fail("対応外のNuGetForUnityが導入済みです。4.5.xへ揃えて再試行してください。既存版は変更していません。"); return; }
                        RequestUpm(NuGetUrl);
                        break;
                    case Step.InstallingMessagePack:
                        if (HasCore && InvokeBool("AnalyzerReady")) { SetStep(Step.InstallingMessagePackUnity); return; }
                        if (SessionState.GetBool(Key+"Requested",false)) return;
                        SessionState.SetBool(Key+"Requested",true);
                        EditorApplication.LockReloadAssemblies();
                        try {
                            var result = Bridge.GetMethod("Install").Invoke(null,null);
                            if (!(result is bool success) || !success) { Fail("MessagePackの導入に失敗しました。Consoleを確認して再試行してください。"); return; }
                        } finally { EditorApplication.UnlockReloadAssemblies(); AssetDatabase.Refresh(); }
                        break;
                    case Step.InstallingMessagePackUnity:
                        if (HasUnitySupport) { SetStep(Step.Compiling); return; }
                        var existing = PackageVersion(UnitySupportId);
                        if (existing.Length > 0) { Fail("MessagePack.Unity " + existing + " が導入済みです。3.1.9へ揃えて再試行してください。既存版は変更していません。"); return; }
                        RequestUpm(UnitySupportUrl);
                        break;
                    case Step.Compiling:
                        if (Ready) SetStep(Step.Done);
                        break;
                }
            }
            catch (Exception error) { Fail(error.InnerException?.Message ?? error.Message); }
        }

        private static void RequestUpm(string url)
        {
            if (SessionState.GetBool(Key+"Requested",false)) return;
            SessionState.SetBool(Key+"Requested",true);
            request = Client.Add(url);
        }
        private static bool CheckTimeout()
        {
            if (!IsBusy || !long.TryParse(SessionState.GetString(Key+"Started",""),out var ticks)) return false;
            if (DateTime.UtcNow.Ticks - ticks <= TimeSpan.FromMinutes(10).Ticks) return false;
            Fail("処理が完了しませんでした。接続状況とConsoleを確認し、再試行してください。");
            return true;
        }
        private static bool SyncSymbol(bool wanted)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var values = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s => s.Length > 0).Distinct().ToList();
            if (values.Contains(Symbol) == wanted) return false;
            values.RemoveAll(s => s == Symbol);
            if (wanted) values.Add(Symbol);
            PlayerSettings.SetScriptingDefineSymbols(target,string.Join(";",values));
            return true;
        }
    }
}
