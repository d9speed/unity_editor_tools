using System;
using System.Collections.Generic;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEngine;

public partial class HumanoidAliasComponentCopierWindow
{
    [SerializeField] private bool clear_ma_avatar_references = true;

    private sealed class ma_reference_reset
    {
        public readonly List<string> arrays = new List<string>();
        public readonly List<string> avatar_references = new List<string>();
        public readonly List<string> direct_references = new List<string>();
        public bool bone_proxy;
        public bool has_changes => arrays.Count + avatar_references.Count + direct_references.Count > 0 || bone_proxy;
        public string description => string.Join(" / ", arrays) + (arrays.Count > 0 ? "（空の一覧） " : "")
            + string.Join(" / ", avatar_references) + " " + string.Join(" / ", direct_references)
            + (bone_proxy ? " boneReference / subPath" : "");
    }

    private ma_reference_reset build_ma_reference_reset(Component component, ScanResult source)
    {
        if (!clear_ma_avatar_references || component == null || source == null
            || source.SkinnedMeshTargetBySource.Count == 0 || GetComponentCategory(component) != CategoryModularAvatar) return null;
        var reset = new ma_reference_reset();
        using (var serialized = new SerializedObject(component))
        {
            if (component.GetType().Name == "ModularAvatarBlendshapeSync"
                && source.SkinnedMeshTargetBySource.ContainsKey(component.transform))
            {
                var bindings = serialized.FindProperty("Bindings");
                if (bindings != null && bindings.isArray && bindings.arraySize > 0) reset.arrays.Add(bindings.propertyPath);
            }
            if (component.GetType().Name == "ModularAvatarBoneProxy")
            {
                var bone = serialized.FindProperty("boneReference");
                var path = serialized.FindProperty("subPath");
                reset.bone_proxy = bone != null && path != null
                    && (bone.intValue != (int)HumanBodyBones.LastBone || !string.IsNullOrEmpty(path.stringValue));
            }
            var property = serialized.GetIterator();
            var enter_children = true;
            while (property.Next(enter_children))
            {
                enter_children = true;
                if (reset.arrays.Contains(property.propertyPath)) { enter_children = false; continue; }
                if (property.propertyType == SerializedPropertyType.Generic && property.type == "AvatarObjectReference")
                {
                    if (is_external_ma_reference(property, component, source.Root.transform)) reset.avatar_references.Add(property.propertyPath);
                    enter_children = false;
                    continue;
                }
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || property.propertyPath == "m_Script" || property.propertyPath == "m_GameObject") continue;
                var reference = property.objectReferenceValue;
                if ((reference is GameObject || reference is Component) && !EditorUtility.IsPersistent(reference)
                    && !EditorComponentCopyHelper.IsInside(reference, source.Root.transform)) reset.direct_references.Add(property.propertyPath);
            }
        }
        return reset.has_changes ? reset : null;
    }

    private static bool is_external_ma_reference(SerializedProperty property, Component component, Transform root)
    {
        var path = property.FindPropertyRelative("referencePath");
        var target_object = property.FindPropertyRelative("targetObject");
        if (path == null || target_object == null) return false;
        var reference = target_object.objectReferenceValue;
        if (reference != null) return !EditorComponentCopyHelper.IsInside(reference, root);
        if (string.IsNullOrEmpty(path.stringValue)) return false;
        reference = resolve_ma_path_reference(property, component);
        return reference == null || !EditorComponentCopyHelper.IsInside(reference, root);
    }

    private static UnityEngine.Object resolve_ma_path_reference(SerializedProperty property, Component component)
    {
        if (string.IsNullOrEmpty(property.FindPropertyRelative("referencePath")?.stringValue)) return null;
        var reference_type = component.GetType().Assembly.GetType("nadena.dev.modular_avatar.core.AvatarObjectReference");
        var get_reference = reference_type?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(SerializedProperty) }, null);
        if (get_reference != null)
        {
            try
            {
                return get_reference.Invoke(null, new object[] { property }) as UnityEngine.Object;
            }
            catch (TargetInvocationException) { }
        }
        return null;
    }

    private void remap_ma_local_reference_paths(Component original, Component destination, ScanResult source, ScanResult target,
        HashSet<GameObject> added_objects, Dictionary<GameObject, GameObject> added_map)
    {
        if (!clear_ma_avatar_references || source.SkinnedMeshTargetBySource.Count == 0
            || GetComponentCategory(original) != CategoryModularAvatar) return;
        var reference_type = original.GetType().Assembly.GetType("nadena.dev.modular_avatar.core.AvatarObjectReference");
        var set_reference = reference_type?.GetMethod("Set", new[] { typeof(GameObject) });
        if (set_reference == null) return;
        using (var source_serialized = new SerializedObject(original))
        using (var target_serialized = new SerializedObject(destination))
        {
            var property = source_serialized.GetIterator();
            var enter_children = true;
            while (property.Next(enter_children))
            {
                enter_children = true;
                if (property.propertyType != SerializedPropertyType.Generic || property.type != "AvatarObjectReference") continue;
                enter_children = false;
                var reference = property.FindPropertyRelative("targetObject").objectReferenceValue
                    ?? resolve_ma_path_reference(property, original);
                if (!EditorComponentCopyHelper.IsInside(reference, source.Root.transform)) continue;
                var source_transform = reference is GameObject obj ? obj.transform : ((Component)reference).transform;
                var mapped = ResolveTargetTransform(source_transform, source, target, added_objects, added_map);
                var target_property = target_serialized.FindProperty(property.propertyPath);
                if (mapped == null || target_property == null) continue;
                target_property.FindPropertyRelative("targetObject").objectReferenceValue = mapped.gameObject;
                var mapped_reference = Activator.CreateInstance(reference_type);
                set_reference.Invoke(mapped_reference, new object[] { mapped.gameObject });
                var mapped_path = reference_type.GetField("referencePath").GetValue(mapped_reference) as string;
                if (!string.IsNullOrEmpty(mapped_path)) target_property.FindPropertyRelative("referencePath").stringValue = mapped_path;
            }
            target_serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void apply_ma_reference_reset(Component destination, ma_reference_reset reset)
    {
        if (reset == null) return;
        using (var serialized = new SerializedObject(destination))
        {
            foreach (var path in reset.arrays) serialized.FindProperty(path).ClearArray();
            foreach (var path in reset.avatar_references)
            {
                var property = serialized.FindProperty(path);
                property.FindPropertyRelative("referencePath").stringValue = "";
                property.FindPropertyRelative("targetObject").objectReferenceValue = null;
            }
            foreach (var path in reset.direct_references) serialized.FindProperty(path).objectReferenceValue = null;
            if (reset.bone_proxy)
            {
                serialized.FindProperty("boneReference").intValue = (int)HumanBodyBones.LastBone;
                serialized.FindProperty("subPath").stringValue = "";
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (reset.bone_proxy)
            destination.GetType().GetMethod("ClearCache", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null)?.Invoke(destination, new object[] { true });
        if (reset.arrays.Contains("Bindings"))
            destination.GetType().GetMethod("Rebind", BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null)?.Invoke(destination, null);
    }
}
