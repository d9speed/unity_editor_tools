using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed.HumanoidRandomHandPose.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class RemainingToolsSmokeChecks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static void Run(Action<string, Action> check)
        {
            check("Three editor packages are registered and stay out of player assemblies", () =>
            {
                var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                foreach (var suffix in new[] { "animation_tools", "skinned_mesh_tools", "prefab_color_variants" })
                    require(packages.Any(p => p.name == "io.github.d9speed." + suffix && p.version == "0.1.0"), suffix);
                require(!CompilationPipeline.GetAssemblies(AssembliesType.Player).Any(a => a.name.StartsWith("D9speed.")), "Editor-only assemblies");
            });
            check("Animation and color variant menus open", () =>
            {
                open<AnimatorPlaybackPreviewWindow>("D9speed/Animation/Animator Playback Preview");
                open<HumanoidRandomHandPoseWindow>("D9speed/Animation/Random Hand Muscle Generator");
                open<PrefabColorVariantMaker>("D9speed/Tools/PrefabColorVariantMaker");
            });
            check("Hand pose API changes fingers and restores a generated humanoid", () =>
            {
                var root = new GameObject("synthetic_humanoid");
                Avatar avatar = null;
                try
                {
                    var animator = create_humanoid(root, out avatar);
                    using (var handler = new HumanPoseHandler(avatar, root.transform))
                    {
                        HumanPose before = default;
                        handler.GetHumanPose(ref before);
                        var settings = HumanoidRandomHandPoseApi.DefaultSettings;
                        settings.random_seed = 123;
                        settings.blend_duration = 0;
                        settings.finger_cluster_range = new Vector2(0.7f, 0.7f);
                        settings.thumb_range = new Vector2(0.5f, 0.5f);
                        settings.variation = 0;
                        require(HumanoidRandomHandPoseApi.TryStartPeriodic(animator, settings, false, out int id, out string error), error);
                        require(HumanoidRandomHandPoseApi.GenerateNow(id), "Periodic generation");
                        HumanPose changed = default;
                        handler.GetHumanPose(ref changed);
                        require(changed.muscles.Where((v, i) => Mathf.Abs(v - before.muscles[i]) > 0.05f).Any(), "No muscle changed");
                        require(HumanoidRandomHandPoseApi.StopPeriodic(id, true), "Stop session");
                        HumanPose restored = default;
                        handler.GetHumanPose(ref restored);
                        require(!restored.muscles.Where((v, i) => Mathf.Abs(v - before.muscles[i]) > 0.02f).Any(), "Original pose not restored");
                        require(!HumanoidRandomHandPoseApi.TryApplyOnce(null, settings, false, out var invalid) && !invalid.success, "Invalid target rejected");
                    }
                }
                finally { HumanoidRandomHandPoseApi.StopAll(); Object.DestroyImmediate(root); if (avatar != null) Object.DestroyImmediate(avatar); }
            });
            check("Animation preview creates and disposes its graph and hand mask", () =>
            {
                var root = new GameObject("preview_animator");
                var animator = root.AddComponent<Animator>();
                var clip = new AnimationClip();
                var window = ScriptableObject.CreateInstance<AnimatorPlaybackPreviewWindow>();
                try
                {
                    set(window, "target_animator", animator);
                    var mask = (AvatarMask)call(window, "create_hands_fingers_avatar_mask");
                    require(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers) && !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body), "Mask regions");
                    call(window, "play_clip", clip);
                    require(((PlayableGraph)get(window, "clip_graph")).IsValid(), "Preview graph");
                    call(window, "stop_clip_graph");
                    require(!((PlayableGraph)get(window, "clip_graph")).IsValid(), "Graph cleanup");
                }
                finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); Object.DestroyImmediate(root); }
            });
            check("Skinned mesh inspector edits, undoes and resets a synthetic blend shape", () =>
            {
                var root = new GameObject("blend_shape_fixture");
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                mesh.AddBlendShapeFrame("Smile", 100, new[] { Vector3.up, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                renderer.sharedMesh = mesh;
                var editor = UnityEditor.Editor.CreateEditor(renderer);
                try
                {
                    require(editor is SkinnedMeshRendererEditorExtra, "Custom inspector registration");
                    require(editor.CreateInspectorGUI().childCount > 0, "Inspector UI");
                    Undo.IncrementCurrentGroup();
                    call(editor, "SetBlendShapeWeight", renderer, 0, 55f, "fixture", true, "");
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                    require(Mathf.Approximately(renderer.GetBlendShapeWeight(0), 55), "Weight edit");
                    Undo.PerformUndo();
                    require(Mathf.Approximately(renderer.GetBlendShapeWeight(0), 0), "Weight Undo");
                    renderer.SetBlendShapeWeight(0, 44);
                    call(editor, "ResetAllBlendShapes", renderer);
                    require(Mathf.Approximately(renderer.GetBlendShapeWeight(0), 0), "Reset");
                }
                finally { Object.DestroyImmediate(editor); Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
            });
            check("Color variant creation preserves the synthetic source and replaces only mapped materials", () =>
            {
                var folder = "Assets/variant_fixture_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                var original = new Material(Shader.Find("Standard")) { name = "original" };
                var replacement = new Material(original) { name = "replacement" };
                var untouched = new Material(original) { name = "untouched" };
                AssetDatabase.CreateAsset(original, folder + "/original.mat");
                AssetDatabase.CreateAsset(replacement, folder + "/replacement.mat");
                AssetDatabase.CreateAsset(untouched, folder + "/untouched.mat");
                var root = new GameObject("original_fixture");
                root.AddComponent<MeshRenderer>().sharedMaterials = new[] { original, untouched };
                var source_path = folder + "/original.prefab";
                var source = PrefabUtility.SaveAsPrefabAsset(root, source_path);
                Object.DestroyImmediate(root);
                var before = File.ReadAllBytes(source_path);
                var window = ScriptableObject.CreateInstance<PrefabColorVariantMaker>();
                try
                {
                    set(window, "variantColumns", new List<PrefabColorVariantMaker.VariantColumn> { new PrefabColorVariantMaker.VariantColumn { suffix = "_blue" } });
                    set(window, "materialMappings", new List<PrefabColorVariantMaker.MaterialMapping> { new PrefabColorVariantMaker.MaterialMapping { original = original, replacements = new List<Material> { replacement } } });
                    call(window, "CreateVariant", source, source_path, 0);
                    var variant = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/original_blue.prefab");
                    require(variant != null && PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant, "Prefab variant output");
                    require(variant.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(new[] { replacement, untouched }), "Material assignment");
                    require(before.SequenceEqual(File.ReadAllBytes(source_path)), "Source prefab was modified");
                }
                finally { Object.DestroyImmediate(window); }
            });
        }

        private static Animator create_humanoid(GameObject root, out Avatar avatar)
        {
            var transforms = new Dictionary<HumanBodyBones, Transform>();
            Action<HumanBodyBones, Transform, Vector3> add = (bone, parent, position) => {
                var go = new GameObject(bone.ToString()); go.transform.SetParent(parent, false); go.transform.localPosition = position; transforms.Add(bone, go.transform);
            };
            add(HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
            add(HumanBodyBones.Spine, transforms[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
            add(HumanBodyBones.Chest, transforms[HumanBodyBones.Spine], new Vector3(0, .2f, 0));
            add(HumanBodyBones.Neck, transforms[HumanBodyBones.Chest], new Vector3(0, .2f, 0));
            add(HumanBodyBones.Head, transforms[HumanBodyBones.Neck], new Vector3(0, .15f, 0));
            foreach (var side in new[] { "Left", "Right" })
            {
                float sign = side == "Left" ? -1 : 1;
                Func<string, HumanBodyBones> bone = name => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + name);
                add(bone("UpperLeg"), transforms[HumanBodyBones.Hips], new Vector3(sign * .1f, -.1f, 0));
                add(bone("LowerLeg"), transforms[bone("UpperLeg")], new Vector3(0, -.4f, 0));
                add(bone("Foot"), transforms[bone("LowerLeg")], new Vector3(0, -.4f, .05f));
                add(bone("UpperArm"), transforms[HumanBodyBones.Chest], new Vector3(sign * .2f, .1f, 0));
                add(bone("LowerArm"), transforms[bone("UpperArm")], new Vector3(sign * .25f, 0, 0));
                add(bone("Hand"), transforms[bone("LowerArm")], new Vector3(sign * .25f, 0, 0));
                int finger_index = 0;
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    add(bone(finger + "Proximal"), transforms[bone("Hand")], new Vector3(sign * .05f, 0, .04f - finger_index++ * .02f));
                    add(bone(finger + "Intermediate"), transforms[bone(finger + "Proximal")], new Vector3(sign * .03f, 0, 0));
                    add(bone(finger + "Distal"), transforms[bone(finger + "Intermediate")], new Vector3(sign * .025f, 0, 0));
                }
            }
            var description = new HumanDescription {
                human = transforms.Select(pair => new HumanBone { boneName = pair.Value.name, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f
            };
            avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            require(avatar.isValid && avatar.isHuman, "Synthetic humanoid fixture");
            var animator = root.AddComponent<Animator>(); animator.avatar = avatar; return animator;
        }
        private static void open<T>(string path) where T : EditorWindow {
            var existing = Resources.FindObjectsOfTypeAll<T>();
            try { require(EditorApplication.ExecuteMenuItem(path), path); require(Resources.FindObjectsOfTypeAll<T>().Any(), path + " window"); }
            finally { foreach (var window in Resources.FindObjectsOfTypeAll<T>().Except(existing)) window.Close(); }
        }
        private static object get(object target, string name) => target.GetType().GetField(name, flags).GetValue(target);
        private static void set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        private static object call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
        private static void require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
