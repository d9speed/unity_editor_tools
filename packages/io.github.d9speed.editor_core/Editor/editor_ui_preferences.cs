using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Public tool preferences; keep existing font keys for upgrades.
public static class D9speedCommonEditorPrefs
{
    private const string key_use_custom_font = "D9speed_Common_UseCustomUiFont";
    private const string key_font_path = "D9speed_Common_UiFontAssetPath";

    public static bool ShowPrefabOverrideIcon
    {
        get => EditorPrefs.GetBool("D9speed_Core_ShowPrefabOverrideIcon", true);
        set { EditorPrefs.SetBool("D9speed_Core_ShowPrefabOverrideIcon", value); EditorApplication.RepaintHierarchyWindow(); }
    }

    public static string AutoHotkeyExecutablePath
    {
        get => EditorPrefs.GetString("D9speed_Core_AutoHotkeyExecutablePath", string.Empty);
        set => EditorPrefs.SetString("D9speed_Core_AutoHotkeyExecutablePath", value ?? string.Empty);
    }

    public static string SakuraGrepScriptPath
    {
        get => EditorPrefs.GetString("D9speed_Core_SakuraGrepScriptPath", string.Empty);
        set => EditorPrefs.SetString("D9speed_Core_SakuraGrepScriptPath", value ?? string.Empty);
    }

    public static bool UseCustomUiFont
    {
        get => EditorPrefs.GetBool(key_use_custom_font, false);
        set => EditorPrefs.SetBool(key_use_custom_font, value);
    }

    public static string UiFontAssetPath
    {
        get => EditorPrefs.GetString(key_font_path, string.Empty);
        set => EditorPrefs.SetString(key_font_path, value ?? string.Empty);
    }
}

public static class D9speedEditorFontUtility
{
    public static Font GetConfiguredFont()
    {
        if (!D9speedCommonEditorPrefs.UseCustomUiFont) return null;
        var path = D9speedCommonEditorPrefs.UiFontAssetPath;
        return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<Font>(path);
    }

    public static void SetConfiguredFont(Font font)
    {
        D9speedCommonEditorPrefs.UiFontAssetPath = font == null ? string.Empty : AssetDatabase.GetAssetPath(font);
    }

    public static void Apply(VisualElement root)
    {
        if (root != null) root.style.unityFont = GetConfiguredFont();
    }
}

namespace D9speed_BaseEditorUtils
{
    internal static class EditorUiPreferences
    {
        [MenuItem("D9speed/Settings")]
        private static void OpenSettings() => SettingsService.OpenUserPreferences("Preferences/D9speed Tools");

        [SettingsProvider]
        private static SettingsProvider create_provider()
        {
            return new SettingsProvider("Preferences/D9speed Tools", SettingsScope.User)
            {
                keywords = new[] { "D9speed", "Font", "Prefab", "Override", "AutoHotkey", "Sakura", "フォント", "設定" },
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("Editor拡張のフォント", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var enabled = EditorGUILayout.Toggle("カスタムフォントを使用", D9speedCommonEditorPrefs.UseCustomUiFont);
                    var path = D9speedCommonEditorPrefs.UiFontAssetPath;
                    var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Font>(path);
                    font = (Font)EditorGUILayout.ObjectField("フォント", font, typeof(Font), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        D9speedCommonEditorPrefs.UseCustomUiFont = enabled;
                        D9speedEditorFontUtility.SetConfiguredFont(font);
                    }
                    EditorGUILayout.HelpBox("変更後、対象ツールを開き直してください。未指定の場合はUnity標準フォントを使います。", MessageType.Info);
                    if (GUILayout.Button("フォント指定を解除")) D9speedEditorFontUtility.SetConfiguredFont(null);

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var show_icon = EditorGUILayout.Toggle("Prefab変更マークを表示", D9speedCommonEditorPrefs.ShowPrefabOverrideIcon);
                    if (EditorGUI.EndChangeCheck()) D9speedCommonEditorPrefs.ShowPrefabOverrideIcon = show_icon;

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("サクラエディタ連携（Windows）", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var executable = DrawFilePath("AutoHotkey本体", D9speedCommonEditorPrefs.AutoHotkeyExecutablePath, "exe");
                    var script = DrawFilePath("Grep用AHKスクリプト", D9speedCommonEditorPrefs.SakuraGrepScriptPath, "ahk");
                    if (EditorGUI.EndChangeCheck())
                    {
                        D9speedCommonEditorPrefs.AutoHotkeyExecutablePath = executable;
                        D9speedCommonEditorPrefs.SakuraGrepScriptPath = script;
                    }
                    EditorGUILayout.HelpBox("AutoHotkey・サクラエディタ・Grep用スクリプトは別途用意してください。選択したアセットのフルパスをスクリプトの第1引数へ渡します。", MessageType.Info);
                }
            };
        }

        private static string DrawFilePath(string label, string value, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("参照", GUILayout.Width(48)))
                {
                    var selected = EditorUtility.OpenFilePanel(label, string.Empty, extension);
                    if (!string.IsNullOrEmpty(selected)) { value = selected; GUI.changed = true; }
                }
            }
            return value;
        }
    }
}
