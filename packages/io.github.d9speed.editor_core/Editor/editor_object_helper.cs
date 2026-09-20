#if UNITY_EDITOR
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    public static class EditorObjectHelper
    {
        /// <summary>識別子順で取得する。6000.5以降はEntityId、それ以前は旧APIと同じInstanceID順。</summary>
        public static T[] FindSceneObjects<T>(bool include_inactive = false) where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            var objects = Object.FindObjectsByType<T>(
                include_inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            System.Array.Sort(objects, (left, right) => left.GetEntityId().CompareTo(right.GetEntityId()));
            return objects;
#else
            return Object.FindObjectsByType<T>(
                include_inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID);
#endif
        }

        /// <summary>同一セッション内のキャッシュ用ID。EntityIdを32bitに切り詰めない。</summary>
        public static long GetObjectId(Object target)
        {
            if (target == null) return 0;
#if UNITY_6000_5_OR_NEWER
            return unchecked((long)EntityId.ToULong(target.GetEntityId()));
#else
            return target.GetInstanceID();
#endif
        }
    }
}
#endif
