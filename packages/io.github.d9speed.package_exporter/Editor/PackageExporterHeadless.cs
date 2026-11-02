using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

namespace D9speedBaseEditorUtil
{

/// <summary>
/// 画面を使わずにバッチエクスポートするときの依頼内容。
/// uloop、-executeMethod、将来のUnity CLIはすべてこの形に変換して渡す
/// </summary>
[Serializable]
public class PackageExportRequest
{
    // list: サブフォルダ一覧のみ / plan: 書き出さずに内容を確認 / export: 書き出す
    public string mode = PackageExporterHeadless.ModePlan;
    // 空欄なら ProjectSettings/PackageExporterSettings.asset の設定を使う
    public string profilePath = "";
    // 以下の空欄・nullの項目は、プロファイルまたは現在の設定の値をそのまま使う
    public string exportPath = "";
    public string outPath = "";
    public string packageName = "";
    // 空欄ならPreferencesの許可フォルダ（EditorPrefs）を使う。; 区切りで複数指定可
    public string allowedRoot = "";
    // 書き出すサブフォルダ名（アバター名）。完全一致。空ならプロファイルの選択、なければ全部
    public List<string> subfolders = new List<string>();
    public bool overwrite = false;
    public bool allowOutsideRoot = false;
    public bool listAssets = false;
}

[Serializable]
public class PackageExportItemResult
{
    public string subfolder = "";
    public string packageFile = "";
    public string packagePath = "";
    public string reportPath = "";
    // ready / would_skip_exists / exported / skipped_exists / blocked_outside_root / no_assets / failed / not_run
    public string status = "";
    public bool fileExists = false;
    public int assetCount = 0;
    public List<string> outsideRootAssets = new List<string>();
    public List<string> assets = new List<string>();
}

[Serializable]
public class PackageExportResult
{
    public bool success = false;
    public string mode = "";
    public string message = "";
    public string profilePath = "";
    public string exportPath = "";
    public string outPath = "";
    public string allowedRoot = "";
    public List<string> availableSubfolders = new List<string>();
    public List<string> selectedSubfolders = new List<string>();
    public List<PackageExportItemResult> packages = new List<PackageExportItemResult>();
    public List<string> warnings = new List<string>();
    public List<string> errors = new List<string>();
}

/// <summary>
/// CLIやスクリプトから使う入口。GUIと同じ収集・命名・レポート処理を使う
/// </summary>
public static class PackageExporterHeadless
{
    public const string ModeList = "list";
    public const string ModePlan = "plan";
    public const string ModeExport = "export";

    public static PackageExportResult Run(PackageExportRequest request)
    {
        return PackageExporter.RunHeadless(request ?? new PackageExportRequest());
    }

    /// <summary>
    /// JSONの依頼を受け取り、JSONの結果を返す。execute-dynamic-code などから呼びやすい形
    /// </summary>
    public static string RunJson(string requestJson)
    {
        return JsonConvert.SerializeObject(RunJsonToResult(requestJson), Formatting.Indented);
    }

    public static PackageExportResult RunJsonToResult(string requestJson)
    {
        try
        {
            var request = string.IsNullOrWhiteSpace(requestJson)
                ? new PackageExportRequest()
                : JsonConvert.DeserializeObject<PackageExportRequest>(requestJson);
            return Run(request);
        }
        catch (JsonException ex)
        {
            var result = new PackageExportResult { message = "依頼JSONを読めませんでした。" };
            result.errors.Add(ex.Message);
            return result;
        }
    }
}

public partial class PackageExporter
{
    internal static PackageExportResult RunHeadless(PackageExportRequest request)
    {
        string mode = (request.mode ?? PackageExporterHeadless.ModePlan).Trim().ToLowerInvariant();
        var result = new PackageExportResult { mode = mode };

        if (mode != PackageExporterHeadless.ModeList
            && mode != PackageExporterHeadless.ModePlan
            && mode != PackageExporterHeadless.ModeExport)
        {
            return Fail(result, $"mode は list / plan / export のいずれかです: {request.mode}");
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return Fail(result, "プレイモード中は実行できません。プレイモードを終了してから実行してください。");
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return Fail(result, "コンパイル中またはアセット更新中です。完了してから実行してください。");
        }

        // プロファイル適用で除外キーワードのリストが差し替わるため、元のリストを保持して最後に戻す
        var settings = PackageExporterSettings.instance;
        List<string> originalExclusionKeywords = settings.exclusionKeywords;
        PackageExporter exporter = CreateInstance<PackageExporter>();
        exporter.hideFlags = HideFlags.HideAndDontSave;
        exporter.headlessMode = true;

        try
        {
            exporter.RunHeadlessInternal(request, mode, result);
        }
        catch (Exception ex)
        {
            Fail(result, "エクスポート中にエラーが発生しました: " + ex.Message);
            Debug.LogException(ex);
        }
        finally
        {
            settings.exclusionKeywords = originalExclusionKeywords;
            DestroyImmediate(exporter);
        }

        if (result.success)
        {
            Debug.Log("[PackageExporter CLI] " + result.message);
        }
        else
        {
            Debug.LogWarning("[PackageExporter CLI] " + result.message + "\n" + string.Join("\n", result.errors));
        }
        return result;
    }

    private void RunHeadlessInternal(PackageExportRequest request, string mode, PackageExportResult result)
    {
        if (!ApplyHeadlessRequest(request, result))
        {
            return;
        }

        GetSubfolders();
        result.availableSubfolders = subFolderSelections.Keys
            .Select(folder => Path.GetFileName(folder))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mode == PackageExporterHeadless.ModeList)
        {
            result.success = true;
            result.message = $"{exportPath} のサブフォルダは {result.availableSubfolders.Count} 件です。";
            return;
        }

        if (!SelectHeadlessSubfolders(request.subfolders, result))
        {
            return;
        }

        bool isExport = mode == PackageExporterHeadless.ModeExport;
        if (!Directory.Exists(outPath))
        {
            if (isExport)
            {
                Fail(result, "出力先フォルダがありません: " + outPath);
                return;
            }
            result.warnings.Add("出力先フォルダがありません。export の前に作成してください: " + outPath);
        }

        string dateString;
        try
        {
            dateString = DateTime.Today.ToString(dateFormat);
        }
        catch (FormatException)
        {
            Fail(result, "日付書式が無効です: " + dateFormat);
            return;
        }

        List<string> selectedFolders = subFolderSelections.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        Regex exclusionRegex = BuildExclusionRegex();
        List<string> nestedPrefabs = DetectNestedPrefabDependencies(selectedFolders, exclusionRegex, overrideExclusionForSubfolders);
        var nestedPrefabSet = new HashSet<string>(nestedPrefabs, StringComparer.OrdinalIgnoreCase);
        if (nestedPrefabs.Count > 0)
        {
            result.warnings.Add(
                $"ネストPrefabを {nestedPrefabs.Count} 件検知しました（{(includeNestedPrefabs ? "含める" : "含めない")}設定）: "
                + string.Join(", ", nestedPrefabs));
        }
        foreach (string imageWarning in DetectExcludedMeshCutterImages(selectedFolders, exclusionRegex, overrideExclusionForSubfolders))
        {
            result.warnings.Add("MeshCutterのマスク画像が除外されています: " + imageWarning);
        }

        // 先に全パッケージの計画を作り、問題があれば1つも書き出さない
        var plannedAssets = new Dictionary<PackageExportItemResult, HashSet<string>>();
        foreach (string folder in selectedFolders)
        {
            HashSet<string> assets = CollectExportAssetPaths(
                folder,
                exclusionRegex,
                overrideExclusionForSubfolders,
                includeNestedPrefabs,
                nestedPrefabSet);
            string packageFileName = BuildPackageFileName(Path.GetFileName(folder), dateString);
            string packagePath = Path.Combine(outPath, packageFileName);
            var item = new PackageExportItemResult
            {
                subfolder = Path.GetFileName(folder),
                packageFile = packageFileName,
                packagePath = packagePath,
                assetCount = assets.Count,
                fileExists = File.Exists(packagePath),
                outsideRootAssets = PackageExportAssetRootGuard.FindOutsideRoot(assets, AllowedAssetRoots)
            };
            if (request.listAssets)
            {
                item.assets = assets.Select(path => path.Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (assets.Count == 0)
            {
                item.status = "no_assets";
            }
            else if (item.outsideRootAssets.Count > 0 && !request.allowOutsideRoot)
            {
                item.status = "blocked_outside_root";
            }
            else if (item.fileExists && !request.overwrite)
            {
                item.status = isExport ? "skipped_exists" : "would_skip_exists";
            }
            else
            {
                item.status = "ready";
            }

            if (item.outsideRootAssets.Count > 0)
            {
                result.warnings.Add(
                    $"{item.subfolder}: {result.allowedRoot} 以外のアセットが {item.outsideRootAssets.Count} 件あります"
                    + (request.allowOutsideRoot ? "（allowOutsideRoot 指定により続行）" : "（書き出しを止めました）"));
            }

            result.packages.Add(item);
            plannedAssets[item] = assets;
        }

        foreach (var duplicate in result.packages.GroupBy(pkg => pkg.packagePath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            result.errors.Add("出力ファイル名が重複しています。ファイル名にサブフォルダ名を含めてください: " + duplicate.Key);
        }

        bool hasBlocked = result.packages.Any(pkg => pkg.status == "blocked_outside_root");
        if (hasBlocked)
        {
            result.errors.Add(
                "許可フォルダ外のアセットを含むパッケージがあります。内容をユーザーに確認し、問題なければ allowOutsideRoot を付けて再実行してください。");
        }

        if (!isExport || result.errors.Count > 0)
        {
            if (isExport)
            {
                foreach (var item in result.packages.Where(pkg => pkg.status == "ready" || pkg.status == "skipped_exists"))
                {
                    item.status = "not_run";
                }
            }
            result.success = result.errors.Count == 0;
            int readyCount = result.packages.Count(pkg => pkg.status == "ready");
            result.message = isExport
                ? "問題があるため、1つも書き出していません。"
                : $"計画のみ（書き出していません）。書き出し予定 {readyCount} 件 / 対象 {result.packages.Count} 件。";
            return;
        }

        string lastPackagePath = null;
        foreach (var item in result.packages.Where(pkg => pkg.status == "ready"))
        {
            ExportPackage(plannedAssets[item].ToArray(), item.packagePath);
            if (!File.Exists(item.packagePath))
            {
                item.status = "failed";
                result.errors.Add("パッケージファイルが作成されませんでした: " + item.packagePath);
                continue;
            }

            item.status = "exported";
            item.reportPath = Path.Combine(outPath, Path.GetFileNameWithoutExtension(item.packageFile) + "_components.json");
            WriteExportedAssetsJsonReport(plannedAssets[item], item.reportPath, item.packagePath, includeMetaFiles, includeOtherComponentsInLog);
            lastPackagePath = item.packagePath;
        }

        result.success = result.errors.Count == 0;
        int exportedCount = result.packages.Count(pkg => pkg.status == "exported");
        result.message = $"{exportedCount} 件を書き出しました（対象 {result.packages.Count} 件）。";
        if (lastPackagePath != null)
        {
            Debug.Log("Batch export (CLI) completed. Last package: " + lastPackagePath);
        }
    }

    private bool ApplyHeadlessRequest(PackageExportRequest request, PackageExportResult result)
    {
        if (!string.IsNullOrWhiteSpace(request.profilePath))
        {
            string profileFullPath = ResolveProjectRelativePath(request.profilePath);
            if (!File.Exists(profileFullPath))
            {
                Fail(result, "プロファイルJSONがありません: " + profileFullPath);
                return false;
            }
            if (!TryReadProfileFromJson(profileFullPath, out ExportProfileJson profile))
            {
                Fail(result, "プロファイルJSONを読めませんでした: " + profileFullPath);
                return false;
            }
            ApplyProfile(profile);
            result.profilePath = profileFullPath;
        }

        if (!string.IsNullOrWhiteSpace(request.exportPath))
        {
            exportPath = request.exportPath;
        }
        if (!string.IsNullOrWhiteSpace(request.outPath))
        {
            outPath = ResolveProjectRelativePath(request.outPath);
        }
        if (!string.IsNullOrWhiteSpace(request.packageName))
        {
            packageName = request.packageName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.allowedRoot))
        {
            allowedRootsOverride = request.allowedRoot;
        }
        // 外部アプリの起動はCLIでは行わない
        openWithTE = false;

        exportPath = (exportPath ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        result.exportPath = exportPath;
        result.outPath = outPath;
        result.allowedRoot = PackageExportAssetRootGuard.FormatRoots(AllowedAssetRoots);

        if (string.IsNullOrEmpty(exportPath) || !AssetDatabase.IsValidFolder(exportPath))
        {
            Fail(result, "エクスポート元フォルダがありません（Assets/ からのパスで指定）: " + exportPath);
            return false;
        }

        if (!request.allowOutsideRoot && !PackageExportAssetRootGuard.IsInsideRoots(exportPath, AllowedAssetRoots))
        {
            Fail(result, $"エクスポート元フォルダが {result.allowedRoot} の外です: {exportPath}");
            return false;
        }

        return true;
    }

    private bool SelectHeadlessSubfolders(List<string> requestedNames, PackageExportResult result)
    {
        var requested = (requestedNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().Replace('\\', '/').TrimEnd('/'))
            .ToList();

        if (requested.Count > 0)
        {
            var unknown = new List<string>();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in requested)
            {
                // 名前またはフルパスの完全一致のみ。あいまい一致はしない
                string key = subFolderSelections.Keys.FirstOrDefault(folder =>
                    folder.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(folder).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (key == null)
                {
                    unknown.Add(name);
                }
                else
                {
                    matched.Add(key);
                }
            }

            if (unknown.Count > 0)
            {
                Fail(result,
                    "見つからないサブフォルダがあります: " + string.Join(", ", unknown)
                    + "。availableSubfolders から正確な名前を選んでください。");
                return false;
            }

            foreach (string key in subFolderSelections.Keys.ToList())
            {
                subFolderSelections[key] = matched.Contains(key);
            }
        }

        result.selectedSubfolders = subFolderSelections
            .Where(kvp => kvp.Value)
            .Select(kvp => Path.GetFileName(kvp.Key))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (result.selectedSubfolders.Count == 0)
        {
            Fail(result, "書き出すサブフォルダがありません: " + exportPath);
            return false;
        }

        return true;
    }

    private static string ResolveProjectRelativePath(string path)
    {
        string trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        string projectRootPath = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRootPath, trimmed));
    }

    private static PackageExportResult Fail(PackageExportResult result, string error)
    {
        result.success = false;
        result.errors.Add(error);
        result.message = error;
        return result;
    }
}
}
