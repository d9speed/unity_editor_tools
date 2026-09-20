using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    internal static class EditorExternalTools
    {
        [MenuItem("Assets/SakuraEditor_With_Grep")]
        private static void OpenSakuraGrep()
        {
            var path = EditorPathUtility.GetSelectedAssetPaths().FirstOrDefault();
            if (path == null) return;
            try
            {
                var info = CreateStartInfo(EditorPathUtility.GetFullAssetPath(path));
                using (Process.Start(info)) { }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("サクラエディタ連携を開始できません: " + error.Message);
                SettingsService.OpenUserPreferences("Preferences/D9speed Tools");
            }
        }

        [MenuItem("Assets/SakuraEditor_With_Grep", true)]
        private static bool CanOpenSakuraGrep() => Application.platform == RuntimePlatform.WindowsEditor
            && EditorPathUtility.GetSelectedAssetPaths().Length == 1;

        internal static ProcessStartInfo CreateStartInfo(string full_path)
        {
            var executable = D9speedCommonEditorPrefs.AutoHotkeyExecutablePath;
            var script = D9speedCommonEditorPrefs.SakuraGrepScriptPath;
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new InvalidOperationException("Windows専用の連携です。");
            if (!File.Exists(executable) || !File.Exists(script))
                throw new InvalidOperationException("PreferencesでAutoHotkey本体とGrep用スクリプトを指定してください。");
            if (!File.Exists(full_path) && !Directory.Exists(full_path))
                throw new FileNotFoundException("選択したアセットの実体が見つかりません。");
            return new ProcessStartInfo {
                FileName = executable, Arguments = "\"" + script + "\" \"" + full_path + "\"", UseShellExecute = false
            };
        }
    }
}
