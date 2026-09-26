using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed.PackageValidation
{
    public static class guide_board_no_tmp_checks
    {
        public static void run_without_resources()
        {
            EditorWindow window = null;
            try
            {
                if (Resources.Load("TMP Settings") != null) throw new Exception("This check requires no TMP Essential Resources.");
                var type = TypeCache.GetTypesDerivedFrom<EditorWindow>().Single(t => t.FullName == "D9speed_BaseEditorUtils.GuideBoard.guide_board_window");
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                type.GetMethod("CreateGUI").Invoke(window, null);
                type.GetMethod("render_preview", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                if (window.rootVisualElement.Q<Button>("prefab_button").enabledSelf || window.rootVisualElement.Q<Button>("export_button").enabledSelf)
                    throw new Exception("Export must be disabled without a font.");
                if (!window.rootVisualElement.Q<Label>("status_label").text.Contains("Import TMP Essential Resources"))
                    throw new Exception("Import guidance is missing.");
                UnityEngine.Object.DestroyImmediate(window);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/guide_board_no_resources_result.txt", "PASS\nUnity " + Application.unityVersion + "\nWindow opens without TMP Essential Resources; import guidance is visible and export is disabled.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void run()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                if (assemblies.Any(a => a.GetName().Name == "D9speed.EditorUtils.GuideBoard")) throw new Exception("Optional assembly was loaded without TMP.");
                var core = assemblies.Single(a => a.GetName().Name == "D9speed.EditorUtils");
                var menu_type = core.GetType("D9speed_BaseEditorUtils.guide_board_menu", true);
                var menu = menu_type.GetMethod("open_window", BindingFlags.NonPublic | BindingFlags.Static).GetCustomAttribute<MenuItem>();
                if (menu.menuItem != "D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)") throw new Exception("Menu path mismatch.");
                if (core.GetType("D9speed_BaseEditorUtils.EditorUiTheme") == null) throw new Exception("Core helper is missing.");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/guide_board_no_tmp_result.txt", "PASS\nUnity " + UnityEngine.Application.unityVersion + "\nCore compiled; optional TMP assembly excluded; menu and core helpers available.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
