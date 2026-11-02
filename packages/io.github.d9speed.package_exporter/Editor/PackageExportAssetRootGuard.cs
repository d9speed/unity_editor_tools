using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace D9speedBaseEditorUtil
{
/// <summary>
/// エクスポート対象が許可したフォルダの外へはみ出していないかを調べる。
/// 許可フォルダはEditorPrefsに ; 区切りで保存し、全プロジェクトで共有する
/// </summary>
public static class PackageExportAssetRootGuard
{
    public const string DefaultAllowedRoots = "Assets/D9speed/";
    public const string AllowedRootsEditorPrefsKey = "D9speed.PackageExporter.AllowedAssetRoots";
    private static readonly char[] root_separators = { ';', '\n', '\r' };

    public static string GetAllowedRootsSetting()
    {
        return EditorPrefs.GetString(AllowedRootsEditorPrefsKey, DefaultAllowedRoots);
    }

    public static void SetAllowedRootsSetting(string roots)
    {
        EditorPrefs.SetString(AllowedRootsEditorPrefsKey, roots ?? string.Empty);
    }

    /// <summary>
    /// ; 区切りの指定を、末尾に / を付けた正規化済みのフォルダ一覧にする。空欄なら空の一覧
    /// </summary>
    public static List<string> ParseRoots(string roots)
    {
        if (string.IsNullOrWhiteSpace(roots))
        {
            return new List<string>();
        }

        return roots.Split(root_separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRoot)
            .Where(root => !string.IsNullOrEmpty(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        string normalized = root.Trim().Replace('\\', '/').TrimEnd('/');
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "/";
    }

    public static string FormatRoots(string roots)
    {
        return string.Join("; ", ParseRoots(roots));
    }

    /// <summary>
    /// いずれかの許可フォルダの中にあるか。許可フォルダが未設定なら常に true
    /// </summary>
    public static bool IsInsideRoots(string assetPath, string roots)
    {
        List<string> rootList = ParseRoots(roots);
        if (rootList.Count == 0 || string.IsNullOrWhiteSpace(assetPath))
        {
            return true;
        }

        string normalizedPath = assetPath.Trim().Replace('\\', '/').TrimEnd('/') + "/";
        return rootList.Any(root => normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 許可フォルダ外のアセットを並べ替えて返す。許可フォルダが未設定なら常に空
    /// </summary>
    public static List<string> FindOutsideRoot(IEnumerable<string> assetPaths, string roots)
    {
        if (assetPaths == null || ParseRoots(roots).Count == 0)
        {
            return new List<string>();
        }

        return assetPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && !IsInsideRoots(path, roots))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
}
