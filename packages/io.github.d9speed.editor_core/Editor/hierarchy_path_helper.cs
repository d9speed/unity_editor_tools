#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    public static class HierarchyPathHelper
    {
        /// <summary>シーンのルート名を含むパス。対象がない場合はnull。</summary>
        public static string GetHierarchyPath(Transform target)
        {
            return target == null ? null : BuildPath(null, target);
        }

        /// <summary>ルート名を含まない相対パス。ルート自身は空文字、範囲外はnull。</summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            return root == null || target == null ? null : BuildPath(root, target);
        }

        private static string BuildPath(Transform root, Transform target)
        {
            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", names) : null;
        }
    }
}
#endif
