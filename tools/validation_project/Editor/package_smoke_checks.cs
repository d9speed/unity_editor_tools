using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class PackageSmokeChecks
    {
        [Serializable] private sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class Report
        {
            public string unity_version;
            public string utc;
            public bool passed;
            public List<Check> checks = new List<Check>();
        }

        public static void Run()
        {
            var report = new Report { unity_version = Application.unityVersion, utc = DateTime.UtcNow.ToString("o") };
            Action<string, Action> check = (name, action) =>
            {
                try { action(); report.checks.Add(new Check { name = name, passed = true, detail = "passed" }); }
                catch (Exception error)
                {
                    report.checks.Add(new Check { name = name, passed = false, detail = error.ToString() });
                    Debug.LogError(name + ": " + error);
                }
            };
            check("All five packages registered with expected versions and no optional SDK", () =>
            {
                var packages = PackageInfo.GetAllRegisteredPackages();
                foreach (var suffix in new[] { "editor_core", "scene_tools", "humanoid_alias_copy", "package_exporter", "rename_tool" })
                    Require(packages.Any(p => p.name == "io.github.d9speed." + suffix && p.version == (suffix == "editor_core" ? "0.1.1" : "0.1.0")), "Missing package: " + suffix);
                Require(packages.Any(p => p.name == "com.unity.nuget.newtonsoft-json" && p.version == "3.2.1"), "Newtonsoft dependency");
                Require(!packages.Any(p => p.name.StartsWith("com.vrchat.") || p.name.StartsWith("nadena.dev.")),
                    "This smoke test must run without optional VRChat packages.");
            });
            check("Tool code belongs to Editor-only package assemblies", () =>
            {
                var editor = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                foreach (var name in new[] { "D9speed.EditorUtils", "D9speed.SceneTools.Editor", "D9speed.HumanoidAliasCopy.Editor", "D9speed.PackageExporter.Editor", "D9speed.RenameTool.Editor" })
                    Require(editor.Any(a => a.name == name && (a.flags & AssemblyFlags.EditorAssembly) != 0), name);
                var player = CompilationPipeline.GetAssemblies(AssembliesType.Player);
                Require(!player.Any(a => a.name.StartsWith("D9speed.")),
                    "Editor tools must not appear in player assemblies.");
            });
            check("Source GUIDs resolve inside the installed packages", () =>
            {
                var expected = new Dictionary<string, string> {
                    { "a2fb06f2662d2d64b8d88e8632f3cba3", "Packages/io.github.d9speed.scene_tools/Editor/DisplayChildNamesInScene.cs" },
                    { "92704199356609549aae141fe7fe7d27", "Packages/io.github.d9speed.scene_tools/Editor/scene_package_info_overlay.cs" },
                    { "ae9f755b1cfa4389a945f322386ca97f", "Packages/io.github.d9speed.editor_core/Editor/editor_object_helper.cs" }
                };
                foreach (var pair in expected) Require(AssetDatabase.GUIDToAssetPath(pair.Key) == pair.Value, pair.Value);
            });
            check("Shared object helper handles inactive objects", () =>
            {
                var type = FindType("D9speed_BaseEditorUtils.EditorObjectHelper", "D9speed.EditorUtils");
                var finder = type.GetMethod("FindSceneObjects").MakeGenericMethod(typeof(GameObject));
                var active = new GameObject("package_smoke_active");
                var inactive = new GameObject("package_smoke_inactive");
                inactive.SetActive(false);
                try
                {
                    var active_only = (GameObject[])finder.Invoke(null, new object[] { false });
                    var all = (GameObject[])finder.Invoke(null, new object[] { true });
                    Require(active_only.Contains(active) && !active_only.Contains(inactive), "Active-only discovery");
                    Require(all.Contains(active) && all.Contains(inactive), "Include-inactive discovery");
                    Require((long)type.GetMethod("GetObjectId").Invoke(null, new object[] { active }) != 0, "Object ID");
                }
                finally { Object.DestroyImmediate(active); Object.DestroyImmediate(inactive); }
            });
            check("Both Scene View overlay panels can be constructed", () =>
            {
                foreach (var name in new[] { "DisplayChildNamesInSceneOverlay", "D9speed.ScenePackageInfo.scene_package_info_overlay" })
                {
                    var type = FindType(name, "D9speed.SceneTools.Editor");
                    Require(type.GetCustomAttributes(false).Any(a => a.GetType().Name == "OverlayAttribute"), "Overlay registration");
                    var overlay = Activator.CreateInstance(type);
                    var panel = type.GetMethod("CreatePanelContent").Invoke(overlay, null) as VisualElement;
                    Require(panel != null, name);
                    if (panel is IMGUIContainer imgui)
                        Require(imgui.onGUIHandler != null, name + " has no IMGUI callback");
                    else
                        Require(panel.childCount > 0, name + " has no UI Toolkit content");
                }
            });
            check("Package info works with optional integrations absent", () =>
            {
                var type = FindType("D9speed.ScenePackageInfo.package_versions", "D9speed.SceneTools.Editor");
                var result = type.GetMethod("read", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Require(result is string, "Package version read returned no string");
            });
            check("Mesh counts work when CPU mesh data is not readable", () =>
            {
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one } };
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                mesh.UploadMeshData(true);
                try
                {
                    var type = FindType("D9speed.ScenePackageInfo.scene_package_info_overlay", "D9speed.SceneTools.Editor");
                    var result = (long)type.GetMethod("count_triangles", BindingFlags.NonPublic | BindingFlags.Static)
                        .Invoke(null, new object[] { mesh });
                    Require(result == 2, "Expected two triangles");
                }
                finally { Object.DestroyImmediate(mesh); }
            });
            ToolsSmokeChecks.Run(check);
            report.passed = report.checks.All(c => c.passed);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/package_smoke_results.json", JsonUtility.ToJson(report, true));
            Debug.Log("Package smoke checks: " + report.checks.Count(c => c.passed) + "/" + report.checks.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static Type FindType(string name, string assembly)
        {
            var type = Type.GetType(name + ", " + assembly, true);
            Require(type != null, "Type not found: " + name);
            return type;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
