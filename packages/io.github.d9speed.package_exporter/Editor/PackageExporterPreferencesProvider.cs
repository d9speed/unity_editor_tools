using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace D9speedBaseEditorUtil
{
/// <summary>
/// Edit > Preferences > D9speed > Package Exporter に許可フォルダの設定を表示する
/// </summary>
public static class PackageExporterPreferencesProvider
{
    public const string PreferencesPath = "Preferences/D9speed/Package Exporter";

    [SettingsProvider]
    public static SettingsProvider CreateProvider()
    {
        return new SettingsProvider(PreferencesPath, SettingsScope.User)
        {
            label = "Package Exporter",
            keywords = new HashSet<string> { "D9speed", "Package Exporter", "unitypackage", "Assets/D9speed" },
            activateHandler = (searchContext, root) =>
            {
                root.style.paddingLeft = 10;
                root.style.paddingTop = 6;
                root.style.paddingRight = 10;

                root.Add(new Label("Package Exporter") { style = { fontSize = 19, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });

                var field = new TextField("許可するアセットフォルダ")
                {
                    value = PackageExportAssetRootGuard.GetAllowedRootsSetting(),
                    multiline = false
                };
                field.isDelayed = true;
                root.Add(field);

                var normalizedLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } };
                root.Add(normalizedLabel);

                void UpdateNormalizedLabel(string value)
                {
                    string formatted = PackageExportAssetRootGuard.FormatRoots(value);
                    normalizedLabel.text = string.IsNullOrEmpty(formatted)
                        ? "未設定: フォルダ外アセットの確認を行いません。"
                        : "確認対象: " + formatted;
                }

                field.RegisterValueChangedCallback(evt =>
                {
                    PackageExportAssetRootGuard.SetAllowedRootsSetting(evt.newValue);
                    UpdateNormalizedLabel(evt.newValue);
                });
                UpdateNormalizedLabel(field.value);

                root.Add(new HelpBox(
                    "エクスポート対象にここで指定したフォルダ以外のアセットが含まれると、画面では確認ダイアログを出し、CLIでは書き出しを止めます。"
                    + "複数指定は ; で区切ります（例: Assets/D9speed/; Assets/D9speed_Shared/）。"
                    + "設定はこのPCのEditorPrefsに保存され、全プロジェクトで共通です。",
                    HelpBoxMessageType.Info) { style = { marginTop = 8 } });

                var resetButton = new Button(() =>
                {
                    PackageExportAssetRootGuard.SetAllowedRootsSetting(PackageExportAssetRootGuard.DefaultAllowedRoots);
                    field.SetValueWithoutNotify(PackageExportAssetRootGuard.DefaultAllowedRoots);
                    UpdateNormalizedLabel(PackageExportAssetRootGuard.DefaultAllowedRoots);
                }) { text = "初期値に戻す" };
                resetButton.style.alignSelf = Align.FlexStart;
                resetButton.style.marginTop = 6;
                root.Add(resetButton);
            }
        };
    }
}
}
