/*
Forked from https://qiita.com/k7a/items/eb5a3ee4ed6448343543 by k7a

Copyright (c) 2020 Narazaka

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

   1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.

   2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.

   3. This notice may not be removed or altered from any source
   distribution.
*/

// Adapted from Narazaka's CopyAssetsWithDependency (2020), originally forked from k7a.
// D9speed modifications: AssetDatabase copies, unique destinations, normalized paths,
// folder GUID remapping, and explicit validation. See the retained notice below.
// https://gist.github.com/Narazaka/1ae51c8515e55ca3dbeec5a3eba313ed

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    public static class CopyAssetsWithDependency
    {
        [MenuItem("Assets/CopyAssetsWithDependency")]
        public static void Execute()
        {
            try { DeepCopy(EditorPathUtility.GetSelectedAssetPaths()); }
            catch (Exception error) { Debug.LogError("アセットの複製に失敗しました: " + error.Message); }
        }

        [MenuItem("Assets/CopyAssetsWithDependency", true)]
        private static bool CanExecute() => EditorPathUtility.GetSelectedAssetPaths().Length > 0
            && EditorPathUtility.GetSelectedAssetPaths().All(path => path.StartsWith("Assets/", StringComparison.Ordinal));

        private static string[] GetRoots(IEnumerable<string> paths)
        {
            var selected = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Replace('\\','/').TrimEnd('/')).Distinct().OrderBy(path => path.Length).ToArray();
            if (selected.Any(path => !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Any(part => part == ".." || part == ".")
                || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))))
                throw new ArgumentException("Assets内のアセットまたはフォルダを選択してください。Assets自体とPackagesは複製対象外です。");
            return selected.Where(path => !selected.Any(parent => parent != path && AssetDatabase.IsValidFolder(parent)
                && path.StartsWith(parent + "/", StringComparison.Ordinal))).ToArray();
        }

        public static Dictionary<string, string> GetAllAssetAndCopyPaths(IEnumerable<string> base_asset_paths)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var root in GetRoots(base_asset_paths))
            {
                var folder = AssetDatabase.IsValidFolder(root);
                var desired = folder ? root + " copy" : Path.GetDirectoryName(root).Replace('\\','/') + "/" + Path.GetFileNameWithoutExtension(root) + " copy" + Path.GetExtension(root);
                var destination = AssetDatabase.GenerateUniqueAssetPath(desired);
                map.Add(root, destination);
                if (!folder) continue;
                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] {root}))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.StartsWith(root + "/", StringComparison.Ordinal)) map[path] = destination + path.Substring(root.Length);
                }
            }
            return map;
        }

        public static Dictionary<string, string> DeepCopy(IEnumerable<string> base_asset_paths, bool confirm = true)
        {
            var roots = GetRoots(base_asset_paths);
            var map = GetAllAssetAndCopyPaths(roots);
            if (map.Count == 0) return map;
            if (confirm && !EditorUtility.DisplayDialog("参照を保って複製", "選択したアセットとフォルダ内を複製します。複製対象どうしの参照をコピー先へ変更し、対象外への参照は維持します。\n\n" + string.Join("\n", roots), "複製", "キャンセル"))
                return new Dictionary<string, string>();
            foreach (var root in roots)
                if (!AssetDatabase.CopyAsset(root, map[root])) throw new IOException("複製できませんでした: " + root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var guid_map = map.ToDictionary(pair => AssetDatabase.AssetPathToGUID(pair.Key), pair => AssetDatabase.AssetPathToGUID(pair.Value));
            if (guid_map.Any(pair => pair.Key.Length == 0 || pair.Value.Length == 0)) throw new IOException("コピー先のGUIDを取得できませんでした。");
            foreach (var destination in map.Values)
            {
                RewriteReferences(destination, guid_map, false);
                RewriteReferences(destination + ".meta", guid_map, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return map;
        }

        private static void RewriteReferences(string path, Dictionary<string, string> guid_map, bool metadata)
        {
            if (!File.Exists(path)) return;
            // Binary assets are preserved; reference rewriting applies to Unity text serialization.
            using (var reader = new StreamReader(path))
                if (!metadata && reader.ReadLine()?.StartsWith("%YAML", StringComparison.Ordinal) != true) return;
            var original = File.ReadAllText(path);
            var rewritten = Regex.Replace(original, @"\bguid: ([0-9a-f]{32})\b", match =>
                guid_map.TryGetValue(match.Groups[1].Value, out var replacement) ? "guid: " + replacement : match.Value);
            if (rewritten != original) File.WriteAllText(path, rewritten, new UTF8Encoding(false));
        }
    }
}
