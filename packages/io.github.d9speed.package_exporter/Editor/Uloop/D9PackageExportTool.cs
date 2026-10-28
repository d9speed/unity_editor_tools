using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace D9speedBaseEditorUtil.Uloop
{
/// <summary>
/// list: サブフォルダ（アバター）一覧 / plan: 書き出さずに確認 / export: 書き出す
/// </summary>
public enum D9PackageExportMode
{
    plan,
    list,
    export
}

public class D9PackageExportSchema : UnityCliLoopToolSchema
{
    [Description("list = show subfolders (avatars), plan = dry run without writing files (default), export = write .unitypackage files")]
    public D9PackageExportMode Mode { get; set; } = D9PackageExportMode.plan;

    [Description("Export profile JSON path (absolute or project-relative). Empty = current ExportBatch settings")]
    public string ProfilePath { get; set; } = "";

    [Description("Source folder whose direct subfolders are exported one package each, e.g. Assets/D9speed/Outfits/Shirt. Empty = profile/settings")]
    public string ExportPath { get; set; } = "";

    [Description("Output folder for .unitypackage files. Empty = profile/settings")]
    public string OutPath { get; set; } = "";

    [Description("Package name prefix. Empty = profile/settings")]
    public string PackageName { get; set; } = "";

    [Description("Subfolder (avatar) names to export, exact match. Empty = profile selection or all")]
    public string[] Subfolders { get; set; } = new string[0];

    [Description("Overwrite existing .unitypackage files")]
    public bool Overwrite { get; set; } = false;

    [Description("Export even when assets outside the allowed root are included. Only after the user approved")]
    public bool AllowOutsideRoot { get; set; } = false;

    [Description("Allowed asset roots separated by ';'. Empty = Preferences > D9speed > Package Exporter")]
    public string AllowedRoot { get; set; } = "";

    [Description("Include every asset path per package in the response")]
    public bool ListAssets { get; set; } = false;
}

public class D9PackageExportResponse : UnityCliLoopToolResponse
{
    public string Mode { get; set; }
    public string Message { get; set; }
    public string ProfilePath { get; set; }
    public string ExportPath { get; set; }
    public string OutPath { get; set; }
    public string AllowedRoot { get; set; }
    public List<string> AvailableSubfolders { get; set; } = new List<string>();
    public List<string> SelectedSubfolders { get; set; } = new List<string>();
    public List<PackageExportItemResult> Packages { get; set; } = new List<PackageExportItemResult>();
    public List<string> Warnings { get; set; } = new List<string>();
    public List<string> Errors { get; set; } = new List<string>();

    public D9PackageExportResponse()
    {
    }
}

/// <summary>
/// uloop d9-package-export。処理本体は PackageExporterHeadless にあり、ここは引数の変換だけを行う
/// </summary>
[UnityCliLoopTool]
public class D9PackageExportTool : UnityCliLoopTool<D9PackageExportSchema, D9PackageExportResponse>
{
    public override string ToolName => "d9-package-export";

    protected override Task<D9PackageExportResponse> ExecuteAsync(D9PackageExportSchema parameters, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var request = new PackageExportRequest
        {
            mode = parameters.Mode.ToString(),
            profilePath = parameters.ProfilePath ?? "",
            exportPath = parameters.ExportPath ?? "",
            outPath = parameters.OutPath ?? "",
            packageName = parameters.PackageName ?? "",
            allowedRoot = parameters.AllowedRoot ?? "",
            subfolders = (parameters.Subfolders ?? new string[0]).ToList(),
            overwrite = parameters.Overwrite,
            allowOutsideRoot = parameters.AllowOutsideRoot,
            listAssets = parameters.ListAssets
        };

        PackageExportResult result = PackageExporterHeadless.Run(request);
        var response = new D9PackageExportResponse
        {
            Success = result.success,
            Mode = result.mode,
            Message = result.message,
            ProfilePath = result.profilePath,
            ExportPath = result.exportPath,
            OutPath = result.outPath,
            AllowedRoot = result.allowedRoot,
            AvailableSubfolders = result.availableSubfolders,
            SelectedSubfolders = result.selectedSubfolders,
            Packages = result.packages,
            Warnings = result.warnings,
            Errors = result.errors
        };
        return Task.FromResult(response);
    }
}
}
