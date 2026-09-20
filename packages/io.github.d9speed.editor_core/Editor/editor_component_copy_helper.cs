#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace D9speed_BaseEditorUtils
{
    public enum ComponentCopyMode { UpdateOrAdd, Add, SkipExisting }

    public static class EditorComponentCopyHelper
    {
        /// <summary>同じ型が複数ある場合も、コピー元と同じ出現順のコンポーネントを返す。</summary>
        public static Component FindCounterpart(Component source, GameObject target, IEnumerable<Component> selected = null)
        {
            if (source == null || target == null) return null;
            var sources = selected == null ? source.GetComponents(source.GetType())
                : selected.Where(item => item != null && item.gameObject == source.gameObject && item.GetType() == source.GetType()).ToArray();
            var targets = target.GetComponents(source.GetType());
            int index = Array.IndexOf(sources, source);
            return index >= 0 && index < targets.Length ? targets[index] : null;
        }

        public static bool IsInside(Object value, Transform root)
        {
            var transform = value is GameObject go ? go.transform : (value as Component)?.transform;
            return transform != null && root != null && (transform == root || transform.IsChildOf(root));
        }

        public static void RecordPrefabChanges(Object value)
        {
            if (value == null) return;
            if (PrefabUtility.IsPartOfPrefabInstance(value))
                PrefabUtility.RecordPrefabInstancePropertyModifications(value);
            EditorUtility.SetDirty(value);
        }

        /// <summary>プレビュー後に対象や設定値が変わったことを検出する。内容は保存・出力しない。</summary>
        public static string GetHierarchyStamp(GameObject root)
        {
            if (root == null) return string.Empty;
            var text = new StringBuilder();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                text.Append(EditorObjectHelper.GetObjectId(transform)).Append(':');
                text.Append(EditorObjectHelper.GetObjectId(transform.parent)).Append(':');
                text.Append(EditorJsonUtility.ToJson(transform.gameObject)).Append('\n');
                foreach (var component in transform.GetComponents<Component>())
                {
                    text.Append(EditorObjectHelper.GetObjectId(component)).Append(':');
                    if (component != null) text.Append(EditorJsonUtility.ToJson(component));
                    text.Append('\n');
                }
            }
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }
}
#endif
