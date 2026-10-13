using System;
using System.Collections;
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

namespace D9speed.HumanoidAliasValidation
{
    public static class humanoid_alias_ma_checks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        [Serializable] private sealed class result { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class report { public string unity; public bool passed; public string ma_version; public List<result> checks = new List<result>(); }
        private static readonly report results = new report();
        private static Type find_type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        private static Type ma_type(string name) => find_type("nadena.dev.modular_avatar.core." + name);
        private static object get(object obj, string name) => obj.GetType().GetField(name, flags).GetValue(obj);
        private static void set(object obj, string name, object value) => obj.GetType().GetField(name, flags).SetValue(obj, value);
        private static object call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, flags).Invoke(obj, args);
        private static object group(string name) => Enum.Parse(typeof(HumanoidAliasComponentCopierWindow).GetNestedType("copy_group", flags), name);
        private static void require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void check(string name, Action action)
        {
            try { action(); results.checks.Add(new result { name = name, passed = true, detail = "ok" }); }
            catch (Exception error) { results.checks.Add(new result { name = name, passed = false, detail = error.ToString() }); Debug.LogError(name + ": " + error); }
        }
        private static GameObject child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static void edit(Component component, Action<SerializedObject> action)
        { using (var serialized = new SerializedObject(component)) { action(serialized); serialized.ApplyModifiedPropertiesWithoutUndo(); } }
        private static void reference(SerializedObject serialized, string name, GameObject obj, string path)
        {
            var property = serialized.FindProperty(name); require(property != null, "Missing property: " + name);
            property.FindPropertyRelative("targetObject").objectReferenceValue = obj;
            property.FindPropertyRelative("referencePath").stringValue = path;
        }
        private static void empty_reference(Component component, string name)
        {
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.FindProperty(name);
                require(property.FindPropertyRelative("targetObject").objectReferenceValue == null && property.FindPropertyRelative("referencePath").stringValue == "", "Reference not empty: " + name);
            }
        }

        private sealed class fixture : IDisposable
        {
            public readonly GameObject source_avatar = new GameObject("source_avatar"), target_avatar = new GameObject("target_avatar");
            public readonly GameObject source, target, source_mesh, target_mesh, body, source_helper, target_helper;
            public readonly HumanoidAliasComponentCopierWindow window;
            public fixture(bool paired = true)
            {
                var descriptor = find_type("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descriptor != null) { source_avatar.AddComponent(descriptor); target_avatar.AddComponent(descriptor); }
                body = child(source_avatar, "Body"); child(body, "Hips");
                source = child(source_avatar, "Standard_Maid_Lime"); target = child(target_avatar, "Standard_Maid_Rurune");
                source_mesh = child(source, "St_Maid_Lime_Lowfar"); target_mesh = child(target, paired ? "St_Maid_Rurune_Lowfar" : "unrelated_destination");
                source_mesh.AddComponent<SkinnedMeshRenderer>(); if (paired) target_mesh.AddComponent<SkinnedMeshRenderer>();
                source_helper = child(source_mesh, "local_anchor"); target_helper = child(target_mesh, "local_anchor");
                window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
                ((ObjectField)get(window, "sourceField")).SetValueWithoutNotify(source);
                ((ObjectField)get(window, "targetField")).SetValueWithoutNotify(target);
                set(window, "copySkinnedMeshMaterials", false); scan();
            }
            public Component add(string name, GameObject obj = null) => (obj ?? source_mesh).AddComponent(ma_type(name));
            public void scan() { call(window, "select_copy_tab", group("modular_avatar")); }
            public void copy()
            {
                call(window, "ExecuteCopy");
                require(((List<string>)get(window, "warnings")).Any(w => w.StartsWith("コピー完了:")), string.Join(";", (List<string>)get(window, "warnings")));
            }
            public Component copied(string name) => target_mesh.GetComponent(ma_type(name));
            public void Dispose() { Object.DestroyImmediate(window); Object.DestroyImmediate(source_avatar); Object.DestroyImmediate(target_avatar); Undo.ClearAll(); }
        }
        private static Component add_sync(fixture f, GameObject obj = null)
        {
            var sync = f.add("ModularAvatarBlendshapeSync", obj);
            edit(sync, serialized =>
            {
                var bindings = serialized.FindProperty("Bindings"); bindings.arraySize = 1;
                reference(serialized, "Bindings.Array.data[0].ReferenceMesh", f.body, "Body");
                serialized.FindProperty("Bindings.Array.data[0].Blendshape").stringValue = "source_body_shape";
                serialized.FindProperty("Bindings.Array.data[0].LocalBlendshape").stringValue = "clothing_shape";
            });
            return sync;
        }
        private static int binding_count(Component component) { using (var serialized = new SerializedObject(component)) return serialized.FindProperty("Bindings").arraySize; }

        public static void run()
        {
            results.unity = Application.unityVersion;
            var sync_type = ma_type("ModularAvatarBlendshapeSync");
            results.ma_version = sync_type == null ? "absent" : UnityEditor.PackageManager.PackageInfo.FindForAssembly(sync_type.Assembly)?.version;
            check("MA reset defaults on and external Unity Constraint references remain intact", () =>
            {
                using (var f = new fixture())
                {
                    require((bool)get(f.window, "clear_ma_avatar_references"), "Default disabled");
                    var constraint = f.source_mesh.AddComponent<ParentConstraint>(); constraint.AddSource(new ConstraintSource { sourceTransform = f.body.transform, weight = 1 });
                    call(f.window, "select_copy_tab", group("constraint")); f.copy();
                    require(f.target_mesh.GetComponent<ParentConstraint>().GetSource(0).sourceTransform == f.body.transform, "Non-MA external reference changed");
                }
            });
            if (sync_type != null)
            {
                check("Paired differently named meshes receive empty Blend Shape Sync; source and enabled state preserved; Undo", () =>
                {
                    using (var f = new fixture())
                    {
                        var sync = add_sync(f); ((Behaviour)sync).enabled = false; f.scan();
                        require(((IEnumerable)get(f.window, "copyRows")).Cast<object>().Any(row => ((string)get(row, "Operation")).Contains("参照未設定")), "Preview does not explain reset");
                        f.copy(); var copied = f.copied("ModularAvatarBlendshapeSync"); require(copied != null && binding_count(copied) == 0, "Bindings copied unchanged");
                        require(!((Behaviour)copied).enabled && binding_count(sync) == 1, "Other settings or source changed");
                        Undo.PerformUndo(); require(f.copied("ModularAvatarBlendshapeSync") == null && f.target_mesh.GetComponent<SkinnedMeshRenderer>() != null, "Undo removed renderer or left sync");
                    }
                });
                check("Mesh Settings clears direct and path-only body references while preserving bounds and modes", () =>
                {
                    using (var f = new fixture())
                    {
                        var settings = f.add("ModularAvatarMeshSettings"); var bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
                        edit(settings, serialized => { reference(serialized, "ProbeAnchor", f.body, "Body"); reference(serialized, "RootBone", null, "Body/Hips"); serialized.FindProperty("InheritBounds").intValue = 1; serialized.FindProperty("Bounds").boundsValue = bounds; });
                        f.scan(); f.copy(); var copied = f.copied("ModularAvatarMeshSettings"); empty_reference(copied, "ProbeAnchor"); empty_reference(copied, "RootBone");
                        using (var serialized = new SerializedObject(copied)) require(serialized.FindProperty("InheritBounds").intValue == 1 && serialized.FindProperty("Bounds").boundsValue == bounds, "Mesh settings lost");
                        using (var serialized = new SerializedObject(settings)) require(serialized.FindProperty("ProbeAnchor.referencePath").stringValue == "Body", "Source reference changed");
                    }
                });
                check("Local clothing references remap to the destination while external references clear", () =>
                {
                    foreach (var path_only in new[] { false, true }) using (var f = new fixture())
                    {
                        var settings = f.add("ModularAvatarMeshSettings");
                        edit(settings, serialized => { reference(serialized, "ProbeAnchor", path_only ? null : f.source_helper, "Standard_Maid_Lime/St_Maid_Lime_Lowfar/local_anchor"); reference(serialized, "RootBone", f.body, "Body"); });
                        f.scan(); f.copy(); var copied = f.copied("ModularAvatarMeshSettings"); empty_reference(copied, "RootBone");
                        using (var serialized = new SerializedObject(copied)) require(serialized.FindProperty("ProbeAnchor.targetObject").objectReferenceValue == f.target_helper
                            && serialized.FindProperty("ProbeAnchor.referencePath").stringValue == "Standard_Maid_Rurune/St_Maid_Rurune_Lowfar/local_anchor", "Clothing reference or path not remapped");
                    }
                });
                check("Bone Proxy clears humanoid bone and subpath, keeping attachment mode and scale option", () =>
                {
                    using (var f = new fixture())
                    {
                        var proxy = f.add("ModularAvatarBoneProxy");
                        edit(proxy, serialized => { serialized.FindProperty("boneReference").intValue = (int)HumanBodyBones.Hips; serialized.FindProperty("subPath").stringValue = "extra_bone"; serialized.FindProperty("attachmentMode").intValue = 2; serialized.FindProperty("matchScale").boolValue = true; });
                        f.scan(); f.copy(); var copied = f.copied("ModularAvatarBoneProxy");
                        using (var serialized = new SerializedObject(copied)) require(serialized.FindProperty("boneReference").intValue == (int)HumanBodyBones.LastBone && serialized.FindProperty("subPath").stringValue == "" && serialized.FindProperty("attachmentMode").intValue == 2 && serialized.FindProperty("matchScale").boolValue, "Bone proxy state incorrect");
                    }
                });
                check("Armature components clear avatar targets and direct bone references while preserving unrelated settings", () =>
                {
                    using (var f = new fixture())
                    {
                        var source_armature = child(f.source, "outfit_armature"); var target_armature = child(f.target, "outfit_armature");
                        var merger = f.add("ModularAvatarMergeArmature", source_armature);
                        edit(merger, serialized => { reference(serialized, "mergeTarget", f.body, "Body"); serialized.FindProperty("prefix").stringValue = "outfit_"; serialized.FindProperty("suffix").stringValue = "_test"; });
                        var outfit = f.add("ModularAvatarOutfitRoot", f.source); edit(outfit, serialized => serialized.FindProperty("armatureRoot").objectReferenceValue = f.body.transform);
                        f.scan(); f.copy(); var copied = target_armature.GetComponent(merger.GetType()); empty_reference(copied, "mergeTarget");
                        using (var serialized = new SerializedObject(copied)) require(serialized.FindProperty("prefix").stringValue == "outfit_" && serialized.FindProperty("suffix").stringValue == "_test", "Armature settings lost");
                        using (var serialized = new SerializedObject(f.target.GetComponent(outfit.GetType()))) require(serialized.FindProperty("armatureRoot").objectReferenceValue == null, "Direct body bone reference remains");
                    }
                });
                check("Unresolved path and avatar-root references clear, and animator settings and assets survive", () =>
                {
                    using (var f = new fixture())
                    {
                        var animator = f.add("ModularAvatarMergeAnimator"); var controller = new UnityEditor.Animations.AnimatorController();
                        try
                        {
                            edit(animator, serialized => { reference(serialized, "relativePathRoot", null, "missing_body"); serialized.FindProperty("layerPriority").intValue = 23; serialized.FindProperty("animator").objectReferenceValue = controller; });
                            var settings = f.add("ModularAvatarMeshSettings"); edit(settings, serialized => reference(serialized, "RootBone", null, "$$$AVATAR_ROOT$$$"));
                            f.scan(); f.copy(); var copied = f.copied("ModularAvatarMergeAnimator"); empty_reference(copied, "relativePathRoot"); empty_reference(f.copied("ModularAvatarMeshSettings"), "RootBone");
                            using (var serialized = new SerializedObject(copied)) require(serialized.FindProperty("layerPriority").intValue == 23 && serialized.FindProperty("animator").objectReferenceValue == controller, "Animator settings lost");
                        }
                        finally { Object.DestroyImmediate(controller); }
                    }
                });
                check("Opt-out and missing mesh pairs retain the previous copy behavior", () =>
                {
                    foreach (var paired in new[] { true, false }) using (var f = new fixture(paired))
                    {
                        add_sync(f); if (paired) set(f.window, "clear_ma_avatar_references", false); f.scan(); f.copy();
                        var copied = paired ? f.copied("ModularAvatarBlendshapeSync") : f.target.GetComponentInChildren(sync_type);
                        require(copied != null && binding_count(copied) == 1, "Legacy bindings reset");
                    }
                });
                check("Updating resets existing bindings; repeated copy avoids duplicates; skip and exclusion preserve settings; Undo", () =>
                {
                    using (var f = new fixture())
                    {
                        var sync = add_sync(f); var existing = add_sync(f, f.target_mesh);
                        set(f.window, "copy_mode", D9speed_BaseEditorUtils.ComponentCopyMode.SkipExisting); f.scan(); f.copy(); require(binding_count(existing) == 1, "Skip reset target");
                        set(f.window, "copy_mode", D9speed_BaseEditorUtils.ComponentCopyMode.UpdateOrAdd);
                        ((HashSet<Component>)get(f.window, "excluded_components")).Add(sync); f.scan(); call(f.window, "ExecuteCopy"); require(binding_count(existing) == 1, "Excluded sync changed");
                        ((HashSet<Component>)get(f.window, "excluded_components")).Clear(); f.scan(); f.copy(); require(binding_count(existing) == 0, "Update did not reset");
                        Undo.PerformUndo(); require(binding_count(existing) == 1, "Undo did not restore old bindings");
                        f.scan(); f.copy(); f.scan(); f.copy(); require(f.target_mesh.GetComponents(sync_type).Length == 1, "Repeated copy duplicated sync");
                    }
                });
                check("Cleared sync stops live editor shape synchronization immediately", () =>
                {
                    var local_mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
                    var remote_mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
                    local_mesh.AddBlendShapeFrame("clothing_shape", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
                    remote_mesh.AddBlendShapeFrame("source_body_shape", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
                    try
                    {
                        using (var f = new fixture())
                        {
                            f.source_mesh.GetComponent<SkinnedMeshRenderer>().sharedMesh = local_mesh;
                            var local_renderer = f.target_mesh.GetComponent<SkinnedMeshRenderer>(); local_renderer.sharedMesh = local_mesh;
                            f.body.AddComponent<SkinnedMeshRenderer>().sharedMesh = remote_mesh;
                            var target_body = child(f.target_avatar, "Body"); var remote_renderer = target_body.AddComponent<SkinnedMeshRenderer>(); remote_renderer.sharedMesh = remote_mesh; remote_renderer.SetBlendShapeWeight(0, 80);
                            add_sync(f); var existing = add_sync(f, f.target_mesh);
                            edit(existing, serialized => reference(serialized, "Bindings.Array.data[0].ReferenceMesh", target_body, "Body"));
                            call(existing, "Rebind"); require(((IList)get(existing, "_editorBindings")).Count == 1, "Live binding fixture failed");
                            f.scan(); f.copy(); require(((IList)get(existing, "_editorBindings")).Count == 0, "Editor binding cache remains");
                            local_renderer.SetBlendShapeWeight(0, 17); remote_renderer.SetBlendShapeWeight(0, 95); call(existing, "EditorUpdate");
                            require(Mathf.Approximately(local_renderer.GetBlendShapeWeight(0), 17), "Old body still drives copied mesh");
                        }
                    }
                    finally { Object.DestroyImmediate(local_mesh); Object.DestroyImmediate(remote_mesh); }
                });
                check("Serialized copied settings contain neither body references nor old reference paths", () =>
                {
                    using (var f = new fixture())
                    {
                        var settings = f.add("ModularAvatarMeshSettings"); edit(settings, serialized => reference(serialized, "ProbeAnchor", f.body, "Body"));
                        add_sync(f); f.scan(); f.copy();
                        var copy = f.copied("ModularAvatarMeshSettings"); var json = EditorJsonUtility.ToJson(copy);
                        var restored = child(f.target, "restored_settings").AddComponent(copy.GetType()); EditorJsonUtility.FromJsonOverwrite(json, restored); empty_reference(restored, "ProbeAnchor");
                        require(!json.Contains("\"referencePath\":\"Body\""), "Serialized body path remains");
                    }
                });
            }
            results.passed = results.checks.All(r => r.passed); Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/humanoid_alias_ma_results.json", JsonUtility.ToJson(results, true));
            Debug.Log("MA copy checks: " + results.checks.Count(r => r.passed) + "/" + results.checks.Count);
            EditorApplication.Exit(results.passed ? 0 : 1);
        }
    }
}
