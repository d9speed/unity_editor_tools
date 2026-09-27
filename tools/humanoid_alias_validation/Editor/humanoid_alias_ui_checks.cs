using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace D9speed.HumanoidAliasValidation
{
    public static class humanoid_alias_ui_checks
    {
        private const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string output = "Logs/humanoid_alias_ui";
        private static readonly List<string> checks = new List<string>(), errors = new List<string>();
        private static fixture current;
        private static IPanel panel;
        private static int scenario, frames;
        [Serializable] private class report { public bool passed; public string unity; public string[] checks, errors; }
        private static object get(object obj, string name) => obj.GetType().GetField(name, flags).GetValue(obj);
        private static object call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, flags).Invoke(obj, args);
        private static object group(string name) => Enum.Parse(typeof(HumanoidAliasComponentCopierWindow).GetNestedType("copy_group", flags), name);
        private static Type find_type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        private static void require(bool value, string message) { if (!value) throw new Exception(message); }
        private static void check(string name, Action action) { try { action(); checks.Add(name); } catch (Exception e) { errors.Add(name + ": " + e); } }
        private static GameObject child(GameObject root, string name) { var obj = new GameObject(name); obj.transform.SetParent(root.transform, false); return obj; }

        private sealed class fixture : IDisposable
        {
            public readonly GameObject source = new GameObject("Standard_Maid_Lime"), target = new GameObject("Standard_Maid_Rurune");
            public readonly HumanoidAliasComponentCopierWindow window;
            public readonly ParentConstraint source_constraint, target_constraint;
            public readonly RotationConstraint source_rotation, target_rotation;
            public readonly SphereCollider target_collider;
            public readonly SkinnedMeshRenderer source_skin, target_skin;
            public readonly Material source_material, target_material;
            public Component source_pb, target_pb, source_pb_collider, target_pb_collider, source_ma, target_ma;
            public fixture()
            {
                var source_anchor = child(source, "for_Courtesy_L"); var target_anchor = child(target, "for_Courtesy_L");
                source_constraint = source_anchor.AddComponent<ParentConstraint>(); source_constraint.weight = 0.6f;
                target_constraint = target_anchor.AddComponent<ParentConstraint>(); target_constraint.weight = 0.1f;
                source_rotation = source_anchor.AddComponent<RotationConstraint>(); source_rotation.weight = 0.8f;
                target_rotation = target_anchor.AddComponent<RotationConstraint>(); target_rotation.weight = 0.2f;
                child(source, "common_collider").AddComponent<SphereCollider>().radius = 0.23f;
                target_collider = child(target, "common_collider").AddComponent<SphereCollider>(); target_collider.radius = 0.7f;
                source_material = new Material(Shader.Find("Standard")); target_material = new Material(Shader.Find("Standard"));
                source_skin = child(source, "maid_mesh").AddComponent<SkinnedMeshRenderer>(); source_skin.sharedMaterial = source_material;
                target_skin = child(target, "maid_mesh").AddComponent<SkinnedMeshRenderer>(); target_skin.sharedMaterial = target_material;
                var pb_type = find_type("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
                var collider_type = find_type("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
                if (pb_type != null && collider_type != null)
                {
                    source_pb_collider = child(source, "Cloth_Collider").AddComponent(collider_type);
                    target_pb_collider = child(target, "Cloth_Collider").AddComponent(collider_type);
                    collider_type.GetField("radius").SetValue(source_pb_collider, 0.3f); collider_type.GetField("radius").SetValue(target_pb_collider, 0.1f);
                    foreach (var name in new[] { "VRCPhysbone", "VRCPhysbone_Courtesy_L", "VRCPhysbone_Courtesy_R", "HandCuffs", "Skirt_front", "Skirt_back" })
                    {
                        var s = child(source, name).AddComponent(pb_type); var t = child(target, name).AddComponent(pb_type);
                        ((IList)pb_type.GetField("colliders").GetValue(s)).Add(source_pb_collider);
                        if (source_pb == null) { source_pb = s; target_pb = t; }
                    }
                }
                var constraint_type = find_type("VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint");
                if (constraint_type != null) { source_anchor.AddComponent(constraint_type); target_anchor.AddComponent(constraint_type); }
                var ma_type = find_type("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator");
                if (ma_type != null)
                {
                    source_ma = source.AddComponent(ma_type); target_ma = target.AddComponent(ma_type);
                    ma_type.GetField("layerPriority").SetValue(source_ma, 23); ma_type.GetField("layerPriority").SetValue(target_ma, 4);
                }
                window = ScriptableObject.CreateInstance<HumanoidAliasComponentCopierWindow>();
                ((ObjectField)get(window, "sourceField")).SetValueWithoutNotify(source);
                ((ObjectField)get(window, "targetField")).SetValueWithoutNotify(target);
                call(window, "Scan");
            }
            public void select(string name) => call(window, "select_copy_tab", group(name));
            public void copy() => call(window, "ExecuteCopy");
            public float pb_radius => target_pb_collider == null ? 0 : (float)target_pb_collider.GetType().GetField("radius").GetValue(target_pb_collider);
            public int ma_priority => target_ma == null ? 0 : (int)target_ma.GetType().GetField("layerPriority").GetValue(target_ma);
            public void Dispose() { Object.DestroyImmediate(window); Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(source_material); Object.DestroyImmediate(target_material); Undo.ClearAll(); }
        }

        public static void run()
        {
            Directory.CreateDirectory(output);
            Application.logMessageReceived += log;
            check("Exactly three tabs, separate common copy, classification and resizable columns", () =>
            {
                using (var f = new fixture())
                {
                    var tabs = f.window.rootVisualElement.Q("copy_tabs"); require(tabs.childCount == 3, "Tab count");
                    require(f.window.rootVisualElement.Q<Button>("copy_common") != null, "Common copy missing");
                    foreach (var category in new[] { "physbone", "constraint", "modular_avatar" })
                    {
                        f.select(category);
                        var table = (MultiColumnListView)get(f.window, "copyListView");
                        foreach (var row in table.itemsSource)
                        {
                            var component = (Component)get(row, "SourceComponent");
                            if (component != null) require(call(f.window, "get_copy_group", component).ToString() == category, "Mixed category");
                        }
                        table.columns[2].width = 250; call(f.window, "Scan"); require(table.columns[2].width.value == 250, "Column width reset");
                    }
                }
            });
            check("Constraint copy changes checked rows only; other tabs, materials and common colliders stay unchanged; Undo", () =>
            {
                using (var f = new fixture())
                {
                    ((HashSet<Component>)get(f.window, "excluded_components")).Add(f.source_rotation); f.select("constraint"); f.copy();
                    require(Mathf.Approximately(f.target_constraint.weight, 0.6f) && Mathf.Approximately(f.target_rotation.weight, 0.2f), "Constraint selection ignored");
                    require(Mathf.Approximately(f.target_collider.radius, 0.7f) && f.target_skin.sharedMaterial == f.target_material, "Common values changed");
                    if (f.target_pb != null) require(Mathf.Approximately(f.pb_radius, 0.1f), "PhysBone changed");
                    if (f.target_ma != null) require(f.ma_priority == 4, "MA changed");
                    Undo.PerformUndo(); require(Mathf.Approximately(f.target_constraint.weight, 0.1f), "Constraint Undo failed");
                }
            });
            check("Missing hierarchy is created only for checked components in the active tab", () =>
            {
                using (var f = new fixture())
                {
                    child(f.source, "new_constraint").AddComponent<ParentConstraint>();
                    var excluded = child(f.source, "unchecked_constraint").AddComponent<ParentConstraint>();
                    child(f.source, "new_common").AddComponent<BoxCollider>();
                    ((HashSet<Component>)get(f.window, "excluded_components")).Add(excluded);
                    f.select("constraint"); f.copy();
                    require(f.target.transform.Find("new_constraint") != null, "Selected hierarchy missing");
                    require(f.target.transform.Find("unchecked_constraint") == null && f.target.transform.Find("new_common") == null, "Unselected hierarchy created");
                }
            });
            check("Common copy changes only selected common components and materials", () =>
            {
                using (var f = new fixture())
                {
                    call(f.window, "execute_group_copy", group("other"));
                    require(Mathf.Approximately(f.target_collider.radius, 0.23f) && f.target_skin.sharedMaterial == f.source_material, "Common values not copied");
                    require(Mathf.Approximately(f.target_constraint.weight, 0.1f), "Constraint changed");
                    if (f.target_pb != null) require(Mathf.Approximately(f.pb_radius, 0.1f), "PhysBone changed");
                    if (f.target_ma != null) require(f.ma_priority == 4, "MA changed");
                    Undo.PerformUndo(); require(Mathf.Approximately(f.target_collider.radius, 0.7f) && f.target_skin.sharedMaterial == f.target_material, "Common Undo failed");
                }
            });
            if (find_type("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone") != null)
                check("PhysBone tab copies colliders and remaps references without copying other categories", () =>
                {
                    using (var f = new fixture())
                    {
                        f.select("physbone"); f.copy(); require(Mathf.Approximately(f.pb_radius, 0.3f), "Collider not copied");
                        require(((IList)f.target_pb.GetType().GetField("colliders").GetValue(f.target_pb))[0] as Component == f.target_pb_collider, "Collider reference not remapped");
                        require(Mathf.Approximately(f.target_constraint.weight, 0.1f) && Mathf.Approximately(f.target_collider.radius, 0.7f) && f.target_skin.sharedMaterial == f.target_material, "Other categories changed");
                        if (f.target_ma != null) require(f.ma_priority == 4, "MA changed");
                    }
                });
            if (find_type("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator") != null)
                check("MA tab copies MA only", () =>
                {
                    using (var f = new fixture())
                    {
                        f.select("modular_avatar"); f.copy(); require(f.ma_priority == 23, "MA not copied");
                        require(Mathf.Approximately(f.target_constraint.weight, 0.1f) && Mathf.Approximately(f.target_collider.radius, 0.7f) && f.target_skin.sharedMaterial == f.target_material, "Other categories changed");
                        if (f.target_pb != null) require(Mathf.Approximately(f.pb_radius, 0.1f), "PhysBone changed");
                    }
                });
            open_scenario(); EditorApplication.update += tick;
        }

        private static void open_scenario()
        {
            frames = 0; current = new fixture();
            current.window.position = new Rect(0, 0, scenario == 2 ? 860 : 1200, scenario == 2 ? 620 : 850);
            current.select(current.source_pb == null || scenario == 3 ? "constraint" : scenario == 4 ? "modular_avatar" : "physbone");
            var panel_type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel", true);
            panel = (IPanel)panel_type.GetMethod("CreateEditorPanel", flags).Invoke(null, new object[] { current.window });
            var editor_ui = typeof(Editor).Assembly.GetType("UnityEditor.UIElements.UIElementsEditorUtility", true);
            bool dark = scenario != 1;
            panel.visualTree.styleSheets.Add((StyleSheet)editor_ui.GetMethod(dark ? "GetCommonDarkStyleSheet" : "GetCommonLightStyleSheet", flags).Invoke(null, null));
            panel.visualTree.Add(current.window.rootVisualElement);
            current.window.rootVisualElement.EnableInClassList("d9_theme_light", !dark);
            current.window.rootVisualElement.EnableInClassList("d9_theme_dark", dark);
            layout();
        }
        private static void tick()
        {
            if (++frames < 25) { current.window.Repaint(); return; }
            try
            {
                layout();
                string name = new[] { "physbone_dark", "physbone_light", "narrow", "constraint_dark", "ma_dark" }[scenario];
                check(name + " layout, theme and footer", () =>
                {
                    var root = current.window.rootVisualElement; var table = root.Q<MultiColumnListView>("copy_preview");
                    require(root.ClassListContains("d9_ui_root"), "Theme missing");
                    require(Math.Abs(root.resolvedStyle.backgroundColor.r - (scenario == 1 ? 245f : 31f) / 255f) < 0.02f, "Theme color mismatch");
                    require(table.worldBound.height > 200, "Table collapsed");
                    if (table.itemsSource.Count > 0) require(table.Q<Label>(className: "d9_table_cell") != null, "Virtualized cells missing");
                    var footer = root.Q<Button>("copy_active_tab").worldBound;
                    require(footer.yMin >= 0 && footer.yMax <= root.worldBound.yMax + 1 && footer.xMax <= root.worldBound.xMax + 1, "Footer clipped");
                    var tabs = root.Q("copy_tabs").worldBound;
                    require(tabs.yMax < footer.yMin && tabs.xMax <= root.worldBound.xMax + 1, "Tabs clipped");
                });
                capture(name);
                if (scenario == 3) check("Real checkbox events persist across tabs; all unchecked disables copy and execution is a no-op", checkbox_interaction);
                panel.Dispose(); current.Dispose();
                if (++scenario < 5) { open_scenario(); return; }
            }
            catch (Exception e) { errors.Add(e.ToString()); }
            EditorApplication.update -= tick; Application.logMessageReceived -= log;
            File.WriteAllText(output + "/checks.json", JsonUtility.ToJson(new report { passed = errors.Count == 0, unity = Application.unityVersion, checks = checks.ToArray(), errors = errors.ToArray() }, true));
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
        private static void checkbox_interaction()
        {
            var table = (MultiColumnListView)get(current.window, "copyListView");
            var toggle = table.Query<Toggle>(className: "d9_table_check").ToList().First(t => t.userData != null && get(t.userData, "SourceComponent") as Component == current.source_constraint);
            toggle.value = false;
            current.select("physbone"); current.select("constraint");
            require(((HashSet<Component>)get(current.window, "excluded_components")).Contains(current.source_constraint), "Check state lost");
            // Bind the same public table cell callbacks after a tab rebuild to check visual state too.
            var cell = (Toggle)table.columns[0].makeCell(); current.window.rootVisualElement.Add(cell);
            var row_index = Enumerable.Range(0, table.itemsSource.Count).First(i => get(table.itemsSource[i], "SourceComponent") as Component == current.source_constraint);
            table.columns[0].bindCell(cell, row_index); require(!cell.value, "Checkbox binding lost state"); cell.RemoveFromHierarchy();
            var excluded = (HashSet<Component>)get(current.window, "excluded_components");
            foreach (var row in table.itemsSource) if (get(row, "SourceComponent") is Component component) excluded.Add(component);
            call(current.window, "Scan"); require(!current.window.rootVisualElement.Q<Button>("copy_active_tab").enabledSelf, "Empty selection allows copying");
            current.copy(); require(Mathf.Approximately(current.target_constraint.weight, 0.1f) && current.target_skin.sharedMaterial == current.target_material, "Empty selection changed target");
        }
        private static void layout()
        {
            panel.visualTree.style.width = current.window.position.width; panel.visualTree.style.height = current.window.position.height;
            panel.GetType().GetMethod("ValidateLayout", flags).Invoke(panel, null);
        }
        private static void capture(string name)
        {
            int width = (int)current.window.position.width, height = (int)current.window.position.height;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32); var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = rt; GL.Clear(true, true, Color.magenta); GL.PushMatrix();
                try { GL.LoadPixelMatrix(0, width, height, 0); panel.GetType().GetMethod("Repaint", flags).Invoke(panel, new object[] { new Event { type = EventType.Repaint } }); }
                finally { GL.PopMatrix(); }
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(output + "/" + name + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
        }
        private static void log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || (type == LogType.Warning && (message.Contains(".uss") || message.Contains("UIスタイル")))) errors.Add(message);
        }
    }
}
