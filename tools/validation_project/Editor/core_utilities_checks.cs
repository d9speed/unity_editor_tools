using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace D9speed.PackageValidation
{
    public static class CoreUtilitiesChecks
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        [Serializable] private sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class Report { public bool passed; public string unity; public List<Check> checks = new List<Check>(); }
        private static Type CoreType(string name) => typeof(EditorPathUtility).Assembly.GetType("D9speed_BaseEditorUtils." + name, true);
        private static object Call(string type, string method, params object[] args) => CoreType(type).GetMethod(method, Flags).Invoke(null, args);
        private static void Require(bool condition, string detail) { if (!condition) throw new Exception(detail); }

        public static void Run()
        {
            var report = new Report { unity = Application.unityVersion };
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            var selection = Selection.objects;
            var fixture = "Assets/core_utilities_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
            Action<string, Action> check = (name, action) => {
                try { action(); report.checks.Add(new Check { name = name, passed = true, detail = "passed" }); }
                catch (Exception error) { report.checks.Add(new Check { name = name, detail = error.ToString() }); Debug.LogError(error); }
            };
            try
            {
                check("Core remains Editor-only and shared preferences are registered", () => {
                    Require(!CompilationPipeline.GetAssemblies(AssembliesType.Player).Any(a => a.name == "D9speed.EditorUtils"), "Core included in Player");
                    var provider = (SettingsProvider)Call("EditorUiPreferences", "create_provider");
                    Require(provider.settingsPath == "Preferences/D9speed Tools" && provider.scope == SettingsScope.User, "Provider identity changed");
                    Require(provider.keywords.Contains("AutoHotkey") && provider.keywords.Contains("Prefab"), "Missing settings keywords");
                    Require(EditorApplication.ExecuteMenuItem("D9speed/Settings"), "Settings menu missing");
                });
                check("Full paths resolve Assets, folders, package cache and multiple selections", () => {
                    File.WriteAllText(fixture + "/日本語 file.txt", "test");
                    File.WriteAllText(fixture + "/second.txt", "test");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    var first = AssetDatabase.LoadAssetAtPath<TextAsset>(fixture + "/日本語 file.txt");
                    var second = AssetDatabase.LoadAssetAtPath<TextAsset>(fixture + "/second.txt");
                    Require(EditorPathUtility.GetFullAssetPath("Assets") == Path.GetFullPath(Application.dataPath), "Assets directory");
                    var package = PackageInfo.GetAllRegisteredPackages().First(p => p.source == UnityEditor.PackageManager.PackageSource.Registry);
                    Require(EditorPathUtility.GetFullAssetPath("Packages/" + package.name + "/package.json") == Path.GetFullPath(Path.Combine(package.resolvedPath, "package.json")), "Package cache path");
                    Selection.objects = new Object[] { first, second };
                    Require(EditorApplication.ExecuteMenuItem("Assets/CopyFullPath"), "CopyFullPath menu missing");
                    var result = EditorGUIUtility.systemCopyBuffer.Split(new[] {Environment.NewLine}, StringSplitOptions.None);
                    Require(result.Length == 2 && result.Contains(Path.GetFullPath(fixture + "/日本語 file.txt")) && result.Contains(Path.GetFullPath(fixture + "/second.txt")), "Multiple full paths");
                    Selection.objects = Array.Empty<Object>();
                    EditorGUIUtility.systemCopyBuffer = "unchanged";
                    Call("EditorPathUtility", "CopyFullPath");
                    Require(EditorGUIUtility.systemCopyBuffer == "unchanged", "Empty selection changed clipboard");
                });
                check("Animation paths use the nearest Animator and escape quoted names", () => {
                    var root = new GameObject("outer root");
                    try {
                        root.AddComponent<Animator>();
                        var nested = new GameObject("nested animator"); nested.transform.SetParent(root.transform); nested.AddComponent<Animator>();
                        var child = new GameObject("quoted \"bone\""); child.transform.SetParent(nested.transform);
                        Require(EditorPathUtility.GetAnimationPath(child.transform) == child.name, "Nearest Animator");
                        Require(EditorPathUtility.GetAnimationPath(nested.transform) == string.Empty, "Root should have empty path");
                        Selection.activeGameObject = child;
                        EditorApplication.ExecuteMenuItem("GameObject/Copy Animation Property Path");
                        Require(EditorGUIUtility.systemCopyBuffer == "path: \"quoted \\\"bone\\\"\"", "Quoted property path");
                        var query = (string)Call("SearchSelectedHierarchyPath", "BuildQuery", HierarchyPathHelper.GetHierarchyPath(child.transform));
                        Require(query.Contains("path:\"outer root/") && query.Contains("\\\"bone\\\""), "Hierarchy query quoting");
                    } finally { Object.DestroyImmediate(root); }
                });
                check("Transform reset restores all selected local transforms with Undo", () => {
                    var go = new GameObject("reset probe");
                    try {
                        go.transform.localPosition = new Vector3(2,3,4); go.transform.localRotation = Quaternion.Euler(10,20,30); go.transform.localScale = Vector3.one * 2;
                        var rotation = go.transform.localRotation;
                        Selection.activeGameObject = go;
                        Undo.IncrementCurrentGroup(); Call("ShortCutExtension", "RunTransformReset");
                        Require(go.transform.localPosition == Vector3.zero && go.transform.localScale == Vector3.one && go.transform.localRotation == Quaternion.identity, "Reset values");
                        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                        Require(go.transform.localPosition == new Vector3(2,3,4) && go.transform.localScale == Vector3.one * 2 && Quaternion.Angle(go.transform.localRotation, rotation) < .001f, "Reset undo");
                    } finally { Object.DestroyImmediate(go); }
                });
                check("Component context copy preserves order, values and Undo", () => {
                    var source = new GameObject("component source"); var target = new GameObject("component target");
                    try {
                        var first = source.AddComponent<BoxCollider>(); first.size = new Vector3(2,3,4);
                        var second = source.AddComponent<SphereCollider>(); second.radius = 2.5f;
                        Call("CopyComponentNameMenu", "CopyComponentName", new MenuCommand(first));
                        Require(EditorGUIUtility.systemCopyBuffer == "BoxCollider", "Component name");
                        Call("MultiComponentCopyPasteMenu", "CopyComponentsFromHere", new MenuCommand(first));
                        Undo.IncrementCurrentGroup();
                        Call("MultiComponentCopyPasteMenu", "PasteComponentsAsNew", target);
                        Require(target.GetComponent<BoxCollider>().size == first.size && target.GetComponent<SphereCollider>().radius == second.radius, "Copied component values");
                        Require(target.GetComponents<Component>()[1] is BoxCollider && target.GetComponents<Component>()[2] is SphereCollider, "Copied component order");
                        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                        Require(target.GetComponent<BoxCollider>() == null && target.GetComponent<SphereCollider>() == null, "Component copy undo");
                    } finally { Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
                });
                check("Mirror UI defaults match behavior and scaled scene objects support Undo", () => {
                    var parent = new GameObject("scaled parent"); parent.transform.localScale = Vector3.one * 2;
                    var source = new GameObject("mirror probe"); source.transform.SetParent(parent.transform, false); source.transform.localPosition = new Vector3(2,1,0);
                    var window = ScriptableObject.CreateInstance<TransformMirrorTool>();
                    GameObject clone = null;
                    try {
                        window.CreateGUI();
                        Require(window.rootVisualElement.Query<Toggle>().ToList().Count(t => t.value) == 2, "Initial toggles disagree with options");
                        var list = (List<Object>)typeof(TransformMirrorTool).GetField("targetObjects", Flags).GetValue(window); list.Add(source);
                        Undo.IncrementCurrentGroup(); typeof(TransformMirrorTool).GetMethod("InstantiateMirroredAll", Flags).Invoke(window, null);
                        clone = GameObject.Find("mirror probe_Mirrored");
                        Require(clone != null && clone.transform.position == new Vector3(-4,2,0) && clone.transform.lossyScale == Vector3.one * 2, "World transform mirror");
                        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                        Require(clone == null && source.transform.position == new Vector3(4,2,0), "Mirror undo or source changed");
                    } finally { if (clone != null) Object.DestroyImmediate(clone); Object.DestroyImmediate(window); Object.DestroyImmediate(parent); }
                });
                check("Asset folder copies remap internal references without overwriting existing copies", () => {
                    var folder = fixture + "/Copy source"; AssetDatabase.CreateFolder(fixture, "Copy source"); AssetDatabase.CreateFolder(folder, "empty");
                    var texture = new Texture2D(2,2); AssetDatabase.CreateAsset(texture, folder + "/texture.asset");
                    var material = new Material(Shader.Find("Standard")); material.mainTexture = texture; AssetDatabase.CreateAsset(material, folder + "/material.mat"); AssetDatabase.SaveAssets();
                    var map = CopyAssetsWithDependency.DeepCopy(new[] { folder, folder + "/material.mat" }, false);
                    var copied = AssetDatabase.LoadAssetAtPath<Material>(map[folder + "/material.mat"]);
                    Require(copied.mainTexture == AssetDatabase.LoadAssetAtPath<Texture2D>(map[folder + "/texture.asset"]), "Internal texture reference not remapped");
                    Require(copied.shader == material.shader && material.mainTexture == texture, "External reference or source changed");
                    Require(AssetDatabase.IsValidFolder(map[folder] + "/empty"), "Empty folder lost");
                    var again = CopyAssetsWithDependency.DeepCopy(new[] {folder}, false);
                    Require(again[folder] != map[folder] && AssetDatabase.LoadAssetAtPath<Material>(map[folder + "/material.mat"]) == copied, "Destination overwritten");
                    var rejected = false;
                    try { CopyAssetsWithDependency.GetAllAssetAndCopyPaths(new[] {"Packages/io.github.d9speed.editor_core"}); } catch (ArgumentException) { rejected = true; }
                    Require(rejected, "Package source accepted for direct duplication");
                });
                check("Preferences persist locally and external command paths are quoted without launching", () => {
                    var keys = new[] {"D9speed_Common_UseCustomUiFont", "D9speed_Common_UiFontAssetPath", "D9speed_Core_ShowPrefabOverrideIcon", "D9speed_Core_AutoHotkeyExecutablePath", "D9speed_Core_SakuraGrepScriptPath"};
                    var exists = keys.ToDictionary(k => k, EditorPrefs.HasKey);
                    var old_strings = keys.ToDictionary(k => k, k => EditorPrefs.GetString(k, ""));
                    var old_font = D9speedCommonEditorPrefs.UseCustomUiFont;
                    var old_icon = D9speedCommonEditorPrefs.ShowPrefabOverrideIcon;
                    try {
                        D9speedCommonEditorPrefs.UseCustomUiFont = false; D9speedCommonEditorPrefs.UiFontAssetPath = "";
                        Require(D9speedEditorFontUtility.GetConfiguredFont() == null, "Standard font fallback");
                        D9speedCommonEditorPrefs.ShowPrefabOverrideIcon = false;
                        Require(!EditorPrefs.GetBool(keys[2], true), "Icon preference persistence");
                        var executable = Path.GetFullPath(fixture + "/fake executable.exe"); File.WriteAllText(executable, "test only");
                        var script = Path.GetFullPath(fixture + "/grep script.ahk"); File.WriteAllText(script, "test only");
                        D9speedCommonEditorPrefs.AutoHotkeyExecutablePath = executable; D9speedCommonEditorPrefs.SakuraGrepScriptPath = script;
                        var info = (System.Diagnostics.ProcessStartInfo)Call("EditorExternalTools", "CreateStartInfo", Path.GetFullPath(fixture));
                        Require(info.FileName == executable && !info.UseShellExecute && info.Arguments == "\"" + script + "\" \"" + Path.GetFullPath(fixture) + "\"", "External command quoting");
                    } finally {
                        foreach (var key in keys) { if (!exists[key]) EditorPrefs.DeleteKey(key); else if (key == keys[0]) EditorPrefs.SetBool(key, old_font); else if (key == keys[2]) EditorPrefs.SetBool(key, old_icon); else EditorPrefs.SetString(key, old_strings[key]); }
                    }
                });
            }
            finally
            {
                Selection.objects = selection; EditorGUIUtility.systemCopyBuffer = clipboard;
                // This directory was uniquely created by this test, within Assets.
                AssetDatabase.DeleteAsset(fixture);
            }
            report.passed = report.checks.All(c => c.passed);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/core_utilities_checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
