using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speedBaseEditorUtil;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class ExporterCompareSmokeChecks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [Serializable] private sealed class Report
        {
            public string package_file = "validation.unitypackage";
            public string summary = "比較ツール検証";
            public List<Instance> component_instances = new List<Instance>();
        }
        [Serializable] private sealed class Instance
        {
            public string category = "Validation";
            public string type_name;
            public string type_full_name;
            public string prefab_path = "Assets/Fixture.prefab";
            public string object_path;
            public string parent_path = "Root";
        }

        public static void Run(Action<string, Action> check)
        {
            check("Exporter and report comparer open from sibling ExportBatch menu entries", () =>
            {
                open_menu<PackageExporter>("D9speed/ExportBatch/ExportBatch");
                open_menu<PackageComponentReportCompareWindow>("D9speed/ExportBatch/Package Component Report Compare");
                require(typeof(PackageExporter).Assembly == typeof(PackageComponentReportCompareWindow).Assembly, "Tools must share the package assembly");
                require(AssetDatabase.GUIDToAssetPath("9b9d1f5db51348e5ad7dd8a752a11b7d") ==
                    "Packages/io.github.d9speed.package_exporter/Editor/PackageComponentReportCompareWindow.cs", "Comparer source GUID");
            });

            check("Report comparer reads multiple JSON reports and identifies all difference categories", () =>
            {
                // These are synthetic JSON records; no prefab asset is read or created.
                var reference = new Report { component_instances = new List<Instance> {
                    instance("Unchanged", "Root/Same"), instance("Missing", "Root/Missing"),
                    instance("Count", "Root/CountA"), instance("Location", "Root/Left")
                } };
                var changed = new Report { component_instances = new List<Instance> {
                    instance("Unchanged", "Root/Same"), instance("Extra", "Root/Extra"),
                    instance("Count", "Root/CountA"), instance("Count", "Root/CountB"), instance("Location", "Root/Right")
                } };
                var directory = Path.GetFullPath("Logs/compare_fixture_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var reference_path = Path.Combine(directory, "reference_components.json");
                var target_path = Path.Combine(directory, "target_components.json");
                var reference_json = JsonUtility.ToJson(reference, true);
                var target_json = JsonUtility.ToJson(changed, true);
                File.WriteAllText(reference_path, reference_json);
                File.WriteAllText(target_path, target_json);
                var window = ScriptableObject.CreateInstance<PackageComponentReportCompareWindow>();
                try
                {
                    set(window, "reference_json_path", reference_path);
                    set(window, "target_json_paths", new List<string> { target_path, reference_path });
                    call(window, "ReloadReports");
                    var rows = ((IEnumerable)get(window, "rows")).Cast<object>().ToDictionary(row => (string)get(row, "component_label"));
                    var expected = new Dictionary<string, string> {
                        { "Unchanged", "Ok" }, { "Missing", "Missing" }, { "Extra", "Extra" },
                        { "Count", "CountDiff" }, { "Location", "LocationDiff" }
                    };
                    require(rows.Count == expected.Count, "Comparison row count");
                    foreach (var pair in expected)
                    {
                        var row = rows[pair.Key];
                        require(get(row, "status").ToString() == pair.Value, "Overall status: " + pair.Key);
                        var targets = ((IEnumerable)get(row, "targets")).Cast<object>().ToArray();
                        require(targets.Length == 2, "Two target columns");
                        require(get(targets[0], "status").ToString() == pair.Value, "Changed target: " + pair.Key);
                        require(get(targets[1], "status").ToString() == "Ok", "Identical target: " + pair.Key);
                    }
                    set(window, "only_differences", true);
                    call(window, "UpdateVisibleRows");
                    require(((IList)get(window, "visible_rows")).Count == 4, "Differences-only filter");
                    set(window, "filter_text", "Root/Right");
                    call(window, "UpdateVisibleRows");
                    require(((IList)get(window, "visible_rows")).Count == 1, "Location filter");
                    require(File.ReadAllText(reference_path) == reference_json && File.ReadAllText(target_path) == target_json,
                        "Comparison must not change input reports");
                }
                finally { Object.DestroyImmediate(window); }
            });
        }

        private static void open_menu<T>(string path) where T : EditorWindow
        {
            var existing = Resources.FindObjectsOfTypeAll<T>();
            try
            {
                require(EditorApplication.ExecuteMenuItem(path), "Menu not found: " + path);
                require(Resources.FindObjectsOfTypeAll<T>().Length > 0, "Window did not open: " + path);
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<T>().Except(existing)) window.Close();
            }
        }
        private static Instance instance(string name, string path) =>
            new Instance { type_name = name, type_full_name = "Validation." + name, object_path = path };
        private static object get(object target, string name) => target.GetType().GetField(name, flags).GetValue(target);
        private static void set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        private static void call(object target, string name) => target.GetType().GetMethod(name, flags).Invoke(target, null);
        private static void require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
