using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speedBaseEditorUtil;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class ExportRenameFluentChecks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly List<string> checks = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static EditorWindow window;
        private static IPanel panel;
        private static int frames;
        private static int scenario;
        private static GameObject rename_fixture;
        private static string output = "Logs/export_rename_fluent";
        [Serializable] private sealed class Report { public bool passed; public string unity; public string[] checks; public string[] errors; }

        public static void Run()
        {
            Directory.CreateDirectory(output);
            Application.logMessageReceived += Log;
            ToolsSmokeChecks.Run((name, action) => { if (name.StartsWith("Rename") || name.StartsWith("Hierarchy") || name.StartsWith("Exporter")) Check(name, action); });
            ExporterCompareSmokeChecks.Run(Check);
            Check("Japanese live preview, selected rows, invalid regex and scope state", () =>
            {
                var rename = ScriptableObject.CreateInstance<RenameToolWindow>();
                var go = new GameObject("old_name");
                var previous = Selection.objects;
                IPanel test_panel = null;
                try
                {
                    Call(rename, "CreateGUI");
                    test_panel = (IPanel)typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel", true).GetMethod("CreateEditorPanel", flags).Invoke(null, new object[] { rename });
                    test_panel.visualTree.Add(rename.rootVisualElement);
                    Selection.activeGameObject = go;
                    ((Toggle)Get(rename, "_hierarchySelection")).value = true;
                    Require(!((ObjectField)Get(rename, "_folderField")).enabledSelf, "Hierarchy scope field state");
                    ((TextField)Get(rename, "_replaceText")).value = "new";
                    ((TextField)Get(rename, "_findText")).value = "old";
                    var items = (List<RenamePreviewItem>)Get(rename, "_previewItems");
                    Require(items.Count == 1 && items[0].newName == "new_name", "Live preview");
                    var apply = (Button)Get(rename, "_applyButton");
                    Require(apply.enabledSelf && apply.text.Contains("1 件"), "Apply count");
                    Call(rename, "SetAllSelected", false);
                    Require(!apply.enabledSelf && !items[0].selected, "Deselect all");
                    Call(rename, "SetAllSelected", true);
                    Require(apply.enabledSelf && items[0].selected, "Select all");
                    ((Toggle)Get(rename, "_useRegex")).value = true;
                    ((TextField)Get(rename, "_findText")).value = "[";
                    Require(!apply.enabledSelf && !((HelpBox)Get(rename, "_regexWarning")).ClassListContains("d9_hidden"), "Regex warning");
                    Require(go.name == "old_name", "Preview mutated object");
                }
                finally { Selection.objects = previous; test_panel?.Dispose(); Object.DestroyImmediate(rename); Object.DestroyImmediate(go); }
            });
            scenario = 0;
            OpenScenario();
            EditorApplication.update += Tick;
        }

        private static void OpenScenario()
        {
            frames = 0;
            int tool = scenario % 3;
            bool narrow = scenario >= 6;
            if (tool == 0)
            {
                window = ScriptableObject.CreateInstance<PackageExporter>();
                window.position = new Rect(0, 0, narrow ? 460 : 640, narrow ? 480 : 880);
                Call(window, "CreateGUI");
            }
            else if (tool == 1)
            {
                window = ScriptableObject.CreateInstance<PackageComponentReportCompareWindow>();
                var fixture = Directory.GetDirectories("Logs", "compare_fixture_*").OrderByDescending(Directory.GetLastWriteTimeUtc).First();
                Set(window, "reference_json_path", Path.GetFullPath(fixture + "/reference_components.json"));
                Set(window, "target_json_paths", new List<string> { Path.GetFullPath(fixture + "/target_components.json") });
                window.position = new Rect(0, 0, narrow ? 760 : 1100, narrow ? 600 : 800);
                Call(window, "CreateGUI");
            }
            else
            {
                window = ScriptableObject.CreateInstance<RenameToolWindow>();
                window.position = new Rect(0, 0, narrow ? 680 : 920, narrow ? 600 : 860);
                Call(window, "CreateGUI");
                rename_fixture = new GameObject("Avatar_old_body");
                Selection.activeGameObject = rename_fixture;
                ((Toggle)Get(window, "_hierarchySelection")).value = true;
                ((TextField)Get(window, "_replaceText")).value = "new";
                ((TextField)Get(window, "_findText")).value = "old";
            }
            var panel_type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel", true);
            panel = (IPanel)panel_type.GetMethod("CreateEditorPanel", flags).Invoke(null, new object[] { window });
            var editor_ui = typeof(Editor).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility", true);
            bool dark = scenario < 3 || scenario >= 6;
            var native = (StyleSheet)editor_ui.GetMethod(dark ? "GetCommonDarkStyleSheet" : "GetCommonLightStyleSheet", flags).Invoke(null, null);
            panel.visualTree.styleSheets.Add(native);
            panel.visualTree.Add(window.rootVisualElement);
            if (tool == 2)
            {
                Call(window, "UpdateScopeFieldStates");
                Call(window, "RefreshPreviewLive");
            }
            window.rootVisualElement.EnableInClassList("d9_theme_light", !dark);
            window.rootVisualElement.EnableInClassList("d9_theme_dark", dark);
            Layout();
        }

        private static void Tick()
        {
            if (++frames < 25) { window.Repaint(); return; }
            try
            {
                Layout();
                string name = new[] { "exporter", "compare", "rename" }[scenario % 3] + (scenario < 3 ? "_dark" : scenario < 6 ? "_light" : "_narrow");
                Check(name + " layout and controls", () => ValidateLayout(scenario % 3));
                SaveLayout(name);
                Capture(name);
                if (scenario % 3 == 1) Check("Comparer table selection, numeric sort, filter and width persistence " + scenario, CompareInteraction);
                if (scenario % 3 == 2) Check("Preview row toggle changes apply count " + scenario, RenameInteraction);
                panel.Dispose();
                Object.DestroyImmediate(window);
                if (rename_fixture != null) Object.DestroyImmediate(rename_fixture);
                if (++scenario < 9) { OpenScenario(); return; }
            }
            catch (Exception error) { errors.Add(error.ToString()); }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            var report = new Report { passed = errors.Count == 0, unity = Application.unityVersion, checks = checks.ToArray(), errors = errors.ToArray() };
            File.WriteAllText(output + "/checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void ValidateLayout(int tool)
        {
            var root = window.rootVisualElement;
            Require(root.ClassListContains("d9_ui_root") && root.panel != null, "Theme/panel missing");
            bool dark = scenario < 3 || scenario >= 6;
            var color = root.resolvedStyle.backgroundColor;
            Require(Math.Abs(color.r - (dark ? 31f : 245f) / 255f) < 0.02f, "Theme mismatch");
            var table = root.Q<MultiColumnListView>();
            if (table != null)
            {
                Require(table.worldBound.height >= 159 && table.worldBound.yMax <= root.worldBound.yMax + 1, "Table clipped");
                Require(table.Q<Label>(className: "d9_table_cell") != null, "Virtualized cells missing");
            }
            var action = root.Q<Button>(tool == 0 ? "start_export" : "apply_renames");
            if (action != null) Require(action.worldBound.yMax <= root.worldBound.yMax + 1 && action.worldBound.xMax <= root.worldBound.xMax + 1, "Footer clips");
            if (tool == 0)
            {
                var scroll = root.Q<ScrollView>("export_settings");
                Require(scroll.contentContainer.layout.height > scroll.layout.height, "Exporter does not scroll");
            }
        }

        private static void CompareInteraction()
        {
            var table = window.rootVisualElement.Q<MultiColumnListView>();
            table.SetSelection(0);
            Require((int)Get(window, "selected_row") >= 0, "Selection");
            table.sortColumnDescriptions.Clear();
            table.sortColumnDescriptions.Add(new SortColumnDescription(3, SortDirection.Descending));
            Require((int)Get(window, "sort_column") == 3 && (bool)Get(window, "sort_descending"), "Header sorting");
            table.columns[2].width = 245;
            Call(window, "ReloadReports");
            Require(table.columns[2].width.value == 245, "Column width after reload");
            window.rootVisualElement.Q<TextField>("report_filter").value = "Root/Right";
            Require(table.itemsSource.Count == 1, "Live filter");
        }

        private static void RenameInteraction()
        {
            var table = window.rootVisualElement.Q<MultiColumnListView>();
            var toggle = table.Q<Toggle>(className: "d9_table_check");
            Require(toggle != null && toggle.value, "Row checkbox");
            toggle.value = false;
            Require(!window.rootVisualElement.Q<Button>("apply_renames").enabledSelf, "Row checkbox did not disable apply");
            Require(rename_fixture.name == "Avatar_old_body", "Selection mutated fixture");
        }

        private static void Layout()
        {
            panel.visualTree.style.width = window.position.width;
            panel.visualTree.style.height = window.position.height;
            panel.GetType().GetMethod("ValidateLayout", flags).Invoke(panel, null);
        }
        private static void SaveLayout(string name)
        {
            var lines = new List<string>();
            void Visit(VisualElement element, int depth)
            {
                lines.Add(new string(' ', depth * 2) + element.GetType().Name + " " + element.name + " [" + string.Join(" ", element.GetClasses()) + "] " + element.worldBound + (element is TextElement text ? " " + text.text : ""));
                foreach (var child in element.hierarchy.Children()) Visit(child, depth + 1);
            }
            Visit(window.rootVisualElement, 0);
            File.WriteAllLines(output + "/" + name + "_layout.txt", lines);
        }
        private static void Capture(string name)
        {
            int width = (int)window.position.width, height = (int)window.position.height;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.magenta);
                GL.PushMatrix();
                try { GL.LoadPixelMatrix(0, width, height, 0); panel.GetType().GetMethod("Repaint", flags).Invoke(panel, new object[] { new Event { type = EventType.Repaint } }); }
                finally { GL.PopMatrix(); }
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(output + "/" + name + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
        }
        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || (type == LogType.Warning && (message.Contains(".uss") || message.Contains("UIスタイル")))) errors.Add(message);
        }
        private static void Check(string name, Action action) { try { action(); checks.Add(name); } catch (Exception error) { errors.Add(name + ": " + error); } }
        private static object Get(object obj, string name) => obj.GetType().GetField(name, flags).GetValue(obj);
        private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, flags).SetValue(obj, value);
        private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, flags).Invoke(obj, args);
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
