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
using Object = UnityEngine.Object;

namespace D9speed.HumanoidAliasValidation
{
    public static class humanoid_alias_copy_checks
    {
        private const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private class check_result { public string name; public bool passed; public string detail; }
        [Serializable] private class validation_report { public string unity; public bool passed; public List<check_result> checks = new List<check_result>(); }
        private static readonly validation_report report = new validation_report();
        private static object get(object obj, string field) => obj.GetType().GetField(field, flags).GetValue(obj);
        private static object call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, flags).Invoke(obj, args);
        private static void copy_scope(object window, string group) => call(window, "execute_group_copy",
            Enum.Parse(window.GetType().GetNestedType("copy_group", flags), group));
        private static void require(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        private static GameObject child(GameObject parent, string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            return obj;
        }
        private static void check(string name, Action action)
        {
            try { action(); report.checks.Add(new check_result { name = name, passed = true, detail = "ok" }); }
            catch (Exception error) { report.checks.Add(new check_result { name = name, passed = false, detail = error.ToString() }); Debug.LogError(name + ": " + error); }
        }

        private sealed class fixture : IDisposable
        {
            public readonly GameObject source = new GameObject("Standard_Maid_Lime");
            public readonly GameObject target = new GameObject("Standard_Maid_Rurune");
            public readonly HumanoidAliasComponentCopierWindow window;
            public readonly List<Transform> source_legs = new List<Transform>();
            public readonly List<Transform> target_legs = new List<Transform>();
            public readonly List<GameObject> helpers = new List<GameObject>();
            public readonly List<Object> extra = new List<Object>();
            public fixture(bool skin = true, bool colliders = true)
            {
                add_legs(source, "StMaid_Lime_Armature", false, source_legs);
                add_legs(target, "StMaid_Rurune_Armature", true, target_legs);
                if (skin) { add_skin(source, source_legs); add_skin(target, target_legs); }
                if (colliders)
                {
                    var group = child(source, "Cloth_Collider");
                    foreach (var leg in source_legs)
                    {
                        var helper = child(group, leg.name.Replace('.', '_'));
                        helper.AddComponent<SphereCollider>().radius = 0.23f;
                        var constraint = helper.AddComponent<ParentConstraint>();
                        constraint.AddSource(new ConstraintSource { sourceTransform = leg, weight = 1 });
                        constraint.weight = 0.6f;
                        helpers.Add(helper);
                    }
                }
                window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
                ((ObjectField)get(window, "sourceField")).SetValueWithoutNotify(source);
                ((ObjectField)get(window, "targetField")).SetValueWithoutNotify(target);
                window.GetType().GetField("copySkinnedMeshMaterials", flags).SetValue(window, false);
            }
            public void scan() => call(window, "Scan");
            public void copy(string group = "other")
            {
                copy_scope(window, group);
                var warnings = (List<string>)get(window, "warnings");
                require(warnings.Any(w => w.StartsWith("コピー完了:")), string.Join(";", warnings));
            }
            public void Dispose()
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
                foreach (var obj in extra) if (obj != null) Object.DestroyImmediate(obj);
                Undo.ClearAll();
            }
        }

        private static void add_legs(GameObject root, string rig_name, bool rurune, List<Transform> bones)
        {
            var hips = child(child(root, rig_name), "Hips");
            foreach (var side in new[] { "L", "R" })
            {
                var upper = child(hips, rurune ? "Upperleg_" + side : "UpperLeg." + side);
                var lower = child(upper, rurune ? "Lowerleg_" + side : "LowerLeg." + side);
                var foot = child(lower, rurune ? "Foot_" + side : "Foot." + side);
                bones.AddRange(new[] { upper.transform, lower.transform, foot.transform });
            }
        }
        private static SkinnedMeshRenderer add_skin(GameObject root, IEnumerable<Transform> bones)
        {
            var renderer = child(root, "cloth_mesh").AddComponent<SkinnedMeshRenderer>();
            renderer.bones = bones.ToArray();
            renderer.rootBone = renderer.bones[0].parent;
            return renderer;
        }
        private static Transform resolved(fixture f, string field, string key)
        {
            var map = (IDictionary)get(get(f.window, field), "ResolvedByKey");
            return map.Contains(key) ? (Transform)get(map[key], "Transform") : null;
        }
        private static void verify_copy(fixture f)
        {
            for (var i = 0; i < f.helpers.Count; i++)
            {
                var copied = f.target.transform.Find("Cloth_Collider/" + f.helpers[i].name);
                require(copied != null, "Missing helper hierarchy: " + f.helpers[i].name);
                require(copied.GetComponents<SphereCollider>().Length == 1, "Collider missing or duplicated");
                require(Mathf.Approximately(copied.GetComponent<SphereCollider>().radius, 0.23f), "Collider values changed");
                var constraint = copied.GetComponent<ParentConstraint>();
                require(constraint != null && constraint.GetSource(0).sourceTransform == f.target_legs[i], "Constraint bone reference not remapped");
                require(Mathf.Approximately(constraint.weight, 0.6f), "Constraint values changed");
                require(f.target_legs[i].GetComponent<Collider>() == null, "Helper was merged into a real bone");
            }
        }

        public static void run()
        {
            report.unity = Application.unityVersion;
            check("Lime-style six collider name collisions resolve to skin bones", () =>
            {
                using (var f = new fixture())
                {
                    f.scan();
                    var keys = new[] { "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                    for (var i = 0; i < keys.Length; i++)
                    {
                        require(resolved(f, "sourceScan", keys[i]) == f.source_legs[i], "Wrong source bone: " + keys[i]);
                        require(resolved(f, "targetScan", keys[i]) == f.target_legs[i], "Wrong target bone: " + keys[i]);
                    }
                    require(!((IEnumerable)get(get(f.window, "sourceScan"), "AmbiguousKeys")).Cast<string>().Any(), "False ambiguity");
                    require(resolved(f, "sourceScan", "Hips") == f.source_legs[0].parent, "Unweighted ancestor not recognized");
                    f.copy(); f.copy("constraint"); verify_copy(f);
                }
            });
            check("Repeated copy updates existing helper hierarchy without duplicates", () =>
            {
                using (var f = new fixture())
                {
                    f.scan(); f.copy(); f.copy("constraint"); f.scan(); f.copy(); f.copy("constraint"); verify_copy(f);
                    require(f.target.GetComponentsInChildren<SphereCollider>().Length == 6, "Repeated copy duplicated helpers");
                }
            });
            check("Each category copy can be undone as one operation", () =>
            {
                using (var f = new fixture())
                {
                    f.scan(); f.copy(); Undo.PerformUndo();
                    require(f.target.transform.Find("Cloth_Collider") == null, "Undo left copied hierarchy");
                    require(f.target_legs.All(t => t != null && t.GetComponents<Component>().Length == 1), "Undo changed real bones");
                }
            });
            check("Name-only rigs retain legacy alias matching and reference remapping", () =>
            {
                using (var f = new fixture(false, false))
                {
                    var constraint = f.source_legs[0].gameObject.AddComponent<ParentConstraint>();
                    constraint.AddSource(new ConstraintSource { sourceTransform = f.source_legs[1], weight = 1 });
                    f.scan(); f.copy("constraint");
                    require(f.target_legs[0].GetComponent<ParentConstraint>().GetSource(0).sourceTransform == f.target_legs[1], "Legacy mapping failed");
                }
            });
            check("Two real skinned rigs remain ambiguous and block copying", () =>
            {
                using (var f = new fixture(true, false))
                {
                    var second = new List<Transform>(); add_legs(f.source, "second_rig", false, second); add_skin(f.source, second);
                    f.source_legs[0].gameObject.AddComponent<SphereCollider>(); f.scan(); copy_scope(f.window, "other");
                    require(((IEnumerable)get(get(f.window, "sourceScan"), "AmbiguousKeys")).Cast<string>().Contains("LeftUpperLeg"), "Two real rigs were silently resolved");
                    require(f.target.GetComponentsInChildren<SphereCollider>().Length == 0, "Ambiguous copy changed target");
                }
            });
            check("Manual mapping can resolve a real bone ambiguity", () =>
            {
                using (var f = new fixture(true, false))
                {
                    var second = new List<Transform>(); add_legs(f.source, "second_rig", false, second); add_skin(f.source, second);
                    f.source_legs[0].gameObject.AddComponent<SphereCollider>(); f.scan();
                    ((Dictionary<Transform, Transform>)get(f.window, "manual_targets"))[f.source_legs[0]] = f.target_legs[0];
                    f.scan(); f.copy(); require(f.target_legs[0].GetComponent<SphereCollider>() != null, "Manual mapping was ignored");
                }
            });
            check("Duplicate auxiliary leaf names use their full hierarchy paths", () =>
            {
                using (var f = new fixture(true, false))
                {
                    foreach (var name in new[] { "left_group", "right_group" })
                    {
                        child(child(f.source, name), "helper").AddComponent<SphereCollider>();
                        child(child(f.target, name), "helper");
                    }
                    f.scan(); f.copy();
                    require(f.target.GetComponentsInChildren<SphereCollider>().Length == 2, "Duplicate leaves were not updated");
                    require(f.target.GetComponentsInChildren<Transform>().Count(t => t.name == "helper") == 2, "Duplicate leaves were created");
                }
            });
            check("References outside the selected source are preserved", () =>
            {
                using (var f = new fixture())
                {
                    var external = new GameObject("external_avatar_bone"); f.extra.Add(external);
                    f.helpers[0].GetComponent<ParentConstraint>().AddSource(new ConstraintSource { sourceTransform = external.transform, weight = 0.5f });
                    f.scan(); f.copy("constraint");
                    require(f.target.transform.Find("Cloth_Collider/UpperLeg_L").GetComponent<ParentConstraint>().GetSource(1).sourceTransform == external.transform, "External reference changed");
                }
            });
            check("Humanoid configuration maps bones with arbitrary names and spaced finger keys", () =>
            {
                using (var f = new fixture(false, false))
                {
                    var human = make_humanoid(f.source, f.extra);
                    human[HumanBodyBones.LeftUpperLeg].gameObject.AddComponent<SphereCollider>();
                    child(f.source, "LeftUpperLeg").AddComponent<BoxCollider>();
                    f.scan();
                    require(resolved(f, "sourceScan", "LeftUpperLeg") == human[HumanBodyBones.LeftUpperLeg], "Humanoid configuration not preferred");
                    require(resolved(f, "sourceScan", "Left Index Proximal") == human[HumanBodyBones.LeftIndexProximal], "Spaced finger key not mapped");
                    f.copy();
                    require(f.target_legs[0].GetComponent<SphereCollider>() != null, "Humanoid mapping not used during copy");
                    require(f.target_legs[0].GetComponent<BoxCollider>() == null, "Named helper merged into humanoid bone");
                }
            });
            check("Changing skin bone references invalidates the preview", () =>
            {
                using (var f = new fixture())
                {
                    f.scan(); f.source.GetComponentInChildren<SkinnedMeshRenderer>().bones = new[] { f.helpers[0].transform };
                    copy_scope(f.window, "other");
                    require(((List<string>)get(f.window, "warnings")).Any(w => w.StartsWith("プレビュー後に対象が変更")), "Stale preview was accepted");
                    require(f.target.GetComponentsInChildren<SphereCollider>().Length == 0, "Stale preview copied values");
                }
            });
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint") != null))
                check("VRC ParentConstraint and PhysBone collider references follow copied helpers", () => check_vrc_copy());
            if (File.Exists("Logs/humanoid_alias_snapshot.json"))
                check("Actual Lime/Rurune hierarchy metadata resolves without false ambiguity", check_hierarchy_snapshot);
            report.passed = report.checks.All(r => r.passed);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/humanoid_alias_copy_results.json", JsonUtility.ToJson(report, true));
            Debug.Log("Humanoid Alias Copy checks: " + report.checks.Count(r => r.passed) + "/" + report.checks.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static Dictionary<HumanBodyBones, Transform> make_humanoid(GameObject root, List<Object> extra)
        {
            var joints = new Dictionary<HumanBodyBones, Transform>();
            Action<HumanBodyBones, HumanBodyBones?, Vector3> joint = (id, parent, position) =>
            {
                var obj = child(parent.HasValue ? joints[parent.Value].gameObject : root, "joint_" + (int)id);
                obj.transform.localPosition = position; joints[id] = obj.transform;
            };
            joint(HumanBodyBones.Hips, null, new Vector3(0, 1, 0));
            joint(HumanBodyBones.Spine, HumanBodyBones.Hips, new Vector3(0, 0.15f, 0));
            joint(HumanBodyBones.Chest, HumanBodyBones.Spine, new Vector3(0, 0.15f, 0));
            joint(HumanBodyBones.Neck, HumanBodyBones.Chest, new Vector3(0, 0.15f, 0));
            joint(HumanBodyBones.Head, HumanBodyBones.Neck, new Vector3(0, 0.1f, 0));
            foreach (var left in new[] { true, false })
            {
                var sign = left ? -1f : 1f;
                var upper_leg = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
                var lower_leg = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
                var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
                var upper_arm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                var lower_arm = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                joint(upper_leg, HumanBodyBones.Hips, new Vector3(sign * 0.1f, -0.05f, 0));
                joint(lower_leg, upper_leg, new Vector3(0, -0.4f, 0.01f));
                joint(foot, lower_leg, new Vector3(0, -0.4f, 0.05f));
                joint(upper_arm, HumanBodyBones.Chest, new Vector3(sign * 0.2f, 0.1f, 0));
                joint(lower_arm, upper_arm, new Vector3(sign * 0.25f, 0, 0));
                joint(hand, lower_arm, new Vector3(sign * 0.2f, 0, 0));
            }
            joint(HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftHand, new Vector3(-0.05f, 0, 0.02f));
            joint(HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexProximal, new Vector3(-0.03f, 0, 0));
            joint(HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftIndexIntermediate, new Vector3(-0.02f, 0, 0));
            var description = new HumanDescription
            {
                human = joints.Select(pair => new HumanBone
                {
                    boneName = pair.Value.name,
                    humanName = HumanTrait.BoneName.First(n => n.Replace(" ", "") == pair.Key.ToString()),
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray(),
                skeleton = new[] { root.transform }.Concat(joints.Values).Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, description); extra.Add(avatar);
            require(avatar.isValid && avatar.isHuman, "Synthetic humanoid avatar invalid");
            root.AddComponent<Animator>().avatar = avatar;
            return joints;
        }

        private static void check_vrc_copy()
        {
            var constraint_type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint")).FirstOrDefault(t => t != null);
            if (constraint_type == null) return; // The no-SDK run exercises all generic cases; the VRC run checks this branch.
            var collider_type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider")).First(t => t != null);
            var physbone_type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone")).First(t => t != null);
            using (var f = new fixture())
            {
                var source_constraint = f.helpers[0].AddComponent(constraint_type);
                var source_collider = f.helpers[0].AddComponent(collider_type);
                var physbone = child(f.source, "physbone").AddComponent(physbone_type);
                collider_type.GetField("rootTransform", flags).SetValue(source_collider, f.helpers[0].transform);
                var colliders = (IList)physbone_type.GetField("colliders", flags).GetValue(physbone); colliders.Add(source_collider);
                using (var serialized = new SerializedObject(source_constraint))
                {
                    var sources = serialized.FindProperty("Sources");
                    require(sources != null, "VRC constraint Sources property missing");
                    var iterator = sources.Copy(); var end = sources.GetEndProperty(); var assigned = false;
                    while (iterator.Next(true) && !SerializedProperty.EqualContents(iterator, end))
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference || !iterator.name.Contains("SourceTransform")) continue;
                        iterator.objectReferenceValue = f.source_legs[0]; assigned = true; break;
                    }
                    require(assigned, "VRC constraint source transform property missing"); serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                f.scan(); f.copy(); f.copy("physbone"); f.copy("constraint");
                var target_helper = f.target.transform.Find("Cloth_Collider/UpperLeg_L");
                var copied_constraint = target_helper.GetComponent(constraint_type);
                var copied_collider = target_helper.GetComponent(collider_type);
                var copied_physbone = f.target.transform.Find("physbone").GetComponent(physbone_type);
                require(((IList)physbone_type.GetField("colliders", flags).GetValue(copied_physbone))[0] as Component == copied_collider, "PhysBone retained source collider reference");
                require(collider_type.GetField("rootTransform", flags).GetValue(copied_collider) as Transform == target_helper, "Collider root was not remapped");
                using (var serialized = new SerializedObject(copied_constraint))
                {
                    var iterator = serialized.GetIterator(); var found = false;
                    while (iterator.Next(true)) if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.name.Contains("SourceTransform") && iterator.objectReferenceValue == f.target_legs[0]) found = true;
                    require(found, "VRC constraint retained source bone reference");
                }
            }
        }

        [Serializable] private class hierarchy_snapshot { public root_snapshot[] roots; }
        [Serializable] private class root_snapshot { public string name; public string[] nodes; public string[] skin_bones; public string[] component_paths; }
        private static void check_hierarchy_snapshot()
        {
            // Only local hierarchy metadata is read. Meshes, materials and avatar assets are not copied or distributed.
            var snapshot = JsonUtility.FromJson<hierarchy_snapshot>(File.ReadAllText("Logs/humanoid_alias_snapshot.json"));
            var roots = new List<GameObject>();
            var window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
            try
            {
                foreach (var data in snapshot.roots)
                {
                    var root = new GameObject(data.name); roots.Add(root);
                    var paths = new Dictionary<string, Transform> { [""] = root.transform };
                    foreach (var path in data.nodes.Where(p => p.Length > 0).OrderBy(p => p.Count(c => c == '/')))
                    {
                        var last = path.LastIndexOf('/');
                        paths[path] = child(paths[last < 0 ? "" : path.Substring(0, last)].gameObject, path.Substring(last + 1)).transform;
                    }
                    add_skin(root, data.skin_bones.Select(p => paths[p]));
                    // A supported component on each original copy location exercises matching without loading proprietary components.
                    if (roots.Count == 1) foreach (var path in data.component_paths.Distinct()) paths[path].gameObject.AddComponent<BoxCollider>();
                }
                ((ObjectField)get(window, "sourceField")).SetValueWithoutNotify(roots[0]);
                ((ObjectField)get(window, "targetField")).SetValueWithoutNotify(roots[1]);
                window.GetType().GetField("copySkinnedMeshMaterials", flags).SetValue(window, false);
                call(window, "Scan");
                foreach (var field in new[] { "sourceScan", "targetScan" })
                    require(!((IEnumerable)get(get(window, field), "AmbiguousKeys")).Cast<string>().Any(), field + " remains ambiguous");
                copy_scope(window, "other");
                require(((List<string>)get(window, "warnings")).Any(w => w.StartsWith("コピー完了:")), string.Join(";", (List<string>)get(window, "warnings")));
                foreach (var helper in roots[0].transform.Find("Cloth_Collider").Cast<Transform>())
                {
                    var copied = roots[1].transform.Find("Cloth_Collider/" + helper.name);
                    require(copied != null && copied.GetComponent<BoxCollider>() != null, "Actual helper location not preserved: " + helper.name);
                }
                var target_scan = get(window, "targetScan");
                foreach (var candidate in ((IDictionary)get(target_scan, "ResolvedByKey")).Values)
                    require(((Transform)get(candidate, "Transform")).GetComponent<BoxCollider>() == null, "Actual helper copied onto a real bone");
            }
            finally
            {
                Object.DestroyImmediate(window);
                foreach (var root in roots) Object.DestroyImmediate(root);
                Undo.ClearAll();
            }
        }
    }
}
