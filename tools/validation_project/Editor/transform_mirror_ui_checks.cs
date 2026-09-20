using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class TransformMirrorUiChecks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static TransformMirrorTool window;
        private static IPanel test_panel;
        private static int frames;
        private static readonly List<string> checks = new List<string>();
        private static readonly List<string> errors = new List<string>();

        [Serializable] private sealed class Report { public bool passed; public string unity; public string[] checks; public string[] errors; }

        public static void Run()
        {
            Directory.CreateDirectory("Logs/transform_mirror_ui");
            Application.logMessageReceived += Log;
            window = ScriptableObject.CreateInstance<TransformMirrorTool>();
            window.titleContent = new GUIContent("Transform Mirror validation");
            window.position = new Rect(0, 0, 460, 700);
            window.CreateGUI();
            var panel_type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel", true);
            test_panel = (IPanel)panel_type.GetMethod("CreateEditorPanel", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { window });
            var editor_ui = typeof(Editor).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility", true);
            editor_ui.GetMethod("AddDefaultEditorStyleSheets", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { test_panel.visualTree });
            test_panel.visualTree.Add(window.rootVisualElement);
            Layout(window.rootVisualElement);
            EditorApplication.update += Tick;
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception ||
                (type == LogType.Warning && (message.Contains(".uss") || message.Contains("UIスタイル"))))
                errors.Add(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Tick()
        {
            if (++frames < 20) { window.Repaint(); return; }
            EditorApplication.update -= Tick;
            var drag_objects = DragAndDrop.objectReferences;
            GameObject source = null;
            GameObject clone = null;
            TextAsset invalid = null;
            try
            {
                var root = window.rootVisualElement;
                var theme_sheet_count = Enumerable.Range(0, root.styleSheets.count).Count(i => AssetDatabase.GetAssetPath(root.styleSheets[i]).Contains("/Editor/ui/"));
                Require(theme_sheet_count == 4 && root.panel != null, "Style sheets or live Editor panel missing: " + theme_sheet_count + ", total=" + root.styleSheets.count + ", panel=" + (root.panel != null));
                var initial_sheet_count = root.styleSheets.count;
                var execute = root.Q<Button>("create_mirror");
                var clear = root.Q<Button>("clear_targets");
                var pivot_toggle = root.Q<Toggle>("use_custom_pivot");
                var pivot = root.Q<Vector3Field>("custom_pivot");
                Require(!execute.enabledSelf && !clear.enabledSelf && !pivot.enabledSelf, "Initial disabled states");
                Require(root.Query<Toggle>().ToList().Count(t => t.value) == 2, "Default toggle states");
                checks.Add("Empty targets, default options and disabled fields");

                source = new GameObject("mirror_ui_probe");
                source.transform.position = new Vector3(4, 5, 6);
                source.transform.rotation = Quaternion.Euler(15, 30, 45);
                invalid = new TextAsset("ignored");
                DragAndDrop.objectReferences = new Object[] { source, invalid };
                var target_list = root.Q<ListView>("mirror_targets");
                using (var update = DragUpdatedEvent.GetPooled()) { update.target = target_list; target_list.SendEvent(update); }
                Require(target_list.ClassListContains("d9_drop_active"), "Drop highlight missing");
                using (var cancel = DragExitedEvent.GetPooled()) { cancel.target = target_list; target_list.SendEvent(cancel); }
                Require(!target_list.ClassListContains("d9_drop_active"), "Cancelled drag left highlight");
                using (var drop = DragPerformEvent.GetPooled())
                {
                    var list = root.Q<ListView>("mirror_targets");
                    drop.target = list;
                    list.SendEvent(drop);
                }
                Require(execute.enabledSelf && clear.enabledSelf && root.Q<Label>("target_count").text == "1 件", "Drop filtering / target count");
                pivot_toggle.value = true;
                pivot.value = new Vector3(1, 2, 3);
                root.Q<Toggle>("mirror_rotation").value = false;
                Require(pivot.enabledSelf, "Custom pivot should be enabled");
                window.CreateGUI();
                Require(root.Q<Toggle>("use_custom_pivot").value && !root.Q<Toggle>("mirror_rotation").value && root.Q<Vector3Field>("custom_pivot").value == new Vector3(1, 2, 3), "Rebuild lost options");
                Require(root.styleSheets.count == initial_sheet_count && root.Q<Button>("create_mirror").enabledSelf, "Rebuild duplicated styles or lost targets");
                checks.Add("GameObject drop filtering and UI rebuild preserve targets/options");

                Undo.IncrementCurrentGroup();
                Submit(root.Q<Button>("create_mirror"));
                clone = GameObject.Find("mirror_ui_probe_Mirrored");
                Require(clone != null && clone.transform.position == new Vector3(-2, 5, 6), "Button did not use custom pivot");
                Require(Quaternion.Angle(clone.transform.rotation, source.transform.rotation) < 0.001f, "Rotation toggle was ignored");
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                Require(clone == null && source.transform.position == new Vector3(4, 5, 6), "Undo changed source or left clone");
                checks.Add("Keyboard submit reaches mirror operation; pivot, rotation and Undo work");
                window.CreateGUI();

                foreach (var dark in new[] { false, true })
                {
                    var editor_ui = typeof(Editor).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility", true);
                    var native_sheet = (StyleSheet)editor_ui.GetMethod(dark ? "GetCommonDarkStyleSheet" : "GetCommonLightStyleSheet", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                    test_panel.visualTree.styleSheets.Clear();
                    test_panel.visualTree.styleSheets.Add(native_sheet);
                    root.EnableInClassList("d9_theme_light", !dark);
                    root.EnableInClassList("d9_theme_dark", dark);
                    Layout(root);
                    var expected = dark ? new Color32(31,31,31,255) : new Color32(245,245,245,255);
                    Require(ColorDistance(root.resolvedStyle.backgroundColor, expected) < 0.02f, "Theme aliases did not resolve");
                    Require(root.Q<Button>("create_mirror").resolvedStyle.borderTopLeftRadius == 4, "Control radius token did not resolve");
                    SaveLayout(root, dark ? "dark" : "light");
                    Capture(root, dark ? "dark" : "light");
                }
                checks.Add("Light/dark style imports, semantic variables and layout");

                window.position = new Rect(0, 0, 340, 380);
                Layout(root);
                var scroll = root.Q<ScrollView>("mirror_scroll");
                Require(scroll.contentContainer.layout.height > scroll.layout.height, "Narrow window did not scroll");
                Require(root.Q<Button>("create_mirror").worldBound.xMax <= root.worldBound.xMax + 1, "Action clips horizontally");
                SaveLayout(root, "narrow");
                Capture(root, "narrow");
                checks.Add("Narrow docking keeps scrolling body and visible action");

                Submit(root.Q<Button>("clear_targets"));
                Require(!root.Q<Button>("create_mirror").enabledSelf && root.Q<Label>("target_count").text == "0 件" && source != null, "Clear changed scene or did not reset controls");
                checks.Add("Clear only resets the list and disabled state");
                window.position = new Rect(0, 0, 460, 740);
                window.CreateGUI();
                root.Q<Toggle>("use_custom_pivot").value = false;
                root.Q<Vector3Field>("custom_pivot").value = Vector3.zero;
                root.Q<Toggle>("mirror_rotation").value = true;
                Layout(root);
                SaveLayout(root, "empty");
                Capture(root, "empty");
            }
            catch (Exception error) { errors.Add(error.ToString()); }
            finally
            {
                DragAndDrop.objectReferences = drag_objects;
                if (clone != null) Object.DestroyImmediate(clone);
                if (source != null) Object.DestroyImmediate(source);
                if (invalid != null) Object.DestroyImmediate(invalid);
                test_panel.Dispose();
                Object.DestroyImmediate(window);
                Application.logMessageReceived -= Log;
                var report = new Report { passed = errors.Count == 0, unity = Application.unityVersion, checks = checks.ToArray(), errors = errors.ToArray() };
                File.WriteAllText("Logs/transform_mirror_ui/checks.json", JsonUtility.ToJson(report, true));
                EditorApplication.Exit(report.passed ? 0 : 1);
            }
        }

        private static float ColorDistance(Color a, Color b) => Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b);

        private static void Submit(Button button)
        {
            button.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private static void Layout(VisualElement root)
        {
            root.panel.visualTree.style.width = window.position.width;
            root.panel.visualTree.style.height = window.position.height;
            root.panel.GetType().GetMethod("ValidateLayout", flags).Invoke(root.panel, null);
        }

        private static void SaveLayout(VisualElement root, string name)
        {
            var lines = new List<string>();
            void Visit(VisualElement element, int depth)
            {
                lines.Add(new string(' ', depth * 2) + element.GetType().Name + " " + element.name + " [" + string.Join(" ", element.GetClasses()) + "] " + element.worldBound + (element is TextElement text ? " " + text.text : string.Empty));
                foreach (var child in element.hierarchy.Children()) Visit(child, depth + 1);
            }
            Visit(root, 0);
            File.WriteAllLines("Logs/transform_mirror_ui/" + name + "_layout.txt", lines);
        }

        private static void Capture(VisualElement root, string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var width = Mathf.RoundToInt(window.position.width);
            var height = Mathf.RoundToInt(window.position.height);
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.magenta);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, width, height, 0);
                    root.panel.GetType().GetMethod("Repaint", flags).Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                }
                finally { GL.PopMatrix(); }
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes("Logs/transform_mirror_ui/" + name + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); }
        }
    }
}
