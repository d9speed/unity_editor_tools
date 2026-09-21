using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace D9speed_BaseEditorUtils
{
    public sealed class TransformMirrorPlan
    {
        public sealed class Pair
        {
            public Object source;
            public Object destination;
            public string source_path;
            public string destination_path;
            public string status;
            public bool warning;
        }

        public sealed class Creation
        {
            public GameObject source;
            public Transform search_root;
            public Transform parent;
            public string name;
        }

        public readonly List<Creation> creations = new();
        public readonly List<Pair> pairs = new();
        public readonly Dictionary<Component, Dictionary<Object, Object>> reference_pairs = new();
        public readonly Dictionary<Transform, string> planned_paths = new();
        public readonly List<string> warnings = new();
        public string signature => string.Join("\n", creations.Select(c => c.source.GetInstanceID() + ":" +
            (c.parent != null ? c.parent.GetInstanceID() : 0) + ":" + c.search_root.GetInstanceID() + ":" + c.name)) +
            "\n" + string.Join("\n", pairs.Select(p => p.source?.GetInstanceID() + ":" + p.destination?.GetInstanceID() + ":" + p.source_path + ":" + p.destination_path + ":" + p.status));

        public static string opposite_name(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var suffix_end = name.Length;
            var number_start = name.LastIndexOf('.');
            if (number_start > 0 && name.Length - number_start == 4 && name.Skip(number_start + 1).All(char.IsDigit))
                suffix_end = number_start;
            if (suffix_end < 2 || (name[suffix_end - 2] != '_' && name[suffix_end - 2] != '.')) return name;
            var side = name[suffix_end - 1];
            var opposite = side == 'L' ? 'R' : side == 'R' ? 'L' : side == 'l' ? 'r' : side == 'r' ? 'l' : side;
            return name.Substring(0, suffix_end - 1) + opposite + name.Substring(suffix_end);
        }

        public static IEnumerable<Transform> depth_first(Transform root)
        {
            if (root == null) yield break;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                for (var i = current.childCount - 1; i >= 0; i--) stack.Push(current.GetChild(i));
            }
        }

        public static string path(Transform value)
        {
            if (value == null) return "(シーンルート)";
            var names = new Stack<string>();
            for (var current = value; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names);
        }

        public static Transform nearest_root(GameObject value)
        {
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(value);
            return root != null ? root.transform : value.transform.root;
        }

        private static string relative_path(Transform value, Transform root, bool mirrored)
        {
            var parts = new Stack<string>();
            for (var current = value; current != null && current != root; current = current.parent)
                parts.Push(mirrored ? opposite_name(current.name) : current.name);
            return string.Join("/", parts);
        }

        private sealed class SearchScope
        {
            public Transform root;
            public List<Transform> nodes;

            public Transform counterpart(Transform source, out string status)
            {
                status = "対応候補なし・元参照を維持";
                if (source == null || (source != root && !source.IsChildOf(root)))
                {
                    status = "探索ルート外・元参照を維持";
                    return null;
                }
                var original_path = relative_path(source, root, false);
                var expected_path = relative_path(source, root, true);
                if (original_path != expected_path)
                {
                    var matches = nodes.Where(t => t != source && relative_path(t, root, false) == expected_path).ToArray();
                    if (matches.Length == 1) { status = "左右の階層パスが一致"; return matches[0]; }
                    if (matches.Length > 1) { status = "候補が複数・元参照を維持"; return null; }
                }
                var expected_name = opposite_name(source.name);
                if (expected_name == source.name) { status = "左右名なし・共通参照を維持"; return null; }
                var named = nodes.Where(t => t != source && t.name == expected_name).ToArray();
                if (named.Length == 1) { status = "ルート内の一意な左右名"; return named[0]; }
                if (named.Length > 1) status = "候補が複数・元参照を維持";
                return null;
            }
        }

        public static TransformMirrorPlan build(IEnumerable<GameObject> targets)
        {
            var plan = new TransformMirrorPlan();
            var selected = targets.Where(t => t != null).Distinct().ToList();
            var roots = selected.Where(t => !selected.Any(p => p != t && t.transform.IsChildOf(p.transform))).ToList();
            var scopes = new Dictionary<Transform, SearchScope>();
            var allocated = new Dictionary<string, HashSet<string>>();
            foreach (var source in roots)
            {
                var scope_root = nearest_root(source);
                if (!scopes.TryGetValue(scope_root, out var scope))
                    scopes.Add(scope_root, scope = new SearchScope { root = scope_root, nodes = depth_first(scope_root).ToList() });
                var parent = EditorUtility.IsPersistent(source) ? null : source.transform.parent;
                if (parent != null)
                {
                    var matched_parent = scope.counterpart(parent, out _);
                    if (matched_parent != null && !roots.Any(r => matched_parent == r.transform || matched_parent.IsChildOf(r.transform)))
                        parent = matched_parent;
                }
                var opposite = opposite_name(source.name);
                var requested_name = opposite != source.name ? opposite : source.name + "_Mirrored";
                var scene = EditorUtility.IsPersistent(source) ? UnityEngine.SceneManagement.SceneManager.GetActiveScene() : source.scene;
                var key = scene.handle + ":" + (parent == null ? 0 : parent.GetInstanceID());
                if (!allocated.TryGetValue(key, out var names))
                {
                    var siblings = parent == null ? scene.GetRootGameObjects().Select(g => g.name) : Enumerable.Range(0, parent.childCount).Select(i => parent.GetChild(i).name);
                    allocated.Add(key, names = new HashSet<string>(siblings));
                }
                var result_name = requested_name;
                for (var i = 1; names.Contains(result_name); i++) result_name = requested_name + "_mirror_" + i;
                names.Add(result_name);
                plan.creations.Add(new Creation { source = source, search_root = scope_root, parent = parent, name = result_name });
                foreach (var node in depth_first(source.transform))
                {
                    var output_path = node == source.transform
                        ? (parent == null ? "" : path(parent) + "/") + result_name
                        : plan.planned_paths[node.parent] + "/" + opposite_name(node.name);
                    plan.planned_paths.Add(node, output_path);
                    var renamed = node == source.transform && requested_name != result_name;
                    plan.pairs.Add(new Pair { source = node.gameObject, source_path = path(node), destination_path = output_path,
                        status = renamed ? "新規作成（同名があるため別名）" : "新規作成", warning = renamed });
                }
            }

            foreach (var creation in plan.creations)
            {
                var scope = scopes[creation.search_root];
                foreach (var node in depth_first(creation.source.transform))
                foreach (var component in node.GetComponents<Component>())
                {
                    if (component == null) continue;
                    if (component is MeshCollider)
                        plan.warnings.Add(path(node) + ": MeshColliderのメッシュ形状は反転しません。");
                    if (!TransformMirrorComponents.supported(component)) continue;
                    using var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (!TransformMirrorComponents.user_reference(property)) continue;
                        var reference = property.objectReferenceValue;
                        var reference_transform = transform_of(reference);
                        if (reference_transform == null) continue;
                        var label = path(node) + " · " + component.GetType().Name + "/" + property.propertyPath;
                        if (plan.planned_paths.TryGetValue(reference_transform, out var output))
                        {
                            plan.pairs.Add(new Pair { source = reference, source_path = label + " → " + path(reference_transform), destination_path = output, status = "今回作成するペアへ参照置換" });
                            continue;
                        }
                        var counterpart = scope.counterpart(reference_transform, out var status);
                        var replacement = corresponding_object(reference, counterpart);
                        if (counterpart != null && replacement == null) status = "対応コンポーネントが不一致・元参照を維持";
                        if (replacement != null)
                        {
                            if (!plan.reference_pairs.TryGetValue(component, out var mapping))
                                plan.reference_pairs.Add(component, mapping = new Dictionary<Object, Object>());
                            mapping[reference] = replacement;
                        }
                        var warn = replacement == null && status != "左右名なし・共通参照を維持";
                        plan.pairs.Add(new Pair { source = reference, destination = replacement, source_path = label + " → " + path(reference_transform),
                            destination_path = path(transform_of(replacement) ?? reference_transform), status = status, warning = warn });
                        if (warn) plan.warnings.Add(label + ": " + status);
                    }
                    if (TransformMirrorComponents.is_vrc_constraint(component))
                    {
                        var target = TransformMirrorComponents.read(component, "TargetTransform") as Transform;
                        if (target != null && !plan.planned_paths.ContainsKey(target))
                            plan.warnings.Add(path(node) + ": 複製範囲外のTargetTransformを持つVRC Constraintは複製側を無効にします。");
                    }
                }
            }
            return plan;
        }

        internal static Transform transform_of(Object value) => value is GameObject go ? go.transform : value is Component c ? c.transform : null;

        internal static Object corresponding_object(Object source, Transform counterpart)
        {
            if (counterpart == null) return null;
            if (source is GameObject) return counterpart.gameObject;
            if (source is Transform) return counterpart;
            if (source is not Component component) return null;
            var originals = component.gameObject.GetComponents(component.GetType());
            var candidates = counterpart.GetComponents(component.GetType());
            if (originals.Length != candidates.Length) return null;
            return candidates[Array.IndexOf(originals, component)];
        }
    }
}
