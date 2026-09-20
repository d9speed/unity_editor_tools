using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace D9speed_BaseEditorUtils
{
    public static class EditorPathUtility
    {
        public static string GetFullAssetPath(string asset_path)
        {
            if (string.IsNullOrWhiteSpace(asset_path)) return string.Empty;
            asset_path = asset_path.Replace('\\', '/');
            if (asset_path == "Assets" || asset_path.StartsWith("Assets/", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, asset_path));
            var package = PackageInfo.FindForAssetPath(asset_path);
            if (package == null) return string.Empty;
            var prefix = "Packages/" + package.name;
            return Path.GetFullPath(Path.Combine(package.resolvedPath, asset_path.Substring(prefix.Length).TrimStart('/')));
        }

        public static string[] GetSelectedAssetPaths() => Selection.assetGUIDs
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToArray();

        [MenuItem("Assets/CopyFullPath")]
        private static void CopyFullPath()
        {
            var paths = GetSelectedAssetPaths().Select(GetFullAssetPath).Where(path => path.Length > 0).ToArray();
            if (paths.Length > 0) EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, paths);
        }

        [MenuItem("Assets/CopyFullPath", true)]
        private static bool CanCopyFullPath() => GetSelectedAssetPaths().Length > 0;

        public static string GetAnimationPath(Transform selected)
        {
            if (selected == null) return string.Empty;
            Transform root = null;
            for (var current = selected; current != null; current = current.parent)
                if (current.GetComponent<Animator>() != null) { root = current; break; }
            if (root == null) root = PrefabUtility.GetNearestPrefabInstanceRoot(selected.gameObject)?.transform ?? selected.root;
            return AnimationUtility.CalculateTransformPath(selected, root);
        }

        [MenuItem("GameObject/Copy Animation Property Path", false, 0)]
        private static void CopyAnimationPath() => EditorGUIUtility.systemCopyBuffer =
            "path: \"" + GetAnimationPath(Selection.activeTransform).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        [MenuItem("GameObject/Copy Animation Property Path", true)]
        private static bool CanCopyAnimationPath() => Selection.activeTransform != null;
    }
}
