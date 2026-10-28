using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

namespace D9speedBaseEditorUtil
{
/// <summary>
/// Unityのコマンドライン（-batchmode -executeMethod）から呼ぶ入口。
/// 例: Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod D9speedBaseEditorUtil.PackageExporterCommandLine.Run
///       -d9RequestFile request.json -d9ResultFile result.json
/// 依頼は -d9Request '{"mode":"plan",...}' でも渡せる。終了コードは成功 0、失敗 1
/// </summary>
public static class PackageExporterCommandLine
{
    private const string request_file_arg = "-d9RequestFile";
    private const string request_json_arg = "-d9Request";
    private const string result_file_arg = "-d9ResultFile";

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string requestJson = GetArgValue(args, request_json_arg);
        string requestFile = GetArgValue(args, request_file_arg);
        string resultFile = GetArgValue(args, result_file_arg);

        if (string.IsNullOrEmpty(requestJson) && !string.IsNullOrEmpty(requestFile))
        {
            requestJson = File.ReadAllText(requestFile);
        }

        PackageExportResult result = PackageExporterHeadless.RunJsonToResult(requestJson);
        string resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);
        Debug.Log("[PackageExporter CLI] result\n" + resultJson);
        if (!string.IsNullOrEmpty(resultFile))
        {
            File.WriteAllText(resultFile, resultJson);
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(result.success ? 0 : 1);
        }
    }

    private static string GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
}
