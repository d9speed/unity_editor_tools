using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace D9speed_BaseEditorUtils
{
    public static class TransformMirrorComponents
    {
        private const BindingFlags member_flags = BindingFlags.Instance | BindingFlags.Public;

        private static bool inherits(Component component, string type_name)
        {
            for (var type = component.GetType(); type != null; type = type.BaseType)
                if (type.FullName == type_name) return true;
            return false;
        }

        public static bool is_vrc_constraint(Component component) => inherits(component, "VRC.Dynamics.VRCConstraintBase");
        private static bool is_physbone(Component component) => inherits(component, "VRC.Dynamics.VRCPhysBoneBase");
        private static bool is_physbone_collider(Component component) => inherits(component, "VRC.Dynamics.VRCPhysBoneColliderBase");
        public static bool supported(Component component) => component is IConstraint || component is Collider ||
            is_vrc_constraint(component) || is_physbone(component) || is_physbone_collider(component);

        internal static bool user_reference(SerializedProperty property) =>
            property.propertyType == SerializedPropertyType.ObjectReference &&
            property.name != "m_Script" && property.name != "m_GameObject" &&
            property.name != "m_CorrespondingSourceObject" && property.name != "m_PrefabInstance" && property.name != "m_PrefabAsset";

        internal static object read(object target, string name)
        {
            var type = target.GetType();
            return type.GetField(name, member_flags)?.GetValue(target) ?? type.GetProperty(name, member_flags)?.GetValue(target);
        }

        private static void write(object target, string name, object value)
        {
            var type = target.GetType();
            var field = type.GetField(name, member_flags);
            if (field != null) field.SetValue(target, value);
            else type.GetProperty(name, member_flags)?.SetValue(target, value);
        }

        private static void apply_configuration(Component component) =>
            component.GetType().GetMethod("ApplyConfigurationChanges", member_flags, null, Type.EmptyTypes, null)?.Invoke(component, null);

        private static Transform effective_transform(Component component, string field)
        {
            var value = read(component, field) as Transform;
            return value != null ? value : component.transform;
        }

        public static Vector3 reflect_vector(Vector3 value) => new(-value.x, value.y, value.z);
        public static Vector3 reflect_point(Vector3 value, Vector3 pivot) => pivot + reflect_vector(value - pivot);
        public static Quaternion reflect_rotation(Quaternion value) => new(value.x, -value.y, -value.z, value.w);
        private static Vector3 reflect_euler(Vector3 value) => new(value.x, -value.y, -value.z);

        private sealed class ComponentPair
        {
            public Component source;
            public Component destination;
            public bool enabled;
        }

        public static List<GameObject> execute(TransformMirrorPlan plan, Vector3 pivot, bool mirror_rotation, bool copy_blendshapes)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Modeで実行してください。");
            if (plan == null || plan.creations.Count == 0) return new List<GameObject>();
            var current = TransformMirrorPlan.build(plan.creations.Select(c => c.source));
            if (plan.signature != current.signature) throw new InvalidOperationException("候補が変わりました。「候補を更新」で確認してください。");
            Undo.IncrementCurrentGroup();
            var undo_group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Transform Mirror");
            var copies = new List<GameObject>();
            var holders = new List<GameObject>();
            var objects = new Dictionary<Object, Object>();
            var transforms = new List<KeyValuePair<Transform, Transform>>();
            var components = new List<ComponentPair>();
            try
            {
                foreach (var creation in plan.creations)
                {
                    var source = creation.source;
                    var scene = EditorUtility.IsPersistent(source) ? SceneManager.GetActiveScene() : source.scene;
                    var holder = new GameObject("transform_mirror_staging") { hideFlags = HideFlags.HideAndDontSave };
                    holder.SetActive(false);
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    holders.Add(holder);
                    GameObject clone;
                    if (EditorUtility.IsPersistent(source) && PrefabUtility.IsPartOfPrefabAsset(source) && source.transform.parent == null)
                        clone = (GameObject)PrefabUtility.InstantiatePrefab(source, holder.transform);
                    else clone = Object.Instantiate(source, holder.transform, false);
                    if (clone == null) throw new InvalidOperationException("複製に失敗しました: " + source.name);
                    copies.Add(clone);
                    Undo.RegisterCreatedObjectUndo(clone, "Transform Mirror");
                    reconnect_prefab(source, clone);
                    clone.name = creation.name;
                    var originals = TransformMirrorPlan.depth_first(source.transform).ToArray();
                    var destinations = TransformMirrorPlan.depth_first(clone.transform).ToArray();
                    if (originals.Length != destinations.Length) throw new InvalidOperationException("複製前後の階層が一致しません。");
                    for (var i = 0; i < originals.Length; i++)
                    {
                        var src = originals[i];
                        var dst = destinations[i];
                        objects.Add(src, dst);
                        objects.Add(src.gameObject, dst.gameObject);
                        transforms.Add(new KeyValuePair<Transform, Transform>(src, dst));
                        if (i > 0) dst.name = TransformMirrorPlan.opposite_name(src.name);
                        var src_components = src.GetComponents<Component>();
                        var dst_components = dst.GetComponents<Component>();
                        if (src_components.Length != dst_components.Length) throw new InvalidOperationException("複製前後のコンポーネントが一致しません。");
                        for (var j = 0; j < src_components.Length; j++)
                        {
                            var src_component = src_components[j];
                            var dst_component = dst_components[j];
                            if (src_component == null || dst_component == null || src_component is Transform) continue;
                            objects.Add(src_component, dst_component);
                            if (!supported(src_component)) continue;
                            var pair = new ComponentPair { source = src_component, destination = dst_component, enabled = dst_component is Behaviour b && b.enabled };
                            components.Add(pair);
                            if (dst_component is Behaviour behaviour) behaviour.enabled = false;
                            if (dst_component is IConstraint constraint)
                            {
                                constraint.constraintActive = false;
                                constraint.locked = false;
                            }
                            if (is_vrc_constraint(dst_component))
                            {
                                write(dst_component, "IsActive", false);
                                write(dst_component, "Locked", false);
                                write(dst_component, "FreezeToWorld", false);
                            }
                        }
                    }
                    clone.SetActive(false);
                    clone.transform.SetParent(creation.parent, false);
                }

                Object resolve(Component owner, Object value)
                {
                    if (value == null) return null;
                    if (objects.TryGetValue(value, out var clone)) return clone;
                    return plan.reference_pairs.TryGetValue(owner, out var mapping) && mapping.TryGetValue(value, out var counterpart) ? counterpart : value;
                }

                foreach (var pair in transforms)
                {
                    pair.Value.localScale = pair.Key.localScale;
                    if (pair.Key.parent != null && pair.Value.parent == null) pair.Value.localScale = pair.Key.lossyScale;
                    pair.Value.SetPositionAndRotation(reflect_point(pair.Key.position, pivot),
                        mirror_rotation ? reflect_rotation(pair.Key.rotation) : pair.Key.rotation);
                }
                foreach (var pair in components)
                {
                    remap_references(pair.source, pair.destination, value => resolve(pair.source, value));
                    if (pair.source is IConstraint unity_constraint)
                        mirror_unity_constraint(unity_constraint, (IConstraint)pair.destination, pivot, mirror_rotation);
                    else if (is_vrc_constraint(pair.source))
                        mirror_vrc_constraint(pair.source, pair.destination, pivot, mirror_rotation);
                    else if (is_physbone(pair.source) || is_physbone_collider(pair.source))
                        mirror_physbone(pair.source, pair.destination, pivot, mirror_rotation);
                    else mirror_collider(pair.source, pair.destination, pivot);
                }
                if (!copy_blendshapes)
                {
                    foreach (var pair in transforms)
                    {
                        var renderers = pair.Value.GetComponents<SkinnedMeshRenderer>();
                        var source_renderers = pair.Key.GetComponents<SkinnedMeshRenderer>();
                        for (var i = 0; i < renderers.Length; i++)
                        {
                            var renderer = renderers[i];
                            var defaults = PrefabUtility.GetCorrespondingObjectFromSource(source_renderers[i]);
                            if (renderer.sharedMesh == null) continue;
                            for (var j = 0; j < renderer.sharedMesh.blendShapeCount; j++)
                                renderer.SetBlendShapeWeight(j, defaults != null && defaults.sharedMesh == renderer.sharedMesh ? defaults.GetBlendShapeWeight(j) : 0f);
                        }
                    }
                }

                foreach (var pair in components)
                {
                    var destination = pair.destination;
                    var safe_target = true;
                    if (is_vrc_constraint(destination))
                    {
                        var source_target = read(pair.source, "TargetTransform") as Transform;
                        safe_target = source_target == null || objects.ContainsKey(source_target);
                        write(destination, "Locked", read(pair.source, "Locked"));
                        write(destination, "FreezeToWorld", read(pair.source, "FreezeToWorld"));
                        write(destination, "IsActive", safe_target && (bool)read(pair.source, "IsActive"));
                    }
                    if (destination is IConstraint constraint)
                    {
                        constraint.locked = ((IConstraint)pair.source).locked;
                        constraint.constraintActive = ((IConstraint)pair.source).constraintActive;
                    }
                    if (destination is Behaviour behaviour) behaviour.enabled = pair.enabled && safe_target;
                    if (is_vrc_constraint(destination) || is_physbone(destination) || is_physbone_collider(destination)) apply_configuration(destination);
                    EditorUtility.SetDirty(destination);
                }
                for (var i = 0; i < copies.Count; i++) copies[i].SetActive(plan.creations[i].source.activeSelf);
                foreach (var copy in copies)
                {
                    foreach (var transform in TransformMirrorPlan.depth_first(copy.transform))
                        if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                    foreach (var component in copy.GetComponentsInChildren<Component>(true))
                        if (component != null && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(copy)) PrefabUtility.RecordPrefabInstancePropertyModifications(copy);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(copy.scene);
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undo_group);
                return copies;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo_group);
                foreach (var copy in copies) if (copy != null) Object.DestroyImmediate(copy);
                throw;
            }
            finally
            {
                foreach (var holder in holders) if (holder != null) Object.DestroyImmediate(holder);
            }
        }

        private static void remap_references(Component source, Component destination, Func<Object, Object> resolve)
        {
            using var original = new SerializedObject(source);
            using var copy = new SerializedObject(destination);
            var property = original.GetIterator();
            while (property.Next(true))
            {
                if (!user_reference(property)) continue;
                if (TransformMirrorPlan.transform_of(property.objectReferenceValue) == null) continue;
                var target = copy.FindProperty(property.propertyPath);
                if (target != null) target.objectReferenceValue = resolve(property.objectReferenceValue);
            }
            copy.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void reconnect_prefab(GameObject source, GameObject clone)
        {
            if (EditorUtility.IsPersistent(source) || !PrefabUtility.IsAnyPrefabInstanceRoot(source)) return;
            var asset = PrefabUtility.GetCorrespondingObjectFromSource(source);
            if (asset == null || asset.transform.parent != null) return;
            var nodes = TransformMirrorPlan.depth_first(clone.transform).ToArray();
            var original_components = new HashSet<Component>(clone.GetComponentsInChildren<Component>(true).Where(c => c != null));
            var original_nodes = new HashSet<Transform>(nodes);
            var parents = nodes.Select(t => t.parent).ToArray();
            var sibling_indices = nodes.Select(t => t.GetSiblingIndex()).ToArray();
            clone.name = source.name;
            PrefabUtility.ConvertToPrefabInstance(clone, asset, new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                changeRootNameToAssetName = false,
                logInfo = false
            }, InteractionMode.AutomatedAction);
            if (nodes.Any(t => t == null) || original_components.Any(c => c == null))
                throw new InvalidOperationException("Prefabの対応付けで複製階層が変化しました。");
            // Conversion merges missing prefab objects; keep the source's removed overrides.
            foreach (var added in TransformMirrorPlan.depth_first(clone.transform).Where(t => !original_nodes.Contains(t)).ToArray())
                if (added != null) Object.DestroyImmediate(added.gameObject);
            foreach (var added in clone.GetComponentsInChildren<Component>(true).Where(c => c != null && !original_components.Contains(c)).Reverse().ToArray())
                if (added != null && added is not Transform) Object.DestroyImmediate(added);
            for (var i = 1; i < nodes.Length; i++)
            {
                if (nodes[i].parent != parents[i]) nodes[i].SetParent(parents[i], false);
                nodes[i].SetSiblingIndex(sibling_indices[i]);
            }
        }

        private static Vector3 local_point(Vector3 value, Transform source, Transform destination, Vector3 pivot)
        {
            var world = source == null ? value : source.TransformPoint(value);
            var mirrored = reflect_point(world, pivot);
            return destination == null ? mirrored : destination.InverseTransformPoint(mirrored);
        }

        private static Vector3 local_vector(Vector3 value, Transform source, Transform destination)
        {
            var world = source == null ? value : source.TransformVector(value);
            return destination == null ? reflect_vector(world) : destination.InverseTransformVector(reflect_vector(world));
        }

        private static Quaternion local_rotation(Quaternion value, Transform source, Transform destination)
        {
            var mirrored = reflect_rotation((source == null ? Quaternion.identity : source.rotation) * value);
            return destination == null ? mirrored : Quaternion.Inverse(destination.rotation) * mirrored;
        }

        private static Vector3 parent_offset(Vector3 value, Transform source, Transform destination, Vector3 pivot)
        {
            var world = source == null ? value : source.position + source.rotation * value;
            var mirrored = reflect_point(world, pivot);
            return destination == null ? mirrored : Quaternion.Inverse(destination.rotation) * (mirrored - destination.position);
        }

        private static void mirror_unity_constraint(IConstraint source, IConstraint destination, Vector3 pivot, bool rotation)
        {
            var src = ((Component)source).transform;
            var dst = ((Component)destination).transform;
            if (source is ParentConstraint parent && destination is ParentConstraint copy_parent)
            {
                copy_parent.translationAtRest = local_point(parent.translationAtRest, src.parent, dst.parent, pivot);
                if (rotation) copy_parent.rotationAtRest = local_rotation(Quaternion.Euler(parent.rotationAtRest), src.parent, dst.parent).eulerAngles;
                for (var i = 0; i < parent.sourceCount; i++)
                {
                    var a = parent.GetSource(i).sourceTransform;
                    var b = copy_parent.GetSource(i).sourceTransform;
                    copy_parent.SetTranslationOffset(i, parent_offset(parent.GetTranslationOffset(i), a, b, pivot));
                    if (rotation) copy_parent.SetRotationOffset(i, local_rotation(Quaternion.Euler(parent.GetRotationOffset(i)), a, b).eulerAngles);
                }
            }
            if (source is PositionConstraint position && destination is PositionConstraint copy_position)
            {
                copy_position.translationAtRest = local_point(position.translationAtRest, src.parent, dst.parent, pivot);
                var src_center = source_center(Enumerable.Range(0, position.sourceCount).Select(i => position.GetSource(i)), false);
                var dst_center = source_center(Enumerable.Range(0, copy_position.sourceCount).Select(i => copy_position.GetSource(i)), false);
                copy_position.translationOffset = reflect_point(src_center + position.translationOffset, pivot) - dst_center;
            }
            if (source is RotationConstraint rotate && destination is RotationConstraint copy_rotate && rotation)
            {
                copy_rotate.rotationAtRest = local_rotation(Quaternion.Euler(rotate.rotationAtRest), src.parent, dst.parent).eulerAngles;
                copy_rotate.rotationOffset = reflect_euler(rotate.rotationOffset);
            }
            if (source is AimConstraint aim && destination is AimConstraint copy_aim && rotation)
            {
                copy_aim.rotationAtRest = local_rotation(Quaternion.Euler(aim.rotationAtRest), src.parent, dst.parent).eulerAngles;
                copy_aim.rotationOffset = reflect_euler(aim.rotationOffset);
                copy_aim.aimVector = reflect_vector(aim.aimVector);
                copy_aim.upVector = reflect_vector(aim.upVector);
                copy_aim.worldUpVector = aim.worldUpType == AimConstraint.WorldUpType.ObjectRotationUp
                    ? local_vector(aim.worldUpVector, aim.worldUpObject, copy_aim.worldUpObject) : reflect_vector(aim.worldUpVector);
            }
            if (source is LookAtConstraint look && destination is LookAtConstraint copy_look && rotation)
            {
                copy_look.rotationAtRest = local_rotation(Quaternion.Euler(look.rotationAtRest), src.parent, dst.parent).eulerAngles;
                copy_look.rotationOffset = reflect_euler(look.rotationOffset);
                copy_look.roll = -look.roll;
            }
        }

        private static void mirror_vrc_constraint(Component source, Component destination, Vector3 pivot, bool rotation)
        {
            var src_target = effective_transform(source, "TargetTransform");
            var dst_target = effective_transform(destination, "TargetTransform");
            var local = (bool)read(source, "SolveInLocalSpace");
            if (read(source, "PositionAtRest") is Vector3 rest)
                write(destination, "PositionAtRest", local_point(rest, src_target.parent, dst_target.parent, pivot));
            if (read(source, "PositionOffset") is Vector3 offset)
            {
                var src_center = source_center(vrc_sources(source), local);
                var dst_center = source_center(vrc_sources(destination), local);
                write(destination, "PositionOffset", local_point(src_center + offset,
                    local ? src_target.parent : null, local ? dst_target.parent : null, pivot) - dst_center);
            }
            if (rotation)
            {
                if (read(source, "RotationAtRest") is Vector3 rest_rotation)
                    write(destination, "RotationAtRest", local_rotation(Quaternion.Euler(rest_rotation), src_target.parent, dst_target.parent).eulerAngles);
                foreach (var field in new[] { "RotationOffset", "AimAxis", "UpAxis", "WorldUpVector" })
                {
                    if (read(source, field) is not Vector3 vector) continue;
                    var result = field == "RotationOffset" ? reflect_euler(vector) : reflect_vector(vector);
                    if (field == "WorldUpVector" && read(source, "WorldUp")?.ToString() == "ObjectRotationUp")
                        result = local_vector(vector, read(source, "WorldUpTransform") as Transform, read(destination, "WorldUpTransform") as Transform);
                    write(destination, field, result);
                }
                if (read(source, "Roll") is float roll) write(destination, "Roll", -roll);
            }
            if (!source.GetType().Name.Contains("ParentConstraint")) return;
            var original_sources = (IList)read(source, "Sources");
            var copied_sources = (IList)read(destination, "Sources");
            for (var i = 0; i < original_sources.Count; i++)
            {
                var a = original_sources[i];
                var b = copied_sources[i];
                var src = read(a, "SourceTransform") as Transform;
                var dst = read(b, "SourceTransform") as Transform;
                var position_offset = (Vector3)read(a, "ParentPositionOffset");
                var rotation_offset = Quaternion.Euler((Vector3)read(a, "ParentRotationOffset"));
                if (local)
                {
                    var src_position = src != null ? src.localPosition : Vector3.zero;
                    var src_rotation = src != null ? src.localRotation : Quaternion.identity;
                    var dst_position = dst != null ? dst.localPosition : Vector3.zero;
                    var dst_rotation = dst != null ? dst.localRotation : Quaternion.identity;
                    var mirrored = local_point(src_position + src_rotation * position_offset, src_target.parent, dst_target.parent, pivot);
                    write(b, "ParentPositionOffset", Quaternion.Inverse(dst_rotation) * (mirrored - dst_position));
                    if (rotation) write(b, "ParentRotationOffset", (Quaternion.Inverse(dst_rotation) *
                        local_rotation(src_rotation * rotation_offset, src_target.parent, dst_target.parent)).eulerAngles);
                }
                else
                {
                    write(b, "ParentPositionOffset", parent_offset(position_offset, src, dst, pivot));
                    if (rotation) write(b, "ParentRotationOffset", local_rotation(rotation_offset, src, dst).eulerAngles);
                }
                copied_sources[i] = b;
            }
            write(destination, "Sources", copied_sources);
        }

        private static IEnumerable<ConstraintSource> vrc_sources(Component component)
        {
            var sources = (IList)read(component, "Sources");
            for (var i = 0; i < sources.Count; i++)
                yield return new ConstraintSource { sourceTransform = read(sources[i], "SourceTransform") as Transform, weight = (float)read(sources[i], "Weight") };
        }

        private static Vector3 source_center(IEnumerable<ConstraintSource> sources, bool local)
        {
            var total = 0f;
            var center = Vector3.zero;
            foreach (var source in sources)
            {
                if (source.sourceTransform == null || source.weight <= 0) continue;
                total += source.weight;
                center += (local ? source.sourceTransform.localPosition : source.sourceTransform.position) * source.weight;
            }
            return total > 0 ? center / total : Vector3.zero;
        }

        private static void mirror_physbone(Component source, Component destination, Vector3 pivot, bool rotation)
        {
            var src = effective_transform(source, "rootTransform");
            var dst = effective_transform(destination, "rootTransform");
            if (is_physbone_collider(source))
            {
                write(destination, "position", local_point((Vector3)read(source, "position"), src, dst, pivot));
                if (rotation) write(destination, "rotation", local_rotation((Quaternion)read(source, "rotation"), src, dst));
                return;
            }
            write(destination, "endpointPosition", local_vector((Vector3)read(source, "endpointPosition"), src, dst));
            if (read(source, "staticFreezeAxis") is Vector3 freeze_axis) write(destination, "staticFreezeAxis", reflect_vector(freeze_axis));
            if (rotation)
            {
                write(destination, "limitRotation", reflect_euler((Vector3)read(source, "limitRotation")));
                // Limit curves multiply each Euler channel; the mirrored base angle carries its sign.
            }
        }

        private static void mirror_collider(Component source, Component destination, Vector3 pivot)
        {
            if (source is BoxCollider box && destination is BoxCollider copy_box)
                copy_box.center = local_point(box.center, source.transform, destination.transform, pivot);
            else if (source is SphereCollider sphere && destination is SphereCollider copy_sphere)
                copy_sphere.center = local_point(sphere.center, source.transform, destination.transform, pivot);
            else if (source is CapsuleCollider capsule && destination is CapsuleCollider copy_capsule)
                copy_capsule.center = local_point(capsule.center, source.transform, destination.transform, pivot);
            else if (source is CharacterController character && destination is CharacterController copy_character)
                copy_character.center = local_point(character.center, source.transform, destination.transform, pivot);
            else if (source is WheelCollider wheel && destination is WheelCollider copy_wheel)
                copy_wheel.center = local_point(wheel.center, source.transform, destination.transform, pivot);
        }
    }
}
