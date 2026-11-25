using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy to an isolated Unity project's Assets/Editor and execute capsule_selection_checks.run.
public class capsule_selection_checks : EditorWindow
{
    private const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type tool = typeof(DisplayChildNamesInScene);
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<GameObject> fixtures = new List<GameObject>();
    private static capsule_selection_checks window;
    private static SceneView view;
    private static object state;
    private static Camera camera;
    private static Transform bone, tip, other;
    private static bool ready, pointer_in_view = true, native_handle, fake_sphere;
    private static Vector3 pointer_world;
    private static Vector2 pointer_offset;
    private static bool pointer_over_panel;
    private static int capsule_id;
    private static bool capsule_was_nearest;
    [Serializable] private class report { public string unity; public bool success; public string[] checks; public string[] errors; }

    public static void run()
    {
        Tool previous_tool = Tools.current;
        int previous_layers = Tools.visibleLayers;
        try
        {
            Tools.current = Tool.Move; Tools.visibleLayers = -1;
            set("isEnabled", true); set("show_humanoid_capsules", true);
            set("enable_humanoid_capsule_click_selection", true);
            set("show_bone_spheres", false); set("enable_bone_sphere_click_selection", false);
            set("humanoid_capsule_thickness", 0.15f); set("maxDepth", 0f);
            view = CreateInstance<SceneView>(); camera = view.camera;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.orthographic = true; camera.orthographicSize = 2f;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 100f;
            camera.pixelRect = new Rect(0, 0, 600, 400);
            bone = make("upper_arm", new Vector3(-0.6f, 0, 5));
            tip = make("lower_arm", new Vector3(0.6f, 0, 5));
            other = make("original_selection", Vector3.zero);
            state = create("SceneViewState"); field(state, "view", view);
            add_segment(bone, tip);
            window = CreateInstance<capsule_selection_checks>();
            window.position = new Rect(30, 30, 600, 400); window.ShowUtility();
            ready = true; pointer_world = new Vector3(0, 0.1f, 5);

            send(EventType.Layout); send(EventType.MouseDown);
            require(captured() && (bool)get(state, "active_is_capsule") && (Transform)get(state, "active_bone") == bone,
                "Click inside a capsule captures its start bone even with spheres disabled");
            send(EventType.MouseUp); apply_selection();
            require(Selection.activeGameObject == bone.gameObject && !captured(), "MouseUp selects the upper arm rather than its lower-arm endpoint");
            Selection.activeGameObject = other.gameObject;
            pointer_world = new Vector3(0.68f, 0.04f, 5);
            click(); require(Selection.activeGameObject == bone.gameObject, "The rounded cap interior is selectable");
            pointer_world = new Vector3(0, 0.4f, 5);
            click(); require(Selection.activeGameObject == other.gameObject, "Outside the rendered capsule does not capture or select");
            pointer_world = new Vector3(0, 0.1f, 5);

            fake_sphere = true; send(EventType.Layout);
            require(capsule_was_nearest, "Humanoid capsules win over ordinary sphere and mesh controls");
            click(); require(Selection.activeGameObject == bone.gameObject, "Priority also holds through MouseDown and MouseUp");
            native_handle = true; click();
            require(Selection.activeGameObject == other.gameObject && !captured(), "A native transform handle at distance zero keeps priority");
            native_handle = false; fake_sphere = false;

            foreach (int button in new[] { 1, 2 })
            {
                click(button); require(Selection.activeGameObject == other.gameObject && !captured(), "Navigation mouse button " + button + " does not select a capsule");
            }
            click(0, true); require(!captured() && Selection.activeGameObject == other.gameObject, "Alt-left orbit is not captured");
            Tools.current = Tool.View; click();
            require(!captured() && Selection.activeGameObject == other.gameObject, "The View tool retains mouse navigation");
            Tools.current = Tool.Move;
            pointer_over_panel = true; click();
            require(!captured() && Selection.activeGameObject == other.gameObject, "The Transform panel retains its own controls over a capsule");
            pointer_over_panel = false;
            pointer_in_view = false; click();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Another editor window cannot capture scene capsule input");
            pointer_in_view = true;

            Selection.activeGameObject = other.gameObject; send(EventType.Layout); send(EventType.MouseDown);
            pointer_offset = new Vector2(20, 0); send(EventType.MouseDrag); send(EventType.MouseUp); apply_selection(); pointer_offset = Vector2.zero;
            require(!captured() && Selection.activeGameObject == other.gameObject, "Dragging does not commit a click selection");
            send(EventType.Layout); send(EventType.MouseDown); send(EventType.KeyDown, 0, false, KeyCode.Escape); send(EventType.MouseUp); apply_selection();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Escape cancels the gesture and releases capture");
            send(EventType.Layout); send(EventType.MouseDown); send(EventType.MouseDrag, 0, true); send(EventType.MouseUp, 0, true); apply_selection();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Changing to Alt navigation cancels a held capsule");
            send(EventType.Layout); send(EventType.MouseDown); set("show_humanoid_capsules", false); send(EventType.MouseUp); apply_selection();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Hiding capsules mid-gesture cancels selection");
            set("show_humanoid_capsules", true);
            SceneVisibilityManager.instance.DisablePicking(bone.gameObject, false); click();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Scene picking-disabled bones cannot be selected");
            SceneVisibilityManager.instance.EnablePicking(bone.gameObject, false);
            SceneVisibilityManager.instance.Hide(bone.gameObject, false); click();
            require(!captured() && Selection.activeGameObject == other.gameObject, "Hidden bones have no invisible hit area");
            SceneVisibilityManager.instance.Show(bone.gameObject, false);
            bone.gameObject.SetActive(false); click();
            require(Selection.activeGameObject == other.gameObject, "Inactive bones are excluded"); bone.gameObject.SetActive(true);
            set("maxDepth", 2f); click(); require(Selection.activeGameObject == other.gameObject, "The maximum-depth limit also applies to picking"); set("maxDepth", 0f);

            var far_bone = make("far_upper_arm", new Vector3(-0.6f, 0, 7));
            var far_tip = make("far_lower_arm", new Vector3(0.6f, 0, 7)); add_segment(far_bone, far_tip);
            pointer_world = new Vector3(0, 0, 5); click();
            require(Selection.activeGameObject == bone.gameObject, "Overlapping capsule axes choose the nearer bone");
            segments().Clear(); add_segment(bone, null, tip, 0.5f);
            pointer_world = new Vector3(-0.9f, 0, 5); click();
            require(Selection.activeGameObject == bone.gameObject, "Leaf capsules select the leaf bone itself");
            segments().Clear(); add_segment(bone, tip);
            bone.position = new Vector3(0, 0, 4); tip.position = new Vector3(0, 0, 6);
            pointer_world = new Vector3(0.15f, 0.15f, 4); click();
            require(Selection.activeGameObject == bone.gameObject, "End-on capsules have a selectable circular interior");
            camera.orthographic = false; camera.fieldOfView = 60f; click();
            require(Selection.activeGameObject == bone.gameObject, "End-on capsule picking also works in perspective");
            bone.position = new Vector3(-0.6f, 0, 4); tip.position = new Vector3(0.6f, 0, 6);
            pointer_world = new Vector3(0, 0.08f, 5); click();
            require(Selection.activeGameObject == bone.gameObject, "Oblique perspective capsules use the same outline for drawing and picking");

            set("enable_humanoid_capsule_click_selection", false); click();
            require(Selection.activeGameObject == other.gameObject, "The capsule click-selection setting can be disabled independently");
            call("SaveSessionState"); set("enable_humanoid_capsule_click_selection", true); call("LoadSessionState");
            require(!(bool)get_tool("enable_humanoid_capsule_click_selection"), "The capsule click preference survives reload");
            SessionState.EraseBool("DisplayChildNamesInScene.enable_humanoid_capsule_click_selection"); call("LoadSessionState");
            require((bool)get_tool("enable_humanoid_capsule_click_selection"), "New installs enable capsule click selection by default");

            // Exercise geometric limits without allocating scene objects or colliders.
            foreach (bool orthographic in new[] { true, false })
            foreach (float thickness in new[] { 0.03f, 0.15f, 0.5f })
            foreach (Vector3 endpoint in new[] { new Vector3(1, 0, 5), new Vector3(0, 1, 6), new Vector3(0, 0, 7) })
            {
                camera.orthographic = orthographic; Vector3 start = new Vector3(0, 0, 5);
                bool built = (bool)call("build_humanoid_capsule_outline", camera, start, endpoint, Vector3.Distance(start, endpoint) * thickness);
                var world = (Vector3[])get_tool("humanoid_capsule_points");
                var polygon = world.Select(p => (Vector2)camera.WorldToScreenPoint(p)).ToArray();
                require_silent(built && (bool)call("is_inside_capsule_outline", (Vector2)camera.WorldToScreenPoint((start + endpoint) * 0.5f), polygon, 0f), "Geometric center outside capsule");
                require_silent(!(bool)call("is_inside_capsule_outline", new Vector2(-10000, -10000), polygon, 0f), "Remote point inside capsule");
            }
            passed.Add("18 projection, angle and thickness combinations include the center and exclude remote points");
            require(!(bool)call("build_humanoid_capsule_outline", camera, Vector3.one, Vector3.one, 0.2f) &&
                !(bool)call("build_humanoid_capsule_outline", camera, Vector3.one, Vector3.zero, float.NaN), "Degenerate and invalid capsules are rejected");
            require(((Color)get_tool("humanoid_capsule_left_color")).a == 1f && ((Color)get_tool("humanoid_capsule_right_color")).a == 1f &&
                ((Color)get_tool("humanoid_capsule_center_color")).a == 1f && (float)get_tool("humanoid_capsule_line_width") >= 3f,
                "Capsule outlines are opaque and use thicker lines");
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        finally
        {
            ready = false;
            if (state != null) call("ClearActiveBoneSphere", state);
            set("pending_delayed_bone_sphere_selection", null);
            GUIUtility.hotControl = 0; Selection.activeObject = null;
            if (window != null) window.Close();
            if (view != null) Object.DestroyImmediate(view);
            foreach (GameObject fixture in fixtures) if (fixture != null) Object.DestroyImmediate(fixture);
            Tools.current = previous_tool; Tools.visibleLayers = previous_layers;
            var result = new report { unity = Application.unityVersion, success = errors.Count == 0, checks = passed.ToArray(), errors = errors.ToArray() };
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/capsule_selection_checks.json", JsonUtility.ToJson(result, true));
            Debug.Log("CAPSULE_SELECTION_CHECKS: " + passed.Count + " passed, " + errors.Count + " failed");
            EditorApplication.Exit(result.success ? 0 : 1);
        }
    }

    private void OnGUI()
    {
        if (!ready) return;
        typeof(HandleUtility).GetMethod("BeginHandles", flags).Invoke(null, null);
        try
        {
            Handles.SetCamera(new Rect(0, 0, 600, 400), camera);
            Event e = Event.current;
            capsule_id = GUIUtility.GetControlID(44551, FocusType.Passive);
            int sphere_id = GUIUtility.GetControlID(44552, FocusType.Passive);
            int mesh_id = GUIUtility.GetControlID(44553, FocusType.Passive);
            int native_id = GUIUtility.GetControlID(44554, FocusType.Passive);
            e.mousePosition = HandleUtility.WorldToGUIPoint(pointer_world) + pointer_offset;
            field(state, "has_mouse_position", true); field(state, "mouse_position", e.mousePosition);
            field(state, "column_layout_active", pointer_over_panel); field(state, "floating_layout_active", pointer_over_panel);
            field(state, "floating_panel_rect", new Rect(e.mousePosition - Vector2.one * 20, Vector2.one * 40));
            var candidates = (IList)get(state, "column_candidates"); candidates.Clear(); if (pointer_over_panel) candidates.Add(create("LabelCandidate"));
            call("CompleteActiveBoneSphereHandleIfNeeded", view, state, e);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(mesh_id);
                if (fake_sphere) HandleUtility.AddControl(sphere_id, 0.01f);
                if (native_handle) HandleUtility.AddControl(native_id, 0f);
            }
            call("handle_humanoid_capsule_input", view, state, e, capsule_id, pointer_in_view);
            if (e.type == EventType.Layout) capsule_was_nearest = HandleUtility.nearestControl == capsule_id;
            if (e.type == EventType.Repaint) call("draw_humanoid_capsules", view, state, capsule_id);
        }
        finally { typeof(HandleUtility).GetMethod("EndHandles", flags).Invoke(null, null); }
    }
    private static void send(EventType type, int button = 0, bool alt = false, KeyCode key = KeyCode.None)
    { window.SendEvent(new Event { type = type, button = button, alt = alt, keyCode = key, mousePosition = new Vector2(150, 150) }); }
    private static void click(int button = 0, bool alt = false)
    { Selection.activeGameObject = other.gameObject; send(EventType.Layout, button, alt); send(EventType.MouseDown, button, alt); send(EventType.MouseUp, button, alt); apply_selection(); }
    private static void apply_selection() { call("ApplyDelayedBoneSphereSelection"); }
    private static bool captured() { return (int)get(state, "active_control_id") != 0 && GUIUtility.hotControl != 0; }
    private static Transform make(string name, Vector3 position) { var go = new GameObject(name); fixtures.Add(go); go.transform.position = position; return go.transform; }
    private static IList segments() { return (IList)get(state, "humanoid_capsules"); }
    private static void add_segment(Transform start, Transform end, Transform parent = null, float ratio = 0)
    { var segment = create("HumanoidCapsuleSegment"); field(segment, "start", start); field(segment, "end", end); field(segment, "leaf_parent", parent); field(segment, "leaf_length_ratio", ratio); segments().Add(segment); }
    private static object create(string name) { return Activator.CreateInstance(tool.GetNestedType(name, BindingFlags.NonPublic), true); }
    private static object get(object target, string name) { return target.GetType().GetField(name).GetValue(target); }
    private static void field(object target, string name, object value) { target.GetType().GetField(name).SetValue(target, value); }
    private static object get_tool(string name) { return tool.GetField(name, flags).GetValue(null); }
    private static void set(string name, object value) { tool.GetField(name, flags).SetValue(null, value); }
    private static object call(string name, params object[] args) { return tool.GetMethod(name, flags).Invoke(null, args); }
    private static void require(bool condition, string message) { require_silent(condition, message); passed.Add(message); }
    private static void require_silent(bool condition, string message) { if (!condition) throw new Exception(message); }
}
