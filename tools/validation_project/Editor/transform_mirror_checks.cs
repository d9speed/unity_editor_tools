using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class TransformMirrorChecks
    {
        [Serializable] private sealed class Report
        {
            public string unity;
            public bool vrc;
            public List<string> passed = new();
            public List<string> failed = new();
        }
        private static readonly Report report = new();
        private static Scene scene;
        private static readonly List<Action> delayed_checks = new();
        private static int frames;

        public static void Run()
        {
            report.unity = Application.unityVersion;
            report.vrc = vrc_type("VRCParentConstraint") != null;
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            check("Blender suffixes and numbered suffixes", () =>
            {
                foreach (var pair in new[] { ("Hand_L", "Hand_R"), ("Hand.R", "Hand.L"), ("Hand_l.001", "Hand_r.001"), ("Hand.r", "Hand.l"), ("Center", "Center"), ("Left", "Left") })
                    require(TransformMirrorPlan.opposite_name(pair.Item1) == pair.Item2, pair.Item1);
            });
            check("Hierarchy DFS, selected-root deduplication, world poses and grouped Undo/Redo", () =>
            {
                var root = go("hierarchy", null);
                root.transform.SetPositionAndRotation(new Vector3(1, 2, 0), Quaternion.Euler(0, 20, 0));
                root.transform.localScale = Vector3.one * 2;
                var source = go("chain_L", root);
                source.transform.localPosition = new Vector3(2, 3, 4);
                source.transform.localRotation = Quaternion.Euler(15, 25, 35);
                var child = go("tip.L", source);
                child.transform.localPosition = new Vector3(1, 2, 3);
                var grandchild = go("tip_end", child);
                grandchild.transform.localPosition = Vector3.up;
                var plan = TransformMirrorPlan.build(new[] { source, child, source });
                require(plan.creations.Count == 1 && plan.planned_paths.Count == 3, "Deduplication");
                require(plan.pairs.Take(3).Select(p => p.source.name).SequenceEqual(new[] { "chain_L", "tip.L", "tip_end" }), "DFS order");
                var pivot = new Vector3(3, 0, 0);
                var clone = TransformMirrorComponents.execute(plan, pivot, true, true).Single();
                var clone_child = clone.transform.GetChild(0);
                near(clone.transform.position, TransformMirrorComponents.reflect_point(source.transform.position, pivot), "Root pose");
                near(clone_child.position, TransformMirrorComponents.reflect_point(child.transform.position, pivot), "Child pose");
                angle(clone_child.rotation, TransformMirrorComponents.reflect_rotation(child.transform.rotation), "Child rotation");
                require(clone_child.name == "tip.R" && clone.transform.parent == root.transform, "Names/parent");
                Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                require(clone == null && source != null && child != null, "Undo source preservation");
                Undo.PerformRedo();
                require(root.transform.Find("chain_R") != null, "Redo clone");
                near(root.transform.Find("chain_R/tip.R").position, TransformMirrorComponents.reflect_point(child.transform.position, pivot), "Redo pose");
            });
            check("External sources, common references, nulls, duplicate names and parent matching", () =>
            {
                var root = go("reference_scope", null);
                var left = go("hand_L", root); left.transform.position = new Vector3(2, 1, 0);
                var right = go("hand_R", root); right.transform.position = new Vector3(-2, 1, 0);
                var source = go("charm_L", left); source.transform.localPosition = new Vector3(1, 2, 3);
                var original = source.AddComponent<ParentConstraint>();
                original.AddSource(new ConstraintSource { sourceTransform = left.transform, weight = 1 });
                original.AddSource(new ConstraintSource { sourceTransform = null, weight = 0 });
                original.SetTranslationOffset(0, new Vector3(1, 2, 3));
                original.SetRotationOffset(0, new Vector3(10, 20, 30));
                original.locked = true; original.constraintActive = true;
                var source_json = EditorJsonUtility.ToJson(original);
                var plan = TransformMirrorPlan.build(new[] { source });
                require(plan.creations.Single().parent == right.transform, "Mirrored parent");
                require(plan.pairs.Any(p => p.destination == right.transform), "Preview of external source");
                var copy = TransformMirrorComponents.execute(plan, Vector3.zero, true, true).Single().GetComponent<ParentConstraint>();
                require(copy.GetSource(0).sourceTransform == right.transform && copy.GetSource(1).sourceTransform == null, "Sources/nulls");
                near(copy.GetTranslationOffset(0), new Vector3(-1, 2, 3), "Parent translation offset");
                angle(Quaternion.Euler(copy.GetRotationOffset(0)), TransformMirrorComponents.reflect_rotation(Quaternion.Euler(10, 20, 30)), "Parent rotation offset");
                require(copy.locked && copy.constraintActive && EditorJsonUtility.ToJson(original) == source_json, "Flags/source unchanged");
                delayed_checks.Add(() => near(copy.transform.position, TransformMirrorComponents.reflect_point(source.transform.position, Vector3.zero), "Unity constraint after Editor evaluation"));
                var ambiguous_root = go("ambiguous", null);
                var reference = go("bone_L", ambiguous_root);
                go("bone_R", go("a", ambiguous_root));
                go("bone_R", go("b", ambiguous_root));
                var obj = go("obj_L", ambiguous_root);
                obj.AddComponent<PositionConstraint>().AddSource(new ConstraintSource { sourceTransform = reference.transform, weight = 1 });
                var ambiguous = TransformMirrorPlan.build(new[] { obj });
                require(ambiguous.pairs.Any(p => p.warning && p.status.Contains("複数")), "Ambiguity warning");
                var clone = TransformMirrorComponents.execute(ambiguous, Vector3.zero, true, true).Single();
                require(clone.GetComponent<PositionConstraint>().GetSource(0).sourceTransform == reference.transform, "Ambiguous reference unchanged");
                reference.transform.position = new Vector3(4, 1, 0);
                var constrained = obj.GetComponent<PositionConstraint>();
                constrained.translationOffset = new Vector3(2, 3, 0);
                obj.transform.position = new Vector3(6, 4, 0);
                constrained.locked = true; constrained.constraintActive = true;
                var compensated = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { obj }), Vector3.zero, true, true).Single().GetComponent<PositionConstraint>();
                near(compensated.translationOffset, new Vector3(-10, 3, 0), "Unmirrored source offset compensation");
                delayed_checks.Add(() => near(compensated.transform.position, new Vector3(-6, 4, 0), "Unmirrored reference does not snap back"));
            });
            check("Cross-selection references point to new pairs and existing names stay intact", () =>
            {
                var root = go("cross_pairs", null);
                var a = go("a_L", root);
                var b = go("b.L", root);
                var existing = go("a_R", root);
                existing.transform.position = Vector3.one * 9;
                a.AddComponent<PositionConstraint>().AddSource(new ConstraintSource { sourceTransform = b.transform, weight = 0.25f });
                b.AddComponent<RotationConstraint>().AddSource(new ConstraintSource { sourceTransform = a.transform, weight = 0.75f });
                var plan = TransformMirrorPlan.build(new[] { a, b });
                require(plan.creations[0].name == "a_R_mirror_1", "Collision preview");
                var copies = TransformMirrorComponents.execute(plan, Vector3.zero, true, true);
                require(copies[0].GetComponent<PositionConstraint>().GetSource(0).sourceTransform == copies[1].transform, "Forward cross-ref");
                require(copies[1].GetComponent<RotationConstraint>().GetSource(0).sourceTransform == copies[0].transform, "Reverse cross-ref");
                near(existing.transform.position, Vector3.one * 9, "Existing counterpart");
            });
            check("Collider centers and dimensions; rotation-off", () =>
            {
                var source = go("collider_L", null);
                source.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(20, 30, 40));
                var box = source.AddComponent<BoxCollider>(); box.center = new Vector3(1, 2, 3); box.size = new Vector3(2, 4, 6); box.isTrigger = true;
                var sphere = source.AddComponent<SphereCollider>(); sphere.center = Vector3.one; sphere.radius = 0.3f;
                var capsule = source.AddComponent<CapsuleCollider>(); capsule.center = Vector3.right; capsule.direction = 2; capsule.height = 3;
                var clone = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { source }), Vector3.zero, true, true).Single();
                near(clone.GetComponent<BoxCollider>().center, new Vector3(-1, 2, 3), "Box center");
                require(clone.GetComponent<BoxCollider>().size == box.size && clone.GetComponent<BoxCollider>().isTrigger, "Box shape");
                require(clone.GetComponent<SphereCollider>().radius == sphere.radius && clone.GetComponent<CapsuleCollider>().direction == 2, "Sphere/capsule shape");
                var no_rotation = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { source }), Vector3.zero, false, true).Single();
                angle(no_rotation.transform.rotation, source.transform.rotation, "Rotation toggle");
            });
            check("Nearest nested Prefab root and scene overrides survive duplication", () =>
            {
                var asset_path = "Assets/transform_mirror_probe_" + Guid.NewGuid().ToString("N") + ".prefab";
                var template = go("prefab_scope", null);
                var left = go("bone_L", template); var right = go("bone_R", template);
                var source = go("item_L", template);
                source.AddComponent<PositionConstraint>().AddSource(new ConstraintSource { sourceTransform = left.transform, weight = 1 });
                var box = source.AddComponent<BoxCollider>();
                go("removed_child", template);
                source.AddComponent<SphereCollider>();
                try
                {
                    var asset = PrefabUtility.SaveAsPrefabAsset(template, asset_path);
                    Object.DestroyImmediate(template);
                    var outside = go("outer", null);
                    go("bone_R", outside);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, outside.transform);
                    var instance_source = instance.transform.Find("item_L").gameObject;
                    Object.DestroyImmediate(instance.transform.Find("removed_child").gameObject);
                    Object.DestroyImmediate(instance_source.GetComponent<SphereCollider>());
                    go("added_child_L", instance_source);
                    instance_source.GetComponent<BoxCollider>().center = new Vector3(7, 2, 3);
                    var plan = TransformMirrorPlan.build(new[] { instance_source });
                    require(plan.creations.Single().search_root == instance.transform, "Nearest prefab scope");
                    var clone = TransformMirrorComponents.execute(plan, Vector3.zero, true, true).Single();
                    require(clone.GetComponent<PositionConstraint>().GetSource(0).sourceTransform == instance.transform.Find("bone_R"), "No outer-scope leakage");
                    near(clone.GetComponent<BoxCollider>().center, new Vector3(-7, 2, 3), "Component override");
                    var asset_copy = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { asset }), Vector3.zero, true, true).Single();
                    require(PrefabUtility.IsPartOfPrefabInstance(asset_copy), "Prefab asset connection");
                    require(asset_copy.transform.GetChild(0).name == "bone_R", "Prefab child name");
                    var instance_copy = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { instance }), Vector3.zero, true, true).Single();
                    require(PrefabUtility.IsPartOfPrefabInstance(instance_copy), "Scene prefab connection");
                    require(instance_copy.transform.Find("removed_child") == null, "Removed prefab child stays removed");
                    require(instance_copy.transform.Find("item_R/added_child_R") != null, "Added prefab child survives");
                    require(instance_copy.transform.Find("item_R").GetComponent<SphereCollider>() == null, "Removed prefab component stays removed");
                }
                finally { if (template != null) Object.DestroyImmediate(template); AssetDatabase.DeleteAsset(asset_path); }
            });
            if (report.vrc)
            {
                check("VRC locked parent offsets, source overflow and deferred evaluation", () =>
                {
                    var root = go("vrc_scope", null);
                    var left = go("source_L", root); left.transform.position = new Vector3(2, 1, 0);
                    var right = go("source_R", root); right.transform.position = new Vector3(-2, 1, 0);
                    var source = go("locked_L", root); source.transform.position = new Vector3(3, 3, 3);
                    source.transform.rotation = Quaternion.Euler(10, 20, 30);
                    var constraint = source.AddComponent(vrc_type("VRCParentConstraint"));
                    var sources = (IList)get(constraint, "Sources");
                    sources.Clear();
                    var source_type = sources.GetType().GetProperty("Item").PropertyType;
                    for (var i = 0; i < 18; i++)
                    {
                        var item = Activator.CreateInstance(source_type, left.transform, i == 0 ? 1f : 0f);
                        set(item, "ParentPositionOffset", new Vector3(1, 2, 3));
                        set(item, "ParentRotationOffset", new Vector3(10, 20, 30));
                        sources.Add(item);
                    }
                    set(constraint, "Sources", sources);
                    set(constraint, "IsActive", true); set(constraint, "Locked", true);
                    var original_json = EditorJsonUtility.ToJson(constraint);
                    var plan = TransformMirrorPlan.build(new[] { source });
                    var clone = TransformMirrorComponents.execute(plan, Vector3.zero, true, true).Single();
                    var copy = clone.GetComponent(constraint.GetType());
                    var copied = (IList)get(copy, "Sources");
                    require(copied.Count == 18, "Overflow count: source=" + sources.Count + ", copied=" + copied.Count);
                    require((Transform)get(copied[17], "SourceTransform") == right.transform, "Overflow reference");
                    near((Vector3)get(copied[0], "ParentPositionOffset"), new Vector3(-1, 2, 3), "Locked position offset");
                    angle(Quaternion.Euler((Vector3)get(copied[0], "ParentRotationOffset")), TransformMirrorComponents.reflect_rotation(Quaternion.Euler(10, 20, 30)), "Locked rotation offset");
                    require((bool)get(copy, "Locked") && (bool)get(copy, "IsActive"), "Lock/activity flags");
                    require(EditorJsonUtility.ToJson(constraint) == original_json, "Original VRC constraint unchanged");
                    delayed_checks.Add(() =>
                    {
                        near(clone.transform.position, new Vector3(-3, 3, 3), "VRC next-frame position");
                        angle(clone.transform.rotation, TransformMirrorComponents.reflect_rotation(source.transform.rotation), "VRC next-frame rotation");
                        near((Vector3)get(((IList)get(copy, "Sources"))[0], "ParentPositionOffset"), new Vector3(-1, 2, 3), "Offset must not rebake");
                    });
                });
                check("PhysBone roots, ignores, collider references, endpoint and collider pose", () =>
                {
                    var root = go("phys_scope", null);
                    var source = go("physics_L", root); source.transform.position = new Vector3(2, 1, 0);
                    var bone = go("bone.L", source);
                    var ignored = go("ignore_L", bone);
                    var internal_collider = go("inside_L", source).AddComponent(vrc_type("VRCPhysBoneCollider"));
                    set(internal_collider, "position", new Vector3(1, 2, 3));
                    set(internal_collider, "rotation", Quaternion.Euler(10, 20, 30));
                    var external_left = go("collider_L", root).AddComponent(vrc_type("VRCPhysBoneCollider"));
                    var external_right = go("collider_R", root).AddComponent(vrc_type("VRCPhysBoneCollider"));
                    var physbone = source.AddComponent(vrc_type("VRCPhysBone"));
                    set(physbone, "rootTransform", bone.transform);
                    ((IList)get(physbone, "ignoreTransforms")).Add(ignored.transform);
                    ((IList)get(physbone, "colliders")).Add(internal_collider);
                    ((IList)get(physbone, "colliders")).Add(external_left);
                    ((IList)get(physbone, "colliders")).Add(null);
                    set(physbone, "endpointPosition", new Vector3(1, 2, 3));
                    set(physbone, "limitRotation", new Vector3(10, 20, 30));
                    var json = EditorJsonUtility.ToJson(physbone);
                    var clone = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { source }), Vector3.zero, true, true).Single();
                    var copy = clone.GetComponent(physbone.GetType());
                    require((Transform)get(copy, "rootTransform") == clone.transform.Find("bone.R"), "Root reference");
                    require(((IList)get(copy, "ignoreTransforms"))[0] as Transform == clone.transform.Find("bone.R/ignore_R"), "Ignored reference");
                    var collider_copy = clone.transform.Find("inside_R").GetComponent(internal_collider.GetType());
                    var colliders = (IList)get(copy, "colliders");
                    require(ReferenceEquals(colliders[0], collider_copy) && ReferenceEquals(colliders[1], external_right) && colliders[2] == null, "Collider references");
                    near((Vector3)get(copy, "endpointPosition"), new Vector3(-1, 2, 3), "Endpoint");
                    near((Vector3)get(copy, "limitRotation"), new Vector3(10, -20, -30), "Limits");
                    near((Vector3)get(collider_copy, "position"), new Vector3(-1, 2, 3), "Collider center");
                    angle((Quaternion)get(collider_copy, "rotation"), TransformMirrorComponents.reflect_rotation(Quaternion.Euler(10, 20, 30)), "Collider rotation");
                    require(EditorJsonUtility.ToJson(physbone) == json, "PhysBone source unchanged");
                });
                check("External VRC target cannot drive original scene objects", () =>
                {
                    var root = go("external_target_scope", null);
                    var target = go("target_L", root);
                    var source = go("driver_L", root);
                    var constraint = source.AddComponent(vrc_type("VRCPositionConstraint"));
                    set(constraint, "TargetTransform", target.transform);
                    set(constraint, "IsActive", true); set(constraint, "Locked", true);
                    var plan = TransformMirrorPlan.build(new[] { source });
                    require(plan.warnings.Any(w => w.Contains("TargetTransform")), "External-target warning");
                    var copy = TransformMirrorComponents.execute(plan, Vector3.zero, true, true).Single().GetComponent(constraint.GetType());
                    require(!((Behaviour)copy).enabled && !(bool)get(copy, "IsActive"), "External target guarded");
                });
                check("VRC local-space locked offsets and freeze/disabled flags", () =>
                {
                    var root = go("local_space", null); root.transform.position = new Vector3(5, 0, 0);
                    var left_parent = go("source_parent_L", root);
                    left_parent.transform.localPosition = new Vector3(3, 0, 0);
                    var right_parent = go("source_parent_R", root);
                    right_parent.transform.localPosition = new Vector3(-1, 0, 0);
                    var left = go("bone_L", left_parent); left.transform.localPosition = new Vector3(2, 0, 0);
                    var right = go("bone_R", right_parent); right.transform.localPosition = new Vector3(-2, 0, 0);
                    foreach (var type in new[] { "VRCParentConstraint", "VRCPositionConstraint" })
                    {
                        var source = go(type + "_L", root);
                        source.transform.localPosition = new Vector3(3, 2, 0);
                        var constraint = source.AddComponent(vrc_type(type));
                        var sources = (IList)get(constraint, "Sources"); sources.Clear();
                        var source_type = sources.GetType().GetProperty("Item").PropertyType;
                        var item = Activator.CreateInstance(source_type, left.transform, 1f);
                        set(item, "ParentPositionOffset", new Vector3(1, 2, 0));
                        sources.Add(item); set(constraint, "Sources", sources);
                        if (type == "VRCPositionConstraint") set(constraint, "PositionOffset", new Vector3(1, 2, 0));
                        set(constraint, "SolveInLocalSpace", true); set(constraint, "Locked", true); set(constraint, "IsActive", true);
                        set(constraint, "FreezeToWorld", true); ((Behaviour)constraint).enabled = false;
                        var clone = TransformMirrorComponents.execute(TransformMirrorPlan.build(new[] { source }), Vector3.zero, true, true).Single();
                        var copy = clone.GetComponent(constraint.GetType());
                        var offset = type == "VRCPositionConstraint" ? (Vector3)get(copy, "PositionOffset") :
                            (Vector3)get(((IList)get(copy, "Sources"))[0], "ParentPositionOffset");
                        near(offset, new Vector3(-11, 2, 0), "Local-space offset in target parent basis");
                        require((bool)get(copy, "Locked") && (bool)get(copy, "FreezeToWorld") && !((Behaviour)copy).enabled, "Disabled/frozen state");
                    }
                });
            }
            EditorApplication.update += finish_after_evaluation;
        }

        private static void finish_after_evaluation()
        {
            if (++frames < 30) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            EditorApplication.update -= finish_after_evaluation;
            for (var i = 0; i < delayed_checks.Count; i++) check("Deferred constraint evaluation " + i, delayed_checks[i]);
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            Directory.CreateDirectory("Logs/transform_mirror");
            File.WriteAllText("Logs/transform_mirror/checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.failed.Count == 0 ? 0 : 1);
        }

        private static GameObject go(string name, GameObject parent)
        {
            var value = new GameObject(name);
            if (parent != null) value.transform.SetParent(parent.transform, false);
            else SceneManager.MoveGameObjectToScene(value, scene);
            return value;
        }
        private static Type vrc_type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics." + (name.Contains("Constraint") ? "Constraint" : "PhysBone") + ".Components." + name)).FirstOrDefault(t => t != null);
        private static object get(object target, string field) => target.GetType().GetField(field).GetValue(target);
        private static void set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
        private static void check(string name, Action action)
        {
            try { action(); report.passed.Add(name); }
            catch (Exception error) { report.failed.Add(name + ": " + error); Debug.LogError(name + ": " + error); }
        }
        private static void require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void near(Vector3 actual, Vector3 expected, string message) => require(Vector3.Distance(actual, expected) < 0.002f, message + ": " + actual + " != " + expected);
        private static void angle(Quaternion actual, Quaternion expected, string message) => require(Quaternion.Angle(actual, expected) < 0.05f, message + ": " + actual.eulerAngles + " != " + expected.eulerAngles);
    }
}
