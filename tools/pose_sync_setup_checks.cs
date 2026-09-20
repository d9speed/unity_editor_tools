using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityBlenderPoseSync.Setup;

namespace D9speed.PackageValidation
{
    [InitializeOnLoad]
    public static class PoseSyncSetupChecks
    {
        private const string Active = "D9speed.PoseSync.SetupValidation.Active";
        static PoseSyncSetupChecks() { EditorApplication.update += Poll; }
        public static void Run()
        {
            if (PoseSyncDependencies.HasCore) throw new Exception("Expected fresh project without MessagePack");
            if (!EditorApplication.ExecuteMenuItem("D9speed/Animations/PoseSync Setup")) throw new Exception("Setup menu missing");
            var window = PoseSyncSetupWindow.Open();
            window.CreateGUI();
            if (window.rootVisualElement.Q<VisualElement>("setup_checks").Query<Label>().ToList().Count(l => l.text == "✔") != (PoseSyncDependencies.HasNuGet ? 1 : 0))
                throw new Exception("Uninstalled dependency shown as completed");
            SessionState.SetBool(Active,true);
            SessionState.SetString(Active+"Deadline",DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
            PoseSyncDependencies.Begin();
        }
        private static void Poll()
        {
            if (!SessionState.GetBool(Active,false) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (PoseSyncDependencies.CurrentStep == PoseSyncDependencies.Step.Failed)
            { Finish(false,PoseSyncDependencies.LastError); return; }
            if (long.TryParse(SessionState.GetString(Active+"Deadline",""),out var deadline) && DateTime.UtcNow.Ticks > deadline)
            { Finish(false,"Setup validation timeout"); return; }
            if (!PoseSyncDependencies.Ready) return;
            try {
                var window = PoseSyncSetupWindow.Open();
                window.CreateGUI();
                var checks = window.rootVisualElement.Q<VisualElement>("setup_checks").Query<Label>().ToList().Where(l => l.text == "✔").ToArray();
                if (checks.Length != 3 || checks.Any(l => l.style.color.value != new Color(.16f,.85f,.36f))) throw new Exception("Green check marks not inherited");
                var steps = window.rootVisualElement.Q<VisualElement>("setup_steps");
                if (steps.childCount != 4 || steps.Children().Any(v => v.style.backgroundColor.value != new Color(.16f,.85f,.36f)))
                    throw new Exception("Green progress boxes not inherited");
                if (window.rootVisualElement.Q<Button>("setup_action").text != "閉じる") throw new Exception("Completion UI missing");
                window.Close();
                Finish(true,"Fresh install, exact menu, three green checks and four completed boxes verified");
            } catch (Exception error) { Finish(false,error.ToString()); }
        }
        [Serializable] private sealed class Result { public bool passed; public string detail; }
        private static void Finish(bool passed,string detail)
        {
            SessionState.SetBool(Active,false);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/pose_sync_setup_results.json",JsonUtility.ToJson(new Result { passed=passed,detail=detail },true));
            if (!passed) Debug.LogError(detail);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
