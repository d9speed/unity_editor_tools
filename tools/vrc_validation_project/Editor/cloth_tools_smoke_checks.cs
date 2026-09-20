using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_Test_Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class ClothToolsSmokeChecks
    {
        [Serializable] private sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class Report { public bool passed; public string unity_version; public string sdk_version; public List<Check> checks = new List<Check>(); }
        private const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        public static void Run()
        {
            var report = new Report { unity_version = Application.unityVersion };
            Action<string, Action> check = (name, action) => {
                try { action(); report.checks.Add(new Check { name = name, passed = true, detail = "passed" }); }
                catch (Exception error) { report.checks.Add(new Check { name = name, passed = false, detail = error.ToString() }); Debug.LogError(name + ": " + error); }
            };
            check("Ten D9speed packages coexist with the expected SDK and remain Editor-only", () => {
                var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                require(packages.Count(p => p.name.StartsWith("io.github.d9speed.")) == 10, "D9speed package count");
                var args = Environment.GetCommandLineArgs();
                var expected_index = Array.IndexOf(args, "-d9speedExpectedSdkVersion");
                var expected = expected_index >= 0 ? args[expected_index + 1] : "3.10.3";
                report.sdk_version = packages.Single(p => p.name == "com.vrchat.avatars").version;
                require(report.sdk_version == expected, "SDK version");
                require(!CompilationPipeline.GetAssemblies(AssembliesType.Player).Any(a => a.name.StartsWith("D9speed.")), "Player assembly leak");
            });
            check("Both cloth fitting menus open", () => {
                open<PhysBoneWeightColliderGeneratorWindow>("D9speed/Tools/PhysBone Weight Collider Generator");
                open<ProxVRCphysboneSettings>("D9speed/Tools/ProxVRCphysboneSettings");
            });
            check("Weighted synthetic mesh creates a capsule with correct bone, bounds and Undo", () => {
                var root = new GameObject("collider_fixture");
                var bone = new GameObject("weighted_bone"); bone.transform.SetParent(root.transform, false);
                var mesh = new Mesh();
                var points = new List<Vector3>();
                foreach (float y in new[] { -1f, 0f, 1f }) foreach (var point in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }) points.Add(point * .1f + Vector3.up * y);
                mesh.vertices = points.ToArray(); mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = points.Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
                var renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.bones = new[] { bone.transform };
                try {
                    var groups = PhysBoneWeightColliderGenerator.GetWeightedBoneGroups(renderer);
                    require(groups.Count == 1 && groups[0].weighted_vertex_count == 12, "Weighted bone discovery");
                    Undo.IncrementCurrentGroup();
                    var result = PhysBoneWeightColliderGenerator.CreateCapsule(renderer, bone.transform,
                        new PhysBoneWeightColliderSettings { percentile_clip = 0, radius_percentile = 100, diameter_padding_meters = 0 }, true);
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                    require(result.vertex_count == 12 && result.collider.rootTransform == bone.transform, "Collider target");
                    require(result.collider.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule, "Capsule type");
                    require(Mathf.Abs(result.radius - .1f) < .001f && Mathf.Abs(result.height - 2.2f) < .001f, "Capsule bounds");
                    Undo.PerformUndo(); require(result.holder == null, "Created collider Undo");
                } finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
            });
            check("Proxy setup creates rotation and parent constraints without moving targets", () => {
                var root = new GameObject("proxy_fixture");
                var actual = new GameObject("actual"); actual.transform.SetParent(root.transform, false);
                var child = new GameObject("actual_child"); child.transform.SetParent(actual.transform, false); child.transform.localPosition = Vector3.up * .1f;
                var proxy = new GameObject("proxy"); proxy.transform.SetParent(root.transform, false); proxy.transform.localPosition = Vector3.right * .02f;
                var window = ScriptableObject.CreateInstance<ProxVRCphysboneSettings>();
                try {
                    typeof(ProxVRCphysboneSettings).GetField("actualChainRoot", flags).SetValue(window, actual.transform);
                    ((List<Transform>)typeof(ProxVRCphysboneSettings).GetField("proxyTargets", flags).GetValue(window)).Add(proxy.transform);
                    var before = proxy.transform.localPosition;
                    var entries = (IList)typeof(ProxVRCphysboneSettings).GetMethod("BuildPreviewEntries", flags).Invoke(window, null);
                    var entry = entries[0];
                    require((bool)entry.GetType().GetField("CanApply", flags).GetValue(entry), "Preview must resolve sources");
                    Undo.IncrementCurrentGroup();
                    typeof(ProxVRCphysboneSettings).GetMethod("SetupRotationConstraint", flags).Invoke(window, new[] { entry });
                    var rotation = proxy.GetComponent<VRCRotationConstraint>();
                    require(rotation != null && rotation.Sources.Count == 2 && rotation.IsActive, "Rotation constraint");
                    require(Mathf.Abs(rotation.Sources[0].Weight + rotation.Sources[1].Weight - 1f) < .001f, "Normalized weights");
                    require(proxy.transform.localPosition == before, "Rotation setup moved proxy");
                    Object.DestroyImmediate(rotation);
                    typeof(ProxVRCphysboneSettings).GetMethod("SetupParentConstraint", flags).Invoke(window, new[] { entry });
                    var parent = proxy.GetComponent<VRCParentConstraint>();
                    require(parent != null && parent.Sources.Count == 2 && parent.IsActive, "Parent constraint");
                    require(parent.Sources[0].SourceTransform == actual.transform || parent.Sources[1].SourceTransform == actual.transform, "Actual source reference");
                    require(proxy.transform.localPosition == before, "Parent setup moved proxy");
                } finally { Object.DestroyImmediate(window); Object.DestroyImmediate(root); }
            });
            report.passed = report.checks.All(c => c.passed);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/cloth_tools_smoke_results.json", JsonUtility.ToJson(report, true));
            Debug.Log("Cloth tools checks: " + report.checks.Count(c => c.passed) + "/" + report.checks.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void open<T>(string path) where T : EditorWindow {
            var existing = Resources.FindObjectsOfTypeAll<T>();
            try { require(EditorApplication.ExecuteMenuItem(path), path); require(Resources.FindObjectsOfTypeAll<T>().Any(), "Window did not open"); }
            finally { foreach (var window in Resources.FindObjectsOfTypeAll<T>().Except(existing)) window.Close(); }
        }
        private static void require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
