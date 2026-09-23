using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_Test_Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class physbone_rotation_pivot_checks
    {
        private const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class check_result { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class report_data
        {
            public bool passed;
            public string unity_version;
            public List<check_result> checks = new List<check_result>();
        }

        public static void run()
        {
            var report = new report_data { unity_version = Application.unityVersion };
            check(report, "Mode switch preserves pose; center mode preserves position", fixture =>
            {
                var position = fixture.collider.position;
                var rotation = fixture.collider.rotation;
                fixture.set_mode(true);
                near(fixture.collider.position, position, "Mode switch moved collider");
                same_rotation(fixture.collider.rotation, rotation, "Mode switch rotated collider");
                fixture.set_mode(false);
                fixture.set_rotation(new Vector3(25, 40, 80));
                near(fixture.collider.position, position, "Center mode moved collider");
                same_rotation(fixture.collider.rotation, Quaternion.Euler(25, 40, 80), "Center rotation");
            });
            check(report, "Bone pivot moves center by 90 degrees; bone pose and dimensions stay unchanged", fixture =>
            {
                var bone_position = fixture.bone.position;
                var bone_rotation = fixture.bone.rotation;
                fixture.set_mode(true);
                var next = Quaternion.AngleAxis(90, Vector3.forward) * fixture.collider.rotation;
                fixture.set_rotation(next.eulerAngles);
                near(fixture.collider.position, new Vector3(-.2f, 0, 0), "90 degree pivot orbit");
                same_rotation(fixture.collider.rotation, next, "Collider orientation");
                near(fixture.bone.position, bone_position, "Bone moved");
                same_rotation(fixture.bone.rotation, bone_rotation, "Bone rotated");
                require(Mathf.Approximately(fixture.collider.radius, .04f) && Mathf.Approximately(fixture.collider.height, .3f), "Dimensions changed");
            });
            check(report, "Position and rotation Undo and Redo together; UI refreshes", fixture =>
            {
                var before_position = fixture.collider.position;
                var before_rotation = fixture.collider.rotation;
                fixture.set_mode(true);
                Undo.IncrementCurrentGroup();
                fixture.set_rotation((Quaternion.AngleAxis(90, Vector3.forward) * before_rotation).eulerAngles);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                var after_position = fixture.collider.position;
                var after_rotation = fixture.collider.rotation;
                Undo.PerformUndo();
                near(fixture.collider.position, before_position, "Undo position");
                same_rotation(fixture.collider.rotation, before_rotation, "Undo rotation");
                near(get<Vector3Field>(fixture.window, "position_field").value, before_position * 1000, "Undo UI position");
                Undo.PerformRedo();
                near(fixture.collider.position, after_position, "Redo position");
                same_rotation(fixture.collider.rotation, after_rotation, "Redo rotation");
                near(get<Vector3Field>(fixture.window, "position_field").value, after_position * 1000, "Redo UI position");
            });
            check(report, "Child root uses nearest humanoid ancestor with rotation and nonuniform scale", fixture =>
            {
                var child = new GameObject("offset_collider_root").transform;
                child.SetParent(fixture.bone, false);
                child.localPosition = new Vector3(0, .1f, 0);
                child.localRotation = Quaternion.Euler(0, 0, 90);
                child.localScale = new Vector3(1, 2, .5f);
                fixture.collider.rootTransform = child;
                fixture.collider.position = new Vector3(.1f, 0, 0);
                fixture.collider.rotation = Quaternion.identity;
                fixture.set_mode(true);
                fixture.set_rotation(new Vector3(0, 0, 90));
                near(child.TransformPoint(fixture.collider.position), fixture.bone.TransformPoint(new Vector3(-.2f, 0, 0)), "Ancestor pivot orbit");
                require(get<HelpBox>(fixture.window, "rotation_pivot_status").text.Contains(fixture.bone.name), "Pivot bone label");
            });
            check(report, "Unset root uses collider transform and discovers parent humanoid", fixture =>
            {
                fixture.collider.transform.SetParent(fixture.bone, false);
                fixture.collider.rootTransform = null;
                fixture.collider.rotation = Quaternion.identity;
                fixture.set_mode(true);
                fixture.set_rotation(new Vector3(0, 0, 90));
                near(fixture.collider.position, new Vector3(-.2f, 0, 0), "Unset root orbit");
            });
            check(report, "Unmapped root disables bone rotation; center mode remains available", fixture =>
            {
                fixture.collider.rootTransform = fixture.root.transform;
                var before_position = fixture.collider.position;
                var before_rotation = fixture.collider.rotation;
                fixture.set_mode(true);
                require(!get<Vector3Field>(fixture.window, "rotation_field").enabledSelf, "Unmapped rotation enabled");
                require(!get<Button>(fixture.window, "align_rotation_button").enabledSelf, "Unmapped align enabled");
                call(fixture.window, "rotate_selected_collider", Quaternion.Euler(0, 0, 90), "Rejected rotation");
                near(fixture.collider.position, before_position, "Unmapped root moved");
                same_rotation(fixture.collider.rotation, before_rotation, "Unmapped root rotated");
                fixture.set_mode(false);
                require(get<Vector3Field>(fixture.window, "rotation_field").enabledSelf, "Center mode unavailable");
            });
            check(report, "Foreign avatar root is not matched to selected humanoid", fixture =>
            {
                var outsider = new GameObject("foreign_root");
                try
                {
                    fixture.collider.rootTransform = outsider.transform;
                    fixture.set_mode(true);
                    require(!get<Vector3Field>(fixture.window, "rotation_field").enabledSelf, "Foreign root accepted");
                }
                finally { Object.DestroyImmediate(outsider); }
            });
            check(report, "Align respects pivot mode and reverse rotation returns center", fixture =>
            {
                fixture.collider.rotation = Quaternion.identity;
                fixture.set_mode(true);
                fixture.set_rotation(new Vector3(0, 0, 90));
                call(fixture.window, "rotate_selected_collider", Quaternion.identity, "Align PhysBone Collider To Bone");
                near(fixture.collider.position, new Vector3(0, .2f, 0), "Align did not reverse orbit");
                same_rotation(fixture.collider.rotation, Quaternion.identity, "Align orientation");
            });
            check(report, "Mirrored collider resolves opposite bone pivot", fixture =>
            {
                fixture.set_mode(true);
                call(fixture.window, "CreateMirroredCollider");
                var mirrored = get<VRCPhysBoneCollider>(fixture.window, "selected_collider");
                require(mirrored != fixture.collider && mirrored.rootTransform == fixture.animator.GetBoneTransform(HumanBodyBones.RightUpperArm), "Mirror target");
                var old_distance = mirrored.position.magnitude;
                fixture.set_rotation((Quaternion.AngleAxis(90, Vector3.forward) * mirrored.rotation).eulerAngles);
                require(Mathf.Abs(mirrored.position.magnitude - old_distance) < .00001f, "Mirror pivot radius");
                require(get<HelpBox>(fixture.window, "rotation_pivot_status").text.Contains("RightUpperArm"), "Stale pivot label");
            });
            report.passed = report.checks.All(result => result.passed);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/physbone_rotation_pivot_results.json", JsonUtility.ToJson(report, true));
            Debug.Log("PhysBone rotation pivot checks: " + report.checks.Count(result => result.passed) + "/" + report.checks.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void check(report_data report, string name, Action<fixture> action)
        {
            try
            {
                using (var value = new fixture()) action(value);
                report.checks.Add(new check_result { name = name, passed = true, detail = "passed" });
            }
            catch (Exception error)
            {
                report.checks.Add(new check_result { name = name, passed = false, detail = error.ToString() });
                Debug.LogError(name + ": " + error);
            }
        }

        private sealed class fixture : IDisposable
        {
            public readonly GameObject root = new GameObject("pivot_fixture");
            public readonly Animator animator;
            public readonly Transform bone;
            public readonly VRCPhysBoneCollider collider;
            public readonly PhysBoneWeightColliderGeneratorWindow window;
            private readonly Avatar avatar;

            public fixture()
            {
                var bones = new Dictionary<HumanBodyBones, Transform>();
                Action<HumanBodyBones, Transform, Vector3> add = (id, parent, position) =>
                {
                    var transform = new GameObject(id.ToString()).transform;
                    transform.SetParent(parent, false);
                    transform.localPosition = position;
                    bones.Add(id, transform);
                };
                add(HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
                add(HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
                add(HumanBodyBones.Chest, bones[HumanBodyBones.Spine], new Vector3(0, .2f, 0));
                add(HumanBodyBones.Neck, bones[HumanBodyBones.Chest], new Vector3(0, .2f, 0));
                add(HumanBodyBones.Head, bones[HumanBodyBones.Neck], new Vector3(0, .15f, 0));
                foreach (var side in new[] { "Left", "Right" })
                {
                    var sign = side == "Left" ? -1 : 1;
                    Func<string, HumanBodyBones> id = name => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + name);
                    add(id("UpperLeg"), bones[HumanBodyBones.Hips], new Vector3(sign * .1f, -.1f, 0));
                    add(id("LowerLeg"), bones[id("UpperLeg")], new Vector3(0, -.4f, 0));
                    add(id("Foot"), bones[id("LowerLeg")], new Vector3(0, -.4f, .05f));
                    add(id("UpperArm"), bones[HumanBodyBones.Chest], new Vector3(sign * .2f, .1f, 0));
                    add(id("LowerArm"), bones[id("UpperArm")], new Vector3(sign * .25f, 0, 0));
                    add(id("Hand"), bones[id("LowerArm")], new Vector3(sign * .25f, 0, 0));
                }
                avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
                {
                    human = bones.Select(pair => new HumanBone { boneName = pair.Value.name, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                    skeleton = root.GetComponentsInChildren<Transform>().Select(item => new SkeletonBone { name = item.name, position = item.localPosition, rotation = item.localRotation, scale = item.localScale }).ToArray(),
                    upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f
                });
                require(avatar.isValid && avatar.isHuman, "Humanoid fixture invalid");
                animator = root.AddComponent<Animator>();
                animator.avatar = avatar;
                root.transform.SetPositionAndRotation(new Vector3(2, -1, 3), Quaternion.Euler(17, -31, 22));
                root.transform.localScale = Vector3.one * 1.75f;
                bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var holder = new GameObject("collider_holder");
                holder.transform.SetParent(root.transform, false);
                collider = holder.AddComponent<VRCPhysBoneCollider>();
                collider.rootTransform = bone;
                collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
                collider.position = new Vector3(0, .2f, 0);
                collider.rotation = Quaternion.Euler(12, 23, 34);
                collider.radius = .04f;
                collider.height = .3f;
                window = ScriptableObject.CreateInstance<PhysBoneWeightColliderGeneratorWindow>();
                set(window, "humanoid_animator", animator);
                set(window, "selected_collider", collider);
                window.Show();
                call(window, "CreateGUI");
            }

            public void set_mode(bool value)
            {
                var field = get<PopupField<string>>(window, "rotation_pivot_field");
                field.value = field.choices[value ? 1 : 0];
                require(get<bool>(window, "rotate_around_humanoid_bone") == value, "Mode input event did not reach window");
                call(window, "RefreshColliderControls");
            }
            public void set_rotation(Vector3 value)
            {
                get<Vector3Field>(window, "rotation_field").value = value;
                same_rotation(get<VRCPhysBoneCollider>(window, "selected_collider").rotation, Quaternion.Euler(value), "Rotation input event did not reach window");
            }
            public void Dispose()
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Undo.ClearAll();
            }
        }

        private static T get<T>(object target, string name) => (T)target.GetType().GetField(name, flags).GetValue(target);
        private static void set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        private static object call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
        private static void near(Vector3 actual, Vector3 expected, string name) => require(Vector3.Distance(actual, expected) < .0001f, name + ": " + actual + " != " + expected);
        private static void same_rotation(Quaternion actual, Quaternion expected, string name) => require(Quaternion.Angle(actual, expected) < .05f, name);
        private static void require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
