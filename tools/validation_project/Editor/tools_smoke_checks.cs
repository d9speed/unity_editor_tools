using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageExporter = D9speedBaseEditorUtil.PackageExporter;

namespace D9speed.PackageValidation
{
    public static class ToolsSmokeChecks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static void Run(Action<string, Action> check)
        {
            check("Bundled dictionary loads without any external path", () =>
            {
                var type = typeof(HumanoidAliasComponentCopierWindow);
                var path = (string)type.GetMethod("get_dictionary_path", flags).Invoke(null, null);
                Require(path.Replace('\\', '/').EndsWith("/Editor/Data/humanoid_bone_dictionary.json"), path);
                Require(File.Exists(path), "Bundled dictionary missing");
                var window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
                try
                {
                    var aliases = (Dictionary<string, List<string>>)Get(window, "aliases");
                    Require(aliases.Count == 54 && aliases["LeftUpperLeg"].Contains("Upper_Leg.L"), "Dictionary content");
                    Require(window.rootVisualElement.childCount > 0, "Alias UI missing");
                }
                finally { Object.DestroyImmediate(window); }
            });
            check("Alias copy maps different bone names, remaps references and supports Undo", () =>
            {
                var source = new GameObject("copy_source");
                var target = new GameObject("copy_target");
                var source_hips = Child(source, "Hips");
                var target_hips = Child(target, "Hips");
                var source_leg = Child(source_hips, "Upper_Leg.L");
                var target_leg = Child(target_hips, "LeftUpperLeg");
                var constraint = source_leg.AddComponent<ParentConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = source_hips.transform, weight = 1 });
                constraint.weight = 0.75f;
                var collider = source_leg.AddComponent<BoxCollider>();
                collider.size = new Vector3(2, 3, 4);
                var window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
                try
                {
                    ((ObjectField)Get(window, "sourceField")).SetValueWithoutNotify(source);
                    ((ObjectField)Get(window, "targetField")).SetValueWithoutNotify(target);
                    Call(window, "Scan");
                    Call(window, "ExecuteCopy");
                    var copied = target_leg.GetComponent<ParentConstraint>();
                    var warnings = string.Join("; ", (List<string>)Get(window, "warnings"));
                    Require(copied != null && copied.weight == 0.75f, warnings);
                    Require(copied.GetSource(0).sourceTransform == target_hips.transform, "Constraint reference not remapped");
                    Require(target_leg.GetComponent<BoxCollider>().size == collider.size, "Collider values");
                    Undo.PerformUndo();
                    Require(target_leg.GetComponent<ParentConstraint>() == null && target_leg.GetComponent<BoxCollider>() == null, "Copy Undo");
                }
                finally { Object.DestroyImmediate(window); Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
            });
            check("Rename UI and literal, case-insensitive, regex replacements work", () =>
            {
                var window = ScriptableObject.CreateInstance<RenameToolWindow>();
                try
                {
                    Call(window, "CreateGUI");
                    Require(window.rootVisualElement.childCount > 0, "Rename UI missing");
                    var helper = typeof(RenameToolWindow).Assembly.GetType("D9speed_HelperUtility");
                    var replace = helper.GetMethod("ApplyReplace", flags);
                    Func<string, string, string, bool, bool, string> run = (input, find, value, regex, sensitive) =>
                        (string)replace.Invoke(null, new object[] { input, find, value, regex, sensitive });
                    Require(run("Arm.arm", "arm", "$1", false, false) == "$1.$1", "Literal replacement");
                    Require(run("Arm_arm", "arm", "Leg", false, true) == "Arm_Leg", "Case-sensitive replacement");
                    Require(run("bone_12", @"_(\d+)$", "_$1_L", true, true) == "bone_12_L", "Regex groups");
                    Require(run("bone", "[", "x", true, true) == "bone", "Invalid regex fallback");
                }
                finally { Object.DestroyImmediate(window); }
            });
            check("Hierarchy rename changes only selected objects and supports Undo", () =>
            {
                var target = new GameObject("old_name");
                var other = new GameObject("untouched");
                var window = ScriptableObject.CreateInstance<RenameToolWindow>();
                try
                {
                    Undo.IncrementCurrentGroup();
                    Call(window, "ApplyHierarchyChanges", new List<RenamePreviewItem> { RenamePreviewItem.HierarchyObject(target, target.name, "new_name") });
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                    Require(target.name == "new_name" && other.name == "untouched", "Rename targets");
                    Undo.PerformUndo();
                    Require(target.name == "old_name", "Rename Undo");
                }
                finally { Object.DestroyImmediate(window); Object.DestroyImmediate(target); Object.DestroyImmediate(other); }
            });
            check("Exporter UI loads stylesheet from the package", () =>
            {
                var window = ScriptableObject.CreateInstance<PackageExporter>();
                try
                {
                    Call(window, "CreateGUI");
                    var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.d9speed.package_exporter/Editor/PackageExporter.uss");
                    Require(stylesheet != null && window.rootVisualElement.styleSheets.Contains(stylesheet), "Package stylesheet");
                    Require(window.rootVisualElement.childCount > 0, "Exporter UI missing");
                }
                finally { Object.DestroyImmediate(window); }
            });
            check("Exporter JSON profile preserves settings through save and load", () =>
            {
                var window = ScriptableObject.CreateInstance<PackageExporter>();
                try
                {
                    Directory.CreateDirectory("Logs");
                    Set(window, "packageName", "検証_package");
                    Set(window, "additionalNameStrings", new List<string> { "alpha", "日本語" });
                    Require((bool)Call(window, "TryWriteProfileToJson", "Logs/export_profile.json"), "Profile write");
                    var args = new object[] { "Logs/export_profile.json", null };
                    Require((bool)typeof(PackageExporter).GetMethod("TryReadProfileFromJson", flags).Invoke(window, args), "Profile read");
                    Require((string)Get(args[1], "packageName") == "検証_package", "Profile package name");
                    Require(((List<string>)Get(args[1], "additionalNameStrings")).SequenceEqual(new[] { "alpha", "日本語" }), "Profile extra parts");
                }
                finally { Object.DestroyImmediate(window); }
            });
            check("Exporter writes a real unitypackage and JSON asset report", () =>
            {
                // Only new fixtures in the dedicated validation project are exported.
                var fixture = "Assets/export_fixture_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
                File.WriteAllText(fixture + "/sample.txt", "package validation fixture");
                AssetDatabase.ImportAsset(fixture + "/sample.txt");
                var output = Path.GetFullPath("Logs/export_output_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(output);
                var window = ScriptableObject.CreateInstance<PackageExporter>();
                try
                {
                    Set(window, "outPath", output);
                    Set(window, "packageName", "package_validation");
                    Set(window, "openWithTE", true); // Select non-interactive export; ProcessFolder never launches TE.
                    var result = (string)Call(window, "ProcessFolder", fixture, "test", null, false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    Require(File.Exists(result) && new FileInfo(result).Length > 0, "Unitypackage output");
                    var report = Directory.GetFiles(output, "*_components.json").Single();
                    Require(File.ReadAllText(report).Contains(fixture + "/sample.txt"), "Asset report content");
                }
                finally { Object.DestroyImmediate(window); }
            });
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
