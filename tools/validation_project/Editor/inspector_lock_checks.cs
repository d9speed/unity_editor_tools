using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    // Run only in a dedicated validation project: these checks close its Inspectors.
    public static class InspectorLockChecks
    {
        private const string shortcut_id = "Custom/ShortCutEX/InspectorLock";
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Type inspector_type = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow", true);
        private static readonly PropertyInfo lock_property = inspector_type.GetProperty("isLocked", flags);
        private static readonly PropertyInfo tracker_property = inspector_type.GetProperty("tracker", flags);
        private static readonly MethodInfo toggle_method = typeof(EditorPathUtility).Assembly
            .GetType("D9speed_BaseEditorUtils.ShortCutExtension", true).GetMethod("toggle_inspector_lock", flags);

        [Serializable] private sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class Report { public string unity; public bool passed; public List<Check> checks = new List<Check>(); }

        private static void require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static bool is_locked(EditorWindow inspector) => (bool)lock_property.GetValue(inspector);
        private static void toggle() => toggle_method.Invoke(null, null);
        private static EditorWindow[] inspectors() => Resources.FindObjectsOfTypeAll(inspector_type).Cast<EditorWindow>().ToArray();
        private static void close_inspectors()
        {
            foreach (var inspector in inspectors()) inspector.Close();
        }

        public static void Run()
        {
            var report = new Report { unity = Application.unityVersion };
            var previous_selection = Selection.objects;
            var first_object = new GameObject("inspector_lock_first");
            var second_object = new GameObject("inspector_lock_second");
            Action<string, Action> check = (name, action) =>
            {
                try { action(); report.checks.Add(new Check { name = name, passed = true, detail = "passed" }); }
                catch (Exception error) { report.checks.Add(new Check { name = name, detail = error.ToString() }); Debug.LogError(error); }
            };
            try
            {
                check("Shortcut is registered with Ctrl+L / Cmd+L as the default", () =>
                {
                    require(ShortcutManager.instance.GetAvailableShortcutIds().Contains(shortcut_id), "Shortcut not registered");
                    var binding = ShortcutManager.instance.GetShortcutBinding(shortcut_id).keyCombinationSequence.ToArray();
                    require(binding.Length == 1 && binding[0].keyCode == KeyCode.L &&
                        binding[0].modifiers == ShortcutModifiers.Action, "Unexpected initial binding");
                });

                check("No Inspector is a no-op and does not create a window or change selection", () =>
                {
                    close_inspectors();
                    Selection.activeGameObject = first_object;
                    toggle();
                    require(inspectors().Length == 0, "Unexpected Inspector was created");
                    require(Selection.activeGameObject == first_object, "Selection changed");
                });

                var first_inspector = (EditorWindow)ScriptableObject.CreateInstance(inspector_type);
                first_inspector.Show();
                check("Lock keeps the displayed object when selection changes; unlock follows selection", () =>
                {
                    first_inspector.Focus();
                    Selection.activeGameObject = first_object;
                    var tracker = (ActiveEditorTracker)tracker_property.GetValue(first_inspector);
                    tracker.ForceRebuild();
                    lock_property.SetValue(first_inspector, false);
                    toggle();
                    require(is_locked(first_inspector), "Inspector did not lock");
                    Selection.activeGameObject = second_object;
                    tracker.ForceRebuild();
                    require(tracker.activeEditors.Any(editor => editor.target == first_object) &&
                        !tracker.activeEditors.Any(editor => editor.target == second_object), "Locked target changed");
                    toggle();
                    tracker.ForceRebuild();
                    require(!is_locked(first_inspector), "Inspector did not unlock");
                    require(tracker.activeEditors.Any(editor => editor.target == second_object), "Unlocked Inspector did not follow selection");
                });

                var second_inspector = (EditorWindow)ScriptableObject.CreateInstance(inspector_type);
                second_inspector.Show();
                check("With multiple Inspectors exactly one toggles and the current focus is preserved", () =>
                {
                    lock_property.SetValue(first_inspector, false);
                    lock_property.SetValue(second_inspector, false);
                    var scene_view = EditorWindow.GetWindow<SceneView>();
                    scene_view.Focus();
                    var open_inspectors = inspectors();
                    var before = open_inspectors.Select(is_locked).ToArray();
                    var focused_before = EditorWindow.focusedWindow;
                    toggle();
                    require(open_inspectors.Where((inspector, index) => is_locked(inspector) != before[index]).Count() == 1,
                        "Expected exactly one Inspector to toggle");
                    require(EditorWindow.focusedWindow == focused_before, "Shortcut stole focus");
                    require(Selection.activeGameObject == second_object, "Shortcut changed selection");
                });

                check("An empty selection does not throw or create an Inspector", () =>
                {
                    Selection.objects = Array.Empty<Object>();
                    int count_before = inspectors().Length;
                    toggle();
                    toggle();
                    require(inspectors().Length == count_before && Selection.objects.Length == 0, "Window count or selection changed");
                });
            }
            finally
            {
                close_inspectors();
                Selection.objects = previous_selection;
                Object.DestroyImmediate(first_object);
                Object.DestroyImmediate(second_object);
            }

            report.passed = report.checks.All(check => check.passed);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/inspector_lock_results.json", JsonUtility.ToJson(report, true));
            Debug.Log($"Inspector lock checks: {report.checks.Count(check => check.passed)}/{report.checks.Count} passed");
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
