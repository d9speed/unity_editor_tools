// One column per prefab instance. Both columns and their Transform rows paginate.
// 1-30 object blocks per column/page. Nearby panel height follows the actual pages.
// Screen bounds always apply; the previous pixel height cap is now optional.
// No camera-driven repaint loop. Scene Transform discovery is cached between hierarchy changes.
// Compact nearby panel with stable hover transfer; screen-edge columns remain optional.
// Column overflow is paginated, never resolved by overlapping/clamping labels.
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.ShortcutManagement;
using UnityEditor.SceneManagement;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Overlays;
using UnityEngine.UIElements;
#endif

#if UNITY_2021_2_OR_NEWER
[Overlay(typeof(SceneView), "Display Child Names", true)]
public class DisplayChildNamesInSceneOverlay : Overlay
{
    public override VisualElement CreatePanelContent()
    {
        var container = new IMGUIContainer(DisplayChildNamesInScene.DrawOverlayContent);
        // Limit the UI Toolkit element AND the IMGUI rows. minWidth alone is not a limit.
        // Leave height automatic so collapsing settings or switching tabs also shrinks it.
        container.style.width = DisplayChildNamesInScene.SettingsPanelWidth;
        container.style.minWidth = DisplayChildNamesInScene.SettingsPanelWidth;
        container.style.maxWidth = DisplayChildNamesInScene.SettingsPanelWidth;
        container.style.flexGrow = 0f;
        container.style.flexShrink = 0f;
        return container;
    }
}
#endif

// 常に表示されるシーンビューツール
[InitializeOnLoad]
public static class DisplayChildNamesInScene
{
    private const string session_key_prefix = "DisplayChildNamesInScene.";
    private const string session_key_enabled = session_key_prefix + "enabled";
    private const string session_key_cursor_radius_pixels = session_key_prefix + "cursor_radius_pixels";
    private const string session_key_label_hold_radius_pixels = session_key_prefix + "label_hold_radius_pixels";
    private const string session_key_max_depth = session_key_prefix + "max_depth";
    private const string session_key_gui_offset_x = session_key_prefix + "gui_offset_x";
    private const string session_key_gui_offset_y = session_key_prefix + "gui_offset_y";
    private const string session_key_font_size = session_key_prefix + "font_size";
    private const string session_key_label_color_r = session_key_prefix + "label_color_r";
    private const string session_key_label_color_g = session_key_prefix + "label_color_g";
    private const string session_key_label_color_b = session_key_prefix + "label_color_b";
    private const string session_key_label_color_a = session_key_prefix + "label_color_a";
    private const string session_key_min_alpha = session_key_prefix + "min_alpha";
    private const string session_key_max_alpha = session_key_prefix + "max_alpha";
    private const string session_key_show_settings = session_key_prefix + "show_settings";
    private const string session_key_show_vrc_phys_bone_components = session_key_prefix + "show_vrc_phys_bone_components";
    private const string session_key_show_bone_spheres = session_key_prefix + "show_bone_spheres";
    private const string session_key_show_bone_hierarchy = session_key_prefix + "show_bone_hierarchy";
    private const string session_key_show_bone_spheres_near_cursor_only = session_key_prefix + "show_bone_spheres_near_cursor_only";
    private const string session_key_enable_bone_sphere_click_selection = session_key_prefix + "enable_bone_sphere_click_selection";
    private const string session_key_bone_sphere_size = session_key_prefix + "bone_sphere_size";

    private const float default_cursor_radius_pixels = 80f;
    private const float default_label_hold_radius_pixels = 180f;
    private const float default_max_depth = 0f;
    private const float default_bone_sphere_size = 0.06f;
    private const float max_bone_sphere_size = 1.0f;
    private static readonly Vector2 default_gui_offset = new Vector2(0, 20);
    private const int default_font_size = 14;
    private static readonly Color default_label_color = Color.white;
    private const float default_min_alpha = 0.2f;
    private const float default_max_alpha = 1.0f;
    private const string vrc_phys_bone_type_name = "VRCPhysBone";
    private const string vrc_phys_bone_collider_type_name = "VRCPhysBoneCollider";

    private const float label_width = 200f;
    private static float label_height => Mathf.Max(20f, fontSize + 4f);
    private static float component_row_height => Mathf.Max(20f, fontSize + 4f);
    private const float block_spacing = 4f;
    private const float leader_line_gap = 14f;
    private const float label_anchor_group_pixels = 12f;
    private const float bone_sphere_pick_padding_pixels = 6f;
    private static readonly int bone_sphere_button_hash = "DisplayChildNamesInSceneBoneSphere".GetHashCode();
    private static readonly int column_label_control_hash = "DisplayChildNamesInSceneTransformLabel".GetHashCode();
    private static GUIStyle object_name_style;
    private static Texture transform_icon;
    private static GUIStyle prefab_header_style;
    private static GUIStyle column_count_style;
    private static Texture prefab_icon;
    private static float prefab_header_height => label_height + 4f;
    private static readonly Vector3[] bone_line_points = new Vector3[2];
    private static readonly BindingFlags component_member_flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private const string session_key_two_columns = session_key_prefix + "two_columns";
    private const string session_key_column_items_per_page = session_key_prefix + "column_items_per_page";
    private const int default_column_items_per_page = 10;
    private const int max_column_items_per_page = 30;
    private static int column_items_per_page = default_column_items_per_page;
    private const string session_key_column_width = session_key_prefix + "column_width";
    private const string session_key_column_gap = session_key_prefix + "column_gap";
    private const string session_key_column_inset = session_key_prefix + "column_inset";
    private const string session_key_column_top = session_key_prefix + "column_top";
    private const string session_key_column_bottom = session_key_prefix + "column_bottom";
    private const float column_padding = 4f;
    private const float column_center_gap = 32f;
    private const float column_header_height = 22f;
    private static bool two_column_layout = true;
    // Intentionally not persisted: a reload should not silently pin an old target.
    private static bool pin_column_labels;
    private static float column_width = 240f;
    private static float column_gap = 6f;
    private static float column_inset = 12f;
    private static Vector2 column_vertical_margins = new Vector2(48f, 20f);

    private const string session_key_floating_columns = session_key_prefix + "floating_columns";
    private const string session_key_floating_offset_x = session_key_prefix + "floating_offset_x";
    private const string session_key_floating_offset_y = session_key_prefix + "floating_offset_y";
    private const string session_key_floating_max_height = session_key_prefix + "floating_max_height";
    private const string session_key_floating_limit_height = session_key_prefix + "floating_limit_height";
    private const string session_key_floating_spacing = session_key_prefix + "floating_spacing";
    private const string session_key_floating_all_leaders = session_key_prefix + "floating_all_leaders";
    private const float floating_panel_padding = 4f;
    private const float floating_reanchor_distance = 40f;
    private static bool floating_column_layout = true;
    private static Vector2 floating_panel_offset = new Vector2(48f, 40f);
    private static float floating_panel_max_height = 360f;
    // New mode defaults to count-driven height, but preserves the old pixel limit's value.
    private static bool floating_limit_height = false;
    private static float floating_column_spacing = 8f;
    private static bool floating_all_leaders = false;

    private sealed class LabelCandidate
    {
        public GameObject gameObject;
        public GameObject prefab_root;
        public Vector2 anchor;
        public float alpha;
        public List<ComponentDisplayInfo> components;
    }

    private sealed class LabelColumn
    {
        public GameObject prefab_root;
        public int prefab_key;
        public readonly List<LabelCandidate> candidates = new List<LabelCandidate>();
        public Rect panel_rect;
        public Rect body_rect;
        public int page_index;
        public int target_count;
        public int page_item_limit; // Reset this column's page when the configured count changes.
        public readonly List<List<LabelDisplayInfo>> pages = new List<List<LabelDisplayInfo>>();
    }

    private class LabelDisplayInfo
    {
        public GameObject gameObject;
        public GameObject prefab_root;
        public bool show_prefab_header;
        public Rect prefab_header_rect;
        public Vector2 anchor_gui_position;
        public Rect block_rect;
        public Rect label_rect;
        public float alpha;
        public List<ComponentDisplayInfo> components;
        public bool is_column_label;
        public bool left_column;
        public bool continuation;
    }

    private struct LabelHitArea
    {
        public Rect rect;
        public GameObject gameObject;

        public LabelHitArea(Rect rect, GameObject gameObject)
        {
            this.rect = rect;
            this.gameObject = gameObject;
        }
    }

    private struct ComponentDisplayInfo
    {
        public Component component;
        public string prefix;
        public Color color;

        public ComponentDisplayInfo(Component component, string prefix, Color color)
        {
            this.component = component;
            this.prefix = prefix;
            this.color = color;
        }
    }

    private struct BoneSphereDrawInfo
    {
        public Transform bone;
        public Vector3 position;
        public float size;
        public float depth;
        public float center_distance;
        public float pick_distance;
    }

    private sealed class SceneViewState
    {
        public SceneView view;
        public StageHandle transform_stage;
        public HashSet<Transform> bones;
        public readonly List<Transform> ordered_bones = new List<Transform>();
        public readonly List<LabelHitArea> visible_labels = new List<LabelHitArea>();
        public readonly List<LabelDisplayInfo> labels = new List<LabelDisplayInfo>();
        public readonly List<BoneSphereDrawInfo> spheres = new List<BoneSphereDrawInfo>();
        public bool bone_cache_dirty = true;
        public bool has_mouse_position;
        public Vector2 mouse_position;
        public int active_control_id;
        public Transform active_bone;
        public Transform candidate_bone;
        public Vector2 mouse_down_position;
        public bool dragged;
        public bool cancel_requested;
        public int active_column_label_control_id;
        public Transform active_column_label_transform;
        public Rect column_label_mouse_down_rect;
        public Vector2 column_label_mouse_down_position;
        public readonly List<LabelCandidate> column_candidates = new List<LabelCandidate>();
        public readonly List<LabelColumn> prefab_columns = new List<LabelColumn>();
        public readonly List<LabelColumn> visible_columns = new List<LabelColumn>();
        public int column_set_page;
        public int pending_column_set_page = -1;
        public int columns_per_page = 1;
        public Rect column_set_pager_rect;
        public bool column_layout_active;
        public bool column_layout_too_small;
        public bool column_content_dirty = true;
        public bool column_source_valid;
        public Rect column_source_rect;
        public Rect column_viewport;
        public bool floating_layout_active;
        public bool floating_origin_valid;
        public Rect floating_panel_rect;
        public Vector2 floating_origin;
        public Vector2 floating_query_position;
        public GameObject floating_focus_object;
        public int floating_quadrant;
    }

    private static readonly Dictionary<int, SceneViewState> scene_view_states =
        new Dictionary<int, SceneViewState>();

    // 静的コンストラクタ（エディタ起動時に実行）
    static DisplayChildNamesInScene()
    {
        LoadSessionState();

        // シーンビューのGUIイベントに登録
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged -= MarkPhysBoneRootReferenceCacheDirty;
        EditorApplication.hierarchyChanged += MarkPhysBoneRootReferenceCacheDirty;
        Undo.undoRedoPerformed -= MarkPhysBoneRootReferenceCacheDirty;
        Undo.undoRedoPerformed += MarkPhysBoneRootReferenceCacheDirty;
    }

    // ツールの有効/無効状態
    private static bool isEnabled = true;
    private static bool showSettings = false;
    private static bool show_vrc_phys_bone_components = true;
    private static bool show_bone_spheres = true;
    private static bool show_bone_hierarchy = true;
    private static bool show_bone_spheres_near_cursor_only = true;
    private static bool enable_bone_sphere_click_selection = false;
    private static Dictionary<Transform, List<Component>> phys_bone_root_reference_cache = new Dictionary<Transform, List<Component>>();
    private static bool phys_bone_root_reference_cache_dirty = true;
    private static Transform pending_delayed_bone_sphere_selection = null;
    
    // マウスカーソル周辺とみなす半径（GUIピクセル単位）
    private static float cursor_radius_pixels = default_cursor_radius_pixels;
    private static float label_hold_radius_pixels = default_label_hold_radius_pixels;
    // カメラからの最大深度。表示の値を超えるオブジェクトは無視
    private static float maxDepth = default_max_depth;
    private static float bone_sphere_size = default_bone_sphere_size;
    // GUI上での表示位置のオフセット
    private static Vector2 guiOffset = default_gui_offset;
    // 文字サイズ
    private static int fontSize = default_font_size;
    // 文字の色
    private static Color labelColor = default_label_color;
    // 透明度の最小値（最も遠い場合）
    private static float minAlpha = default_min_alpha;
    // 透明度の最大値（最も近い場合）
    private static float maxAlpha = default_max_alpha;
    
    // エディタ終了時のクリーンアップ用クラス
    [InitializeOnLoad]
    private class Cleanup
    {
        static Cleanup()
        {
            EditorApplication.quitting += OnEditorQuitting;
        }
        
        private static void OnEditorQuitting()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.hierarchyChanged -= MarkPhysBoneRootReferenceCacheDirty;
            EditorApplication.delayCall -= ApplyDelayedBoneSphereSelection;
            Undo.undoRedoPerformed -= MarkPhysBoneRootReferenceCacheDirty;
            scene_view_states.Clear();
            EditorApplication.quitting -= OnEditorQuitting;
        }
    }
    
    // シーンビューのGUIを描画
    private static void OnSceneGUI(SceneView sceneView)
    {
        if (sceneView == null || Event.current == null)
            return;

        Event e = Event.current;
        // IMPORTANT: fixed IDs on EVERY event, before any visibility/hotControl test.
        // Visible bone count can no longer change the IDs of later Scene GUI controls.
        int sphere_control_id = GUIUtility.GetControlID(bone_sphere_button_hash, FocusType.Passive);
        int column_label_control_id = GUIUtility.GetControlID(column_label_control_hash, FocusType.Passive);
        SceneViewState state = GetSceneViewState(sceneView);
        // Disabled means no input tracking, camera inspection, picking or repaint requests.
        // Only release captures that this view already owns.
        CompleteActiveBoneSphereHandleIfNeeded(sceneView, state, e);
        if (!isEnabled || sceneView.camera == null)
        {
            clear_column_label_capture(state);
            return;
        }

        sceneView.wantsMouseMove = true;
        TrackSceneViewInput(sceneView, state, e);

        Color previous_color = Handles.color;
        Matrix4x4 previous_matrix = Handles.matrix;
        try
        {
            // Bone positions are already in world space. Do not inherit other tools' state.
            Handles.matrix = Matrix4x4.identity;

            // Build the control snapshot only on Layout, never during Repaint.
            // Keep component controls and their order unchanged throughout a drag.
            if (GUIUtility.hotControl == 0 && e.type == EventType.Layout)
            {
                refresh_scene_transform_cache(state);
                RebuildLabelLayout(sceneView, state);
            }

            // Rendering is not gated by another tool's hotControl.
            draw_bone_hierarchy(sceneView, state);
            DrawBoneSpheres(sceneView, state, sphere_control_id);

            Handles.BeginGUI();
            try
            {
                handle_column_label_input(sceneView, state, e, column_label_control_id);
                if (!state.column_layout_active && e.type == EventType.MouseDown && e.button == 0 &&
                    !e.alt && !Tools.viewToolActive && GUIUtility.hotControl == 0)
                {
                    foreach (LabelHitArea label in state.visible_labels)
                    {
                        if (label.gameObject != null && label.rect.Contains(e.mousePosition))
                        {
                            SelectGameObject(label.gameObject);
                            e.Use();
                            sceneView.Repaint();
                            break;
                        }
                    }
                }

                // Do not omit these controls while a checkbox/transform is hot.
                DisplayObjectNames(sceneView, state);
                // Child controls receive the event first. Empty panel/gutter pixels
                // must not select/zoom the mesh underneath the floating panel.
                if (state.column_layout_active && IsPointerOverColumns(state, e.mousePosition) &&
                    !e.alt && !Tools.viewToolActive && GUIUtility.hotControl == 0 &&
                    ((e.type == EventType.MouseDown && e.button == 0) || e.type == EventType.ScrollWheel))
                    e.Use();
            }
            finally
            {
                Handles.EndGUI();
            }
        }
        finally
        {
            Handles.color = previous_color;
            Handles.matrix = previous_matrix;
        }
    }

    private static void handle_column_label_input(
        SceneView scene_view, SceneViewState state, Event current_event, int control_id)
    {
        if (state.active_column_label_control_id != 0)
        {
            if (GUIUtility.hotControl != state.active_column_label_control_id)
            {
                clear_column_label_capture(state);
                return;
            }

            bool escape = current_event.type == EventType.KeyDown && current_event.keyCode == KeyCode.Escape;
            if (!two_column_layout || !state.column_layout_active ||
                state.active_column_label_transform == null || current_event.alt || Tools.viewToolActive ||
                current_event.type == EventType.Ignore || current_event.type == EventType.MouseLeaveWindow ||
                current_event.type == EventType.DragExited || escape)
            {
                clear_column_label_capture(state);
                if (escape)
                    current_event.Use();
                scene_view.Repaint();
                return;
            }

            if (current_event.type == EventType.MouseDrag && current_event.button == 0)
            {
                if ((current_event.mousePosition - state.column_label_mouse_down_position).sqrMagnitude > 16f)
                {
                    Transform dragged_transform = state.active_column_label_transform;
                    // Keep the Inspector's current selection until a click is completed.
                    // Release our capture before handing the reference to Unity's native drag.
                    clear_column_label_capture(state);
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new Object[] { dragged_transform };
                    DragAndDrop.StartDrag(dragged_transform.name + " (Transform)");
                }
                current_event.Use();
                scene_view.Repaint();
                return;
            }

            if (current_event.rawType == EventType.MouseUp && current_event.button == 0)
            {
                Transform clicked_transform = state.active_column_label_transform;
                bool is_click = current_event.type == EventType.MouseUp &&
                    state.column_label_mouse_down_rect.Contains(current_event.mousePosition) &&
                    (current_event.mousePosition - state.column_label_mouse_down_position).sqrMagnitude <= 16f;
                clear_column_label_capture(state);
                if (is_click)
                    SelectGameObject(clicked_transform.gameObject);
                current_event.Use();
                scene_view.Repaint();
            }
            return;
        }

        if (!two_column_layout || !state.column_layout_active ||
            current_event.type != EventType.MouseDown || current_event.button != 0 ||
            current_event.alt || Tools.viewToolActive || GUIUtility.hotControl != 0)
            return;

        foreach (LabelHitArea label in state.visible_labels)
        {
            if (label.gameObject == null || !label.rect.Contains(current_event.mousePosition))
                continue;
            state.active_column_label_control_id = control_id;
            state.active_column_label_transform = label.gameObject.transform;
            state.column_label_mouse_down_rect = label.rect;
            state.column_label_mouse_down_position = current_event.mousePosition;
            GUIUtility.hotControl = control_id;
            GUIUtility.keyboardControl = 0;
            current_event.Use();
            scene_view.Repaint();
            break;
        }
    }

    private static void clear_column_label_capture(SceneViewState state)
    {
        if (state.active_column_label_control_id != 0 &&
            GUIUtility.hotControl == state.active_column_label_control_id)
            GUIUtility.hotControl = 0;
        state.active_column_label_control_id = 0;
        state.active_column_label_transform = null;
        state.column_label_mouse_down_rect = default;
        state.column_label_mouse_down_position = default;
    }

    private static SceneViewState GetSceneViewState(SceneView sceneView)
    {
        int id = sceneView.GetInstanceID();
        if (!scene_view_states.TryGetValue(id, out SceneViewState state))
        {
            // Closed windows must not retain their Transform and component references.
            foreach (int dead_id in scene_view_states
                .Where(pair => pair.Value.view == null).Select(pair => pair.Key).ToArray())
                scene_view_states.Remove(dead_id);
            state = new SceneViewState { view = sceneView };
            scene_view_states[id] = state;
        }
        return state;
    }

    private static void TrackSceneViewInput(SceneView sceneView, SceneViewState state, Event e)
    {
        // Do not compare camera matrices/pixelRect between GUI event phases.
        // In particular, neither Layout nor Repaint may schedule another repaint.
        // Camera navigation already repaints the SceneView; no mesh pick pass is needed.
        bool pointer_in_view = EditorWindow.mouseOverWindow == sceneView;
        bool pointer_event = e.isMouse || e.type == EventType.ScrollWheel ||
            e.type == EventType.MouseEnterWindow;
        if (pointer_in_view && pointer_event)
        {
            state.mouse_position = e.mousePosition;
            state.has_mouse_position = true;
        }

        if (pointer_in_view && (e.type == EventType.MouseMove ||
            e.type == EventType.MouseEnterWindow))
        {
            sceneView.Repaint();
        }
        else if (e.type == EventType.MouseLeaveWindow && state.has_mouse_position)
        {
            // One repaint to clear the hover highlight; do not start a timer/loop.
            sceneView.Repaint();
        }
    }

    [MenuItem("Tools/Display Child Names/Toggle Enabled")]
    [Shortcut("Display Child Names/Toggle Enabled")]
    private static void ToggleEnabledShortcut()
    {
        SetEnabled(!isEnabled);
    }

    private static void SetEnabled(bool enabled)
    {
        if (isEnabled == enabled)
            return;

        isEnabled = enabled;
        if (!enabled)
        {
            pending_delayed_bone_sphere_selection = null;
            EditorApplication.delayCall -= ApplyDelayedBoneSphereSelection;
            foreach (SceneViewState state in scene_view_states.Values)
            {
                // Release hotControl only from the owning SceneView's GUI callback.
                state.cancel_requested = true;
                state.bones = null;
                state.ordered_bones.Clear();
                state.visible_labels.Clear();
                state.labels.Clear();
                state.column_candidates.Clear();
                state.prefab_columns.Clear();
                clear_column_display(state);
                state.column_set_page = 0;
                state.pending_column_set_page = -1;
                state.column_layout_active = false;
                state.column_source_valid = false;
                state.floating_layout_active = false;
                state.floating_origin_valid = false;
                state.floating_focus_object = null;
                state.column_content_dirty = true;
                state.bone_cache_dirty = true;
            }
        }

        SaveSessionState();
        SceneView.RepaintAll();
    }

    private static void CompleteActiveBoneSphereHandleIfNeeded(
        SceneView sceneView, SceneViewState state, Event e)
    {
        if (state.active_control_id == 0)
        {
            state.cancel_requested = false;
            return;
        }

        if (GUIUtility.hotControl != state.active_control_id)
        {
            // Capture was lost. Never release a control owned by a different tool.
            ClearActiveBoneSphere(state);
            return;
        }

        bool escape = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        if (!isEnabled || !show_bone_spheres || !enable_bone_sphere_click_selection ||
            state.cancel_requested || state.active_bone == null ||
            e.type == EventType.Ignore || escape)
        {
            GUIUtility.hotControl = 0;
            ClearActiveBoneSphere(state);
            if (escape)
                e.Use();
            sceneView.Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            state.dragged |= (e.mousePosition - state.mouse_down_position).sqrMagnitude > 16f;
            e.Use();
            return;
        }

        if (e.rawType != EventType.MouseUp || e.button != 0)
            return;

        Transform selected_bone = state.active_bone;
        bool is_click = !state.dragged && !e.alt &&
            (e.mousePosition - state.mouse_down_position).sqrMagnitude <= 16f;
        GUIUtility.hotControl = 0;
        ClearActiveBoneSphere(state);
        if (is_click)
            ScheduleBoneSphereSelection(selected_bone);
        e.Use();
        sceneView.Repaint();
    }

    private static void ClearActiveBoneSphere(SceneViewState state)
    {
        state.active_control_id = 0;
        state.active_bone = null;
        state.dragged = false;
        state.cancel_requested = false;
    }

    private static void MarkPhysBoneRootReferenceCacheDirty()
    {
        phys_bone_root_reference_cache_dirty = true;
        foreach (SceneViewState state in scene_view_states.Values)
        {
            state.bone_cache_dirty = true;
            state.column_content_dirty = true;
        }
        SceneView.RepaintAll();
    }

    private static void refresh_scene_transform_cache(SceneViewState state)
    {
        StageHandle current_stage = StageUtility.GetCurrentStageHandle();
        bool stage_changed = state.transform_stage != current_stage;
        if (!state.bone_cache_dirty && !stage_changed)
            return;

        // Includes auxiliary/unweighted Transforms and objects without any renderer.
        // Stage scoping also keeps Prefab Mode separate from the main scene.
        Transform[] transforms = current_stage.FindComponentsOfType<Transform>();
        state.ordered_bones.Clear();
        state.ordered_bones.AddRange(transforms.Where(bone => bone != null &&
            bone.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(bone) &&
            (bone.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0)
            .OrderBy(bone => bone.GetInstanceID()));
        state.bones = new HashSet<Transform>(state.ordered_bones);
        state.transform_stage = current_stage;
        state.bone_cache_dirty = false;
        state.column_content_dirty = true;
        if (stage_changed)
        {
            state.column_candidates.Clear();
            state.prefab_columns.Clear();
            clear_column_display(state);
            state.column_set_page = 0;
            state.pending_column_set_page = -1;
            state.column_source_valid = false;
            state.floating_origin_valid = false;
            state.floating_focus_object = null;
        }
    }

    private static bool is_visible_transform(Transform bone)
    {
        return bone != null && bone.gameObject.activeInHierarchy &&
            (bone.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0 &&
            !SceneVisibilityManager.instance.IsHidden(bone.gameObject) &&
            (Tools.visibleLayers & (1 << bone.gameObject.layer)) != 0;
    }

    private static bool IsDepthVisible(Camera camera, Vector3 world_position, out float depth)
    {
        depth = camera.WorldToScreenPoint(world_position).z;
        return !float.IsNaN(depth) && !float.IsInfinity(depth) &&
            depth >= camera.nearClipPlane && depth <= camera.farClipPlane &&
            (maxDepth <= 0f || depth <= maxDepth);
    }

    private static bool try_get_bone_segment(Camera camera, Transform bone, HashSet<Transform> targets,
        out Vector3 start, out Vector3 end)
    {
        start = end = default;
        if (!is_visible_transform(bone) || bone.parent == null ||
            !targets.Contains(bone.parent) || !is_visible_transform(bone.parent))
            return false;
        Vector3 parent_position = bone.parent.position;
        Vector3 child_position = bone.position;
        if ((child_position - parent_position).sqrMagnitude < 0.00000001f)
            return false;
        float parent_depth = camera.WorldToScreenPoint(parent_position).z;
        float child_depth = camera.WorldToScreenPoint(child_position).z;
        if (float.IsNaN(parent_depth) || float.IsNaN(child_depth) ||
            float.IsInfinity(parent_depth) || float.IsInfinity(child_depth))
            return false;
        float near_depth = camera.nearClipPlane;
        float far_depth = maxDepth > 0f ? Mathf.Min(maxDepth, camera.farClipPlane) : camera.farClipPlane;
        if (far_depth < near_depth)
            return false;
        float depth_delta = child_depth - parent_depth;
        float start_t = 0f;
        float end_t = 1f;
        if (Mathf.Abs(depth_delta) < 0.000001f)
        {
            if (parent_depth < near_depth || parent_depth > far_depth)
                return false;
        }
        else
        {
            float near_t = (near_depth - parent_depth) / depth_delta;
            float far_t = (far_depth - parent_depth) / depth_delta;
            start_t = Mathf.Max(0f, Mathf.Min(near_t, far_t));
            end_t = Mathf.Min(1f, Mathf.Max(near_t, far_t));
            if (start_t >= end_t)
                return false;
        }
        // Clip a crossing bone instead of dropping the visible half or drawing behind the camera.
        start = Vector3.Lerp(parent_position, child_position, start_t);
        end = Vector3.Lerp(parent_position, child_position, end_t);
        return true;
    }

    private static void draw_bone_hierarchy(SceneView scene_view, SceneViewState state)
    {
        if (!show_bone_hierarchy || Event.current.type != EventType.Repaint ||
            scene_view.camera == null || state.bones == null)
            return;
        Color previous_color = Handles.color;
        CompareFunction previous_z_test = Handles.zTest;
        try
        {
            Handles.zTest = CompareFunction.Always;
            foreach (Transform bone in state.ordered_bones)
            {
                if (!try_get_bone_segment(scene_view.camera, bone, state.bones,
                    out bone_line_points[0], out bone_line_points[1]))
                    continue;
                bool selected = Selection.Contains(bone.gameObject) || Selection.Contains(bone.parent.gameObject);
                Handles.color = selected ? Handles.selectedColor : new Color(0.3f, 0.85f, 1f, 0.55f);
                Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, bone_line_points);
            }
        }
        finally
        {
            Handles.color = previous_color;
            Handles.zTest = previous_z_test;
        }
    }

    private static void DrawBoneSpheres(SceneView sceneView, SceneViewState state, int control_id)
    {
        // Input/Layout computations need no render-state changes.
        if (Event.current.type != EventType.Repaint)
        {
            DrawBoneSpheresInternal(sceneView, state, control_id);
            return;
        }

        Color previous_color = Handles.color;
        CompareFunction previous_z_test = Handles.zTest;
        try
        {
            Handles.zTest = CompareFunction.Always;
            // Do not force Handles.lighting for the whole SceneView callback.
            DrawBoneSpheresInternal(sceneView, state, control_id);
        }
        finally
        {
            Handles.color = previous_color;
            Handles.zTest = previous_z_test;
        }
    }

    private static void DrawBoneSpheresInternal(SceneView sceneView, SceneViewState state, int control_id)
    {
        if (!show_bone_spheres || state.bones == null || sceneView.camera == null)
        {
            state.candidate_bone = null;
            return;
        }

        Event e = Event.current;
        state.spheres.Clear();
        Transform candidate = null;
        float best_pick_distance = float.PositiveInfinity;
        float best_center_distance = float.PositiveInfinity;
        float best_depth = float.PositiveInfinity;
        bool over_label = state.labels.Any(label => label.block_rect.Contains(state.mouse_position)) ||
            IsPointerOverColumns(state, state.mouse_position);
        bool may_pick = enable_bone_sphere_click_selection && state.has_mouse_position &&
            EditorWindow.mouseOverWindow == sceneView && GUIUtility.hotControl == 0 &&
            !e.alt && !Tools.viewToolActive && !over_label;

        foreach (Transform bone in state.ordered_bones)
        {
            if (!is_visible_transform(bone))
                continue;

            Vector3 position = bone.position;
            if (!IsDepthVisible(sceneView.camera, position, out float depth))
                continue;
            Vector2 gui_position = HandleUtility.WorldToGUIPoint(position);
            if (float.IsNaN(gui_position.x) || float.IsNaN(gui_position.y) ||
                float.IsInfinity(gui_position.x) || float.IsInfinity(gui_position.y))
                continue;

            float center_distance = state.has_mouse_position
                ? Vector2.Distance(state.mouse_position, gui_position) : float.PositiveInfinity;
            if (show_bone_spheres_near_cursor_only && center_distance > cursor_radius_pixels &&
                bone != state.active_bone)
                continue;

            float size = HandleUtility.GetHandleSize(position) * bone_sphere_size;
            if (size <= 0f || float.IsNaN(size) || float.IsInfinity(size))
                continue;
            // Deliberately use a tighter circle than the old size * 1.4 hit area.
            // DistanceToCircle takes a world-space radius; add the GUI-space padding below.
            float pick_distance = Mathf.Max(0f,
                HandleUtility.DistanceToCircle(position, size * 0.5f) - bone_sphere_pick_padding_pixels);
            state.spheres.Add(new BoneSphereDrawInfo
            {
                bone = bone, position = position, size = size, depth = depth,
                center_distance = center_distance, pick_distance = pick_distance
            });

            if (may_pick && (pick_distance < best_pick_distance ||
                (Mathf.Approximately(pick_distance, best_pick_distance) &&
                 (center_distance < best_center_distance ||
                  (Mathf.Approximately(center_distance, best_center_distance) && depth < best_depth)))))
            {
                candidate = bone;
                best_pick_distance = pick_distance;
                best_center_distance = center_distance;
                best_depth = depth;
            }
        }

        if (e.type == EventType.Layout)
        {
            // Only visible spheres are pickable. No invisible AddDefaultControl is used.
            state.candidate_bone = candidate != null && best_pick_distance <= 0f ? candidate : null;
            if (state.candidate_bone != null)
            {
                // Let a standard transform handle with distance 0 win an overlap.
                HandleUtility.AddControl(control_id, 0.01f);
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && may_pick &&
            state.candidate_bone != null && state.candidate_bone == candidate &&
            best_pick_distance <= 0f && HandleUtility.nearestControl == control_id)
        {
            GUIUtility.hotControl = control_id;
            state.active_control_id = control_id;
            state.active_bone = state.candidate_bone;
            state.mouse_down_position = e.mousePosition;
            state.dragged = false;
            e.Use();
            sceneView.Repaint();
        }

        if (e.type != EventType.Repaint)
            return;

        // Draw each sphere once. The highlighted sphere is sorted last rather than
        // drawing a second transparent copy over it.
        Transform highlighted_bone = state.active_bone != null ? state.active_bone :
            (may_pick && HandleUtility.nearestControl == control_id ? state.candidate_bone : null);
        state.spheres.Sort((a, b) =>
        {
            bool a_highlighted = highlighted_bone != null && a.bone == highlighted_bone;
            bool b_highlighted = highlighted_bone != null && b.bone == highlighted_bone;
            if (a_highlighted != b_highlighted)
                return a_highlighted ? 1 : -1;
            int depth_order = b.depth.CompareTo(a.depth);
            return depth_order != 0 ? depth_order : a.bone.GetInstanceID().CompareTo(b.bone.GetInstanceID());
        });
        foreach (BoneSphereDrawInfo sphere in state.spheres)
        {
            bool selected = Selection.Contains(sphere.bone.gameObject);
            bool hovered = may_pick && HandleUtility.nearestControl == control_id &&
                state.candidate_bone == sphere.bone;
            bool pressed = state.active_bone == sphere.bone && GUIUtility.hotControl == state.active_control_id;
            Handles.color = selected || pressed ? Handles.selectedColor :
                (hovered ? Handles.preselectionColor : new Color(labelColor.r, labelColor.g, labelColor.b, 0.8f));
            Handles.SphereHandleCap(0, sphere.position, Quaternion.identity, sphere.size, EventType.Repaint);
        }

    }

    private static void ScheduleBoneSphereSelection(Transform bone)
    {
        if (bone == null)
        {
            return;
        }

        pending_delayed_bone_sphere_selection = bone;
        EditorApplication.delayCall -= ApplyDelayedBoneSphereSelection;
        EditorApplication.delayCall += ApplyDelayedBoneSphereSelection;
    }

    private static void ApplyDelayedBoneSphereSelection()
    {
        Transform bone = pending_delayed_bone_sphere_selection;
        pending_delayed_bone_sphere_selection = null;
        if (bone == null || !isEnabled || !show_bone_spheres || !enable_bone_sphere_click_selection)
            return;
        SelectGameObject(bone.gameObject);
        SceneView.RepaintAll();
    }

    private static void SelectGameObject(GameObject gameObject)
    {
        if (gameObject != null)
            Selection.activeGameObject = gameObject;
    }

    // Compact settings UI. Existing fields/keys are retained; v6 adds page count and optional height cap.
    // Sizes below are GUI points; Unity applies the editor's display scaling.
    internal const float SettingsPanelWidth = 340f;
    private const float settings_horizontal_padding = 8f;
    private const float settings_label_width = 128f;
    private const float settings_numeric_width = 64f;
    private static int settings_tab;
    private static int pending_settings_tab = -1;
    private static readonly GUIContent[] settings_tabs =
    {
        new GUIContent("表示範囲", "検出範囲・最大深度・スフィア・PhysBone系表示"),
        new GUIContent("ラベル配置", "プレハブ別カラム・近傍パネル・距離・幅・余白"),
        new GUIContent("文字・色", "文字サイズ・色・透明度")
    };

    internal static void DrawOverlayContent()
    {
        // These IMGUI settings are global. Never leak them into another Overlay/Inspector.
        float previous_label_width = EditorGUIUtility.labelWidth;
        float previous_field_width = EditorGUIUtility.fieldWidth;
        bool previous_wide_mode = EditorGUIUtility.wideMode;
        int previous_indent = EditorGUI.indentLevel;
        bool settings_changed = false;
        bool reset_requested = false;
        bool refresh_requested = false;
        try
        {
            EditorGUIUtility.labelWidth = settings_label_width;
            EditorGUIUtility.fieldWidth = settings_numeric_width;
            EditorGUIUtility.wideMode = true;
            EditorGUI.indentLevel = 0;

            // Keep the old tab's controls for the click event. Switch at the next Layout.
            if (Event.current != null && Event.current.type == EventType.Layout && pending_settings_tab >= 0)
            {
                settings_tab = Mathf.Clamp(pending_settings_tab, 0, settings_tabs.Length - 1);
                pending_settings_tab = -1;
            }

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                using (new EditorGUILayout.VerticalScope(
                    GUILayout.Width(SettingsPanelWidth), GUILayout.ExpandWidth(false)))
                {
                    GUILayout.Space(3f);
                    Rect header = GetCompactSettingsRow();
                    bool enabled = EditorGUI.ToggleLeft(
                        new Rect(header.x, header.y, 84f, header.height),
                        new GUIContent("有効", "ボーン名・ラベル・階層線・スフィア表示を切り替えます。"), isEnabled);
                    if (enabled != isEnabled)
                        SetEnabled(enabled);
                    using (new EditorGUI.DisabledScope(!two_column_layout))
                    {
                        pin_column_labels = EditorGUI.ToggleLeft(
                            new Rect(header.x + 104f, header.y, header.width - 104f, header.height),
                            new GUIContent("ラベル固定", "現在の表示対象と配置を保持します。プレハブ別カラム用です。"),
                            pin_column_labels);
                    }

                    showSettings = EditorGUI.Foldout(GetCompactSettingsRow(), showSettings,
                        new GUIContent("詳細設定", "タブで設定を切り替えます。説明は各項目にマウスを置くと表示されます。"), true);
                    if (showSettings)
                    {
                        int next_tab = GUI.Toolbar(GetCompactSettingsRow(22f), settings_tab, settings_tabs);
                        if (next_tab != settings_tab)
                            pending_settings_tab = next_tab;
                        GUILayout.Space(4f);

                        switch (settings_tab)
                        {
                            case 0:
                                DrawCompactDisplaySettings();
                                break;
                            case 1:
                                DrawCompactPlacementSettings();
                                break;
                            default:
                                DrawCompactAppearanceSettings();
                                break;
                        }

                        // Preserve existing ranges; page count is an integer in [1, 30].
                        ClampAlphaRange();
                        label_hold_radius_pixels = Mathf.Clamp(label_hold_radius_pixels, 16f, 480f);
                        maxDepth = SanitizeMaxDepth(maxDepth);
                        bone_sphere_size = Mathf.Clamp(bone_sphere_size, 0.01f, max_bone_sphere_size);
                        SanitizeColumnSettings();

                        GUILayout.Space(5f);
                        Rect actions = GetCompactSettingsRow(22f);
                        float button_width = (actions.width - 6f) * 0.5f;
                        refresh_requested = GUI.Button(new Rect(actions.x, actions.y, button_width, actions.height),
                            new GUIContent("参照キャッシュ更新", "PhysBoneのRootRef参照を再検出します。表示設定は変更しません。"));
                        reset_requested = GUI.Button(new Rect(actions.xMax - button_width, actions.y, button_width, actions.height),
                            new GUIContent("設定リセット", "表示・配置・色など、すべてのツール設定を初期値に戻します。"));
                    }
                    GUILayout.Space(3f);
                }
                settings_changed = check.changed;
            }
        }
        finally
        {
            EditorGUIUtility.labelWidth = previous_label_width;
            EditorGUIUtility.fieldWidth = previous_field_width;
            EditorGUIUtility.wideMode = previous_wide_mode;
            EditorGUI.indentLevel = previous_indent;
        }

        if (reset_requested)
        {
            ResetSettings();
            settings_tab = 0;
            pending_settings_tab = -1;
            settings_changed = true;
        }
        if (refresh_requested)
            MarkPhysBoneRootReferenceCacheDirty();
        if (settings_changed)
        {
            foreach (SceneViewState state in scene_view_states.Values)
            {
                state.column_content_dirty = true;
                if (!show_bone_spheres || !enable_bone_sphere_click_selection)
                    state.cancel_requested = true;
            }
            SaveSessionState();
            SceneView.RepaintAll(); // A settings edit only, never a camera/repaint feedback loop.
        }
    }

    private static void DrawCompactDisplaySettings()
    {
        cursor_radius_pixels = CompactSettingsSlider("カーソル半径(px)", cursor_radius_pixels, 8f, 240f,
            "カーソル周辺とみなす範囲。スフィアの近傍表示にも使用します。");
        label_hold_radius_pixels = CompactSettingsSlider("ラベル保持半径(px)", label_hold_radius_pixels, 16f, 480f,
            "ラベル候補を拾う範囲。狭くすると一度に表示する候補が減ります。");
        maxDepth = CompactSettingsFloat("最大深度 (0=無制限)", maxDepth,
            "カメラ前方への距離（Unity単位）。モデル表面からの奥行きではありません。0でこの距離制限を無効にします。");
        show_vrc_phys_bone_components = CompactSettingsToggle("PhysBone系を表示", show_vrc_phys_bone_components,
            "VRCPhysBone・VRCPhysBoneCollider・RootRefとチェックボックスを表示します。");
        GUILayout.Space(3f);
        show_bone_hierarchy = CompactSettingsToggle("ボーン階層（親子線）", show_bone_hierarchy,
            "シーン内のTransformの親子を線で結びます。未使用・補助ボーンを含み、SkinnedMeshRendererで絞り込みません。");
        show_bone_spheres = CompactSettingsToggle("ボーン位置スフィア", show_bone_spheres,
            "シーン内のTransformの位置にスフィアを描画します。");
        using (new EditorGUI.DisabledScope(!show_bone_spheres))
        {
            show_bone_spheres_near_cursor_only = CompactSettingsToggle("カーソル周辺のみ表示", show_bone_spheres_near_cursor_only,
                "スフィアの表示をカーソル半径内に限定します。");
            enable_bone_sphere_click_selection = CompactSettingsToggle("スフィアクリックで選択", enable_bone_sphere_click_selection,
                "表示中のスフィアを左クリックすると、そのボーンを選択します。");
            bone_sphere_size = CompactSettingsSlider("スフィアサイズ", bone_sphere_size, 0.01f, max_bone_sphere_size,
                "スフィアの描画サイズ。表示倍率に応じたハンドルサイズへの倍率です。");
        }
    }

    private static void DrawCompactPlacementSettings()
    {
        two_column_layout = CompactSettingsToggle("プレハブごとにカラム配置", two_column_layout,
            "プレハブ1つにつき1列で表示します。画面に収まらない列は上部の < > で切り替えます。\nOFFで従来のボーン近傍ラベル配置に戻します。");
        if (two_column_layout)
        {
            column_items_per_page = EditorGUI.IntSlider(
                CompactSettingsFieldRect("1列の表示数", "1列・1ページの上限（1〜30件）。ボーン名1つを1件とし、PhysBoneなどの行は件数に含めません。\n超過分は各列上部の < > で切り替えます。画面に収まらない場合は、この件数より手前で分割します。"),
                column_items_per_page, 1, max_column_items_per_page);
            floating_column_layout = CompactSettingsToggle("ボーン近くにまとめる", floating_column_layout,
                "ONで各列を近傍パネルにまとめ、OFFで画面端に配置します。\nパネルへ移動中とパネル上では対象と配置を保持します。");
            if (floating_column_layout)
            {
                floating_panel_offset = CompactSettingsPair("パネル距離(px)", floating_panel_offset, "X", "Y",
                    "正で右／下、負で左／上を優先。画面外に出る場合は反転・補正します。");
                floating_limit_height = CompactSettingsToggle("高さ上限も使う", floating_limit_height,
                    "OFFでは表示件数に合わせてパネルを縦に伸ばします（画面内に収まる範囲）。\nONでは保存済みのピクセル高さでも制限します。ページ切り替えだけではパネルの高さは変わりません。");
                if (floating_limit_height)
                    floating_panel_max_height = CompactSettingsSlider("パネル高さ上限(px)", floating_panel_max_height, 120f, 800f,
                        "表示件数と高さのうち、先に上限へ達した位置でページ分割します。");
                floating_column_spacing = CompactSettingsSlider("カラム間隔(px)", floating_column_spacing, 2f, 32f);
                floating_all_leaders = CompactSettingsToggle("全ラベルの引出線", floating_all_leaders,
                    "OFFでは通常1本。ラベル上にマウスを置くと対応先が切り替わります。");
            }
            column_width = CompactSettingsSlider("カラム幅(px)", column_width, 160f, 360f,
                "設定Overlayではなく、シーン内に表示するラベル1列分の幅です。");
            column_gap = CompactSettingsSlider("ラベル間隔(px)", column_gap, 2f, 24f);
            column_inset = CompactSettingsSlider("左右の余白(px)", column_inset, 0f, 120f,
                "Sceneビューの左右端から確保する安全余白です。");
            column_vertical_margins = CompactSettingsPair("上下の余白(px)", column_vertical_margins, "上", "下",
                "Sceneビュー上端／下端からの安全余白。ほかのOverlayと重なる場合に調整します。");
        }
        if (!two_column_layout)
            guiOffset = CompactSettingsPair("表示オフセット(px)", guiOffset, "X", "Y");
        else if (!floating_column_layout)
            guiOffset.y = CompactSettingsFloat("縦オフセット(px)", guiOffset.y,
                "画面端のカラムではYだけ使用します。Xの保存値は変更しません。");
    }

    private static void DrawCompactAppearanceSettings()
    {
        Rect size_rect = CompactSettingsFieldRect("文字サイズ", "シーン内のラベル文字サイズ。設定Overlayの文字サイズは変えません。");
        fontSize = EditorGUI.IntSlider(size_rect, fontSize, 8, 24);
        Rect color_rect = CompactSettingsFieldRect("文字色", "ラベルと引出線の色です。");
        labelColor = EditorGUI.ColorField(color_rect, labelColor);
        Vector2 alpha_range = CompactSettingsPair("透明度", new Vector2(minAlpha, maxAlpha), "最小", "最大",
            "0=透明、1=不透明。候補までの距離に応じた透明度範囲です。");
        minAlpha = alpha_range.x;
        maxAlpha = alpha_range.y;
        ClampAlphaRange();
        EditorGUI.MinMaxSlider(GetCompactSettingsRow(), ref minAlpha, ref maxAlpha, 0f, 1f);
    }

    private static Rect GetCompactSettingsRow(float height = -1f)
    {
        if (height <= 0f)
            height = EditorGUIUtility.singleLineHeight;
        Rect rect = EditorGUILayout.GetControlRect(false, height,
            GUILayout.Width(SettingsPanelWidth), GUILayout.ExpandWidth(false));
        rect.xMin += settings_horizontal_padding;
        rect.xMax -= settings_horizontal_padding;
        return rect;
    }

    private static Rect CompactSettingsFieldRect(string label, string tooltip = "")
    {
        Rect row = GetCompactSettingsRow();
        EditorGUI.LabelField(new Rect(row.x, row.y, settings_label_width, row.height), new GUIContent(label, tooltip));
        row.xMin += settings_label_width + 4f;
        return row;
    }

    private static float CompactSettingsSlider(string label, float value, float min, float max, string tooltip = "")
    {
        // fieldWidth controls the numeric box on this slider; the row itself is also bounded.
        return EditorGUI.Slider(CompactSettingsFieldRect(label, tooltip), value, min, max);
    }

    private static float CompactSettingsFloat(string label, float value, string tooltip = "")
    {
        Rect field = CompactSettingsFieldRect(label, tooltip);
        field.xMin = field.xMax - settings_numeric_width;
        return EditorGUI.FloatField(field, value);
    }

    private static bool CompactSettingsToggle(string label, bool value, string tooltip = "")
    {
        return EditorGUI.ToggleLeft(GetCompactSettingsRow(), new GUIContent(label, tooltip), value);
    }

    private static Vector2 CompactSettingsPair(string label, Vector2 value, string firstLabel, string secondLabel, string tooltip = "")
    {
        Rect field = CompactSettingsFieldRect(label, tooltip);
        const float axis_width = 24f;
        const float gap = 6f;
        float pair_width = axis_width + settings_numeric_width;
        float start = field.xMax - pair_width * 2f - gap;
        EditorGUI.LabelField(new Rect(start, field.y, axis_width, field.height), new GUIContent(firstLabel, tooltip), EditorStyles.miniLabel);
        value.x = EditorGUI.FloatField(new Rect(start + axis_width, field.y, settings_numeric_width, field.height), value.x);
        start += pair_width + gap;
        EditorGUI.LabelField(new Rect(start, field.y, axis_width, field.height), new GUIContent(secondLabel, tooltip), EditorStyles.miniLabel);
        value.y = EditorGUI.FloatField(new Rect(start + axis_width, field.y, settings_numeric_width, field.height), value.y);
        return value;
    }

    // シーンビューでのオブジェクト名表示処理
    [MenuItem("Tools/Display Child Names/Toggle Label Lock")]
    [Shortcut("Display Child Names/Toggle Label Lock")]
    private static void ToggleColumnLabelLock()
    {
        pin_column_labels = !pin_column_labels;
        SceneView.RepaintAll();
    }

    private static float FiniteClamp(float value, float fallback, float min, float max)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    private static void SanitizeColumnSettings()
    {
        column_items_per_page = Mathf.Clamp(column_items_per_page, 1, max_column_items_per_page);
        column_width = FiniteClamp(column_width, 240f, 160f, 360f);
        column_gap = FiniteClamp(column_gap, 6f, 2f, 24f);
        column_inset = FiniteClamp(column_inset, 12f, 0f, 120f);
        column_vertical_margins.x = FiniteClamp(column_vertical_margins.x, 48f, 0f, 240f);
        column_vertical_margins.y = FiniteClamp(column_vertical_margins.y, 20f, 0f, 240f);
        floating_panel_offset.x = FiniteClamp(floating_panel_offset.x, 48f, -800f, 800f);
        floating_panel_offset.y = FiniteClamp(floating_panel_offset.y, 40f, -800f, 800f);
        floating_panel_max_height = FiniteClamp(floating_panel_max_height, 360f, 120f, 800f);
        floating_column_spacing = FiniteClamp(floating_column_spacing, 8f, 2f, 32f);
    }

    private static bool IsPointerOverColumns(SceneViewState state, Vector2 point)
    {
        if (!two_column_layout || !state.column_layout_active || state.column_layout_too_small)
            return false;
        if (state.floating_layout_active)
            return state.column_candidates.Count > 0 && state.floating_panel_rect.Contains(point);
        return state.column_set_pager_rect.Contains(point) ||
            state.visible_columns.Any(column => column.panel_rect.Contains(point));
    }

    private static bool ShouldHoldColumnTargets(SceneViewState state)
    {
        if (!two_column_layout || state.column_candidates.Count == 0)
            return false;
        if (pin_column_labels)
            return true;
        if (Tools.viewToolActive || (Event.current != null && Event.current.alt))
            return false;
        Vector2 mouse = state.mouse_position;
        if (IsPointerOverColumns(state, mouse))
            return true;
        if (floating_column_layout && state.floating_layout_active)
            return IsInFloatingTransferCorridor(mouse, state);
        if (!state.column_source_valid || state.column_source_rect.Contains(mouse))
            return false;
        // Widening corridors connect the cluster's edge to the full column.
        // Inside the source rectangle, normal hover inspection always resumes.
        return state.visible_columns.Any(column => IsInColumnTransferCorridor(mouse,
            state.column_source_rect, column, column.panel_rect.center.x < state.column_source_rect.center.x));
    }

    private static bool IsInColumnTransferCorridor(Vector2 point, Rect source, LabelColumn column, bool left)
    {
        if (column.pages.Count == 0)
            return false;
        float from_x = left ? source.xMin : source.xMax;
        float to_x = left ? column.panel_rect.xMax : column.panel_rect.xMin;
        if (left ? to_x >= from_x : to_x <= from_x)
            return false;
        if (point.x < Mathf.Min(from_x, to_x) || point.x > Mathf.Max(from_x, to_x))
            return false;
        float t = Mathf.InverseLerp(from_x, to_x, point.x);
        float top = Mathf.Lerp(source.yMin, column.panel_rect.yMin, t) - 12f;
        float bottom = Mathf.Lerp(source.yMax, column.panel_rect.yMax, t) + 12f;
        return point.y >= top && point.y <= bottom;
    }

    private static Rect GetColumnViewport(SceneView view)
    {
        // OnSceneGUI runs inside the camera viewport group. Its local origin is 0,0.
        // cameraViewport is in GUI points, unlike camera.pixelRect on HiDPI displays.
#if UNITY_2021_2_OR_NEWER
        Vector2 size = view.cameraViewport.size;
#else
        Vector2 size = new Vector2(view.position.width, Mathf.Max(0f, view.position.height - 22f));
#endif
        if (float.IsNaN(size.x) || float.IsNaN(size.y) ||
            float.IsInfinity(size.x) || float.IsInfinity(size.y) || size.x <= 0f || size.y <= 0f)
            return new Rect();
        return new Rect(Vector2.zero, size);
    }

    private static void RebuildLabelLayout(SceneView sceneView, SceneViewState state)
    {
        if (!two_column_layout)
        {
            RebuildLocalLabelLayout(sceneView, state);
            return;
        }
        rebuild_prefab_column_layout(sceneView, state, floating_column_layout);
    }

    private static bool RefreshColumnCandidates(SceneView sceneView, SceneViewState state, Rect viewport, bool hold_targets)
    {
        bool reset_pages = false;
        if (!hold_targets)
        {
            var next = new List<LabelCandidate>();
            foreach (GameObject go in GetDisplayTargetObjects(state))
            {
                if (go == null || !go.activeInHierarchy ||
                    !IsDepthVisible(sceneView.camera, go.transform.position, out float depth))
                    continue;
                Vector2 anchor = HandleUtility.WorldToGUIPoint(go.transform.position);
                if (!viewport.Contains(anchor))
                    continue;
                float distance = Vector2.Distance(state.mouse_position, anchor);
                if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > label_hold_radius_pixels)
                    continue;
                float alpha = Mathf.Lerp(maxAlpha, minAlpha, distance / Mathf.Max(1f, label_hold_radius_pixels));
                next.Add(new LabelCandidate
                {
                    gameObject = go, anchor = anchor, alpha = alpha,
                    prefab_root = get_prefab_group_root(go),
                    components = GetDisplayComponentSnapshot(go, alpha)
                });
            }
            // GetDisplayTargetObjects is instance-ID ordered in v2. Compare identities,
            // not the cursor-dependent alpha, to avoid resetting pages on tiny motion.
            reset_pages = next.Count != state.column_candidates.Count;
            if (!reset_pages)
                for (int i = 0; i < next.Count; i++)
                    if (next[i].gameObject != state.column_candidates[i].gameObject ||
                        next[i].prefab_root != state.column_candidates[i].prefab_root ||
                        next[i].components.Count != state.column_candidates[i].components.Count)
                    {
                        reset_pages = true;
                        break;
                    }
            state.column_candidates.Clear();
            state.column_candidates.AddRange(next);
        }
        else
        {
            reset_pages = state.column_candidates.RemoveAll(c => c.gameObject == null || !is_visible_transform(c.gameObject.transform)) > 0;
            foreach (LabelCandidate c in state.column_candidates)
            {
                Vector2 anchor = HandleUtility.WorldToGUIPoint(c.gameObject.transform.position);
                if (!float.IsNaN(anchor.x) && !float.IsNaN(anchor.y) &&
                    !float.IsInfinity(anchor.x) && !float.IsInfinity(anchor.y))
                    c.anchor = anchor;
                if (state.column_content_dirty)
                {
                    c.components = GetDisplayComponentSnapshot(c.gameObject, c.alpha);
                    GameObject prefab_root = get_prefab_group_root(c.gameObject);
                    reset_pages |= prefab_root != c.prefab_root;
                    c.prefab_root = prefab_root;
                }
            }
        }
        state.column_content_dirty = false;

        return reset_pages;
    }

    private static void rebuild_prefab_column_layout(SceneView view, SceneViewState state, bool floating)
    {
        if (view.camera == null || !state.has_mouse_position)
            return;
        Rect viewport = GetColumnViewport(view);
        bool changing_columns = state.pending_column_set_page >= 0;
        bool hold = changing_columns || ShouldHoldColumnTargets(state);
        bool geometry_changed = viewport != state.column_viewport || !state.column_layout_active ||
            state.floating_layout_active != floating;
        bool stale = state.column_candidates.Any(c => c.gameObject == null || !is_visible_transform(c.gameObject.transform));
        if (hold && !changing_columns && !geometry_changed && !state.column_content_dirty && !stale && !state.column_layout_too_small)
        {
            ApplyVisibleColumnPages(state);
            return;
        }

        SanitizeColumnSettings();
        if (state.floating_layout_active != floating)
            state.floating_origin_valid = false;
        state.column_viewport = viewport;
        state.column_layout_active = true;
        state.floating_layout_active = floating;
        bool reset_pages = RefreshColumnCandidates(view, state, viewport, hold);
        refresh_prefab_columns(state, reset_pages);
        if (changing_columns)
        {
            state.column_set_page = state.pending_column_set_page;
            state.pending_column_set_page = -1;
        }
        layout_prefab_columns(state, viewport, floating, hold);
    }

    private static void refresh_prefab_columns(SceneViewState state, bool reset_pages)
    {
        var previous_order = new Dictionary<int, int>();
        var previous_columns = new Dictionary<int, LabelColumn>();
        for (int i = 0; i < state.prefab_columns.Count; i++)
        {
            LabelColumn column = state.prefab_columns[i];
            previous_order[column.prefab_key] = i;
            previous_columns[column.prefab_key] = column;
        }
        var next = new List<LabelColumn>();
        foreach (var group in state.column_candidates.GroupBy(c => c.prefab_root != null ? c.prefab_root.GetInstanceID() : 0))
        {
            if (!previous_columns.TryGetValue(group.Key, out LabelColumn column))
                column = new LabelColumn { prefab_key = group.Key };
            column.prefab_root = group.First().prefab_root;
            column.candidates.Clear();
            column.candidates.AddRange(group);
            if (reset_pages)
                column.page_index = 0;
            next.Add(column);
        }
        // Keep existing columns in place as bones move; insert new groups in spatial order.
        next = next.OrderBy(column => previous_order.TryGetValue(column.prefab_key, out int order) ? order : int.MaxValue)
            .ThenBy(column => column.candidates.Min(c => Mathf.RoundToInt(c.anchor.x / 8f)))
            .ThenBy(column => column.prefab_key).ToList();
        if (!next.Select(column => column.prefab_key).SequenceEqual(state.prefab_columns.Select(column => column.prefab_key)))
            state.column_set_page = 0;
        state.prefab_columns.Clear();
        state.prefab_columns.AddRange(next);
    }

    private static void layout_prefab_columns(SceneViewState state, Rect viewport, bool floating, bool hold)
    {
        clear_column_display(state);
        state.column_layout_too_small = false;
        if (state.prefab_columns.Count == 0)
        {
            state.column_set_page = 0;
            state.floating_origin_valid = false;
            state.floating_focus_object = null;
            state.column_source_valid = false;
            return;
        }
        Rect safe = new Rect(column_inset, column_vertical_margins.x,
            Mathf.Max(0f, viewport.width - column_inset * 2f),
            Mathf.Max(0f, viewport.height - column_vertical_margins.x - column_vertical_margins.y));
        float padding = floating ? floating_panel_padding : 0f;
        float spacing = floating ? floating_column_spacing : column_center_gap;
        float available_width = Mathf.Max(0f, safe.width - padding * 2f);
        float width = Mathf.Min(column_width, available_width);
        int slots = Mathf.Min(state.prefab_columns.Count,
            Mathf.Max(1, Mathf.FloorToInt((available_width + spacing) / (width + spacing))));
        state.columns_per_page = slots;
        int set_count = Mathf.CeilToInt((float)state.prefab_columns.Count / slots);
        state.column_set_page = Mathf.Clamp(state.column_set_page, 0, set_count - 1);
        float pager_height = set_count > 1 ? column_header_height + column_gap : 0f;
        float overhead = padding * 2f + pager_height + column_header_height + column_gap;
        float height_budget = floating && floating_limit_height ? Mathf.Min(floating_panel_max_height, safe.height) : safe.height;
        float capacity = height_budget - overhead;
        float minimum_body = column_padding * 2f + prefab_header_height + label_height + component_row_height;
        state.column_layout_too_small = width < 140f || capacity < minimum_body;
        if (state.column_layout_too_small)
            return;

        // Measure all sets once so paging either rows or columns cannot move the buttons.
        float desired_body = minimum_body;
        foreach (LabelColumn column in state.prefab_columns)
        {
            ConfigureFloatingColumn(column, 0f, 0f, width, capacity);
            BuildColumnPages(column, column.candidates, false, compact: floating, deferPacking: true);
            desired_body = Mathf.Max(desired_body, GetLargestColumnPageHeight(column));
        }
        float body_height = floating ? Mathf.Min(capacity, desired_body) : capacity;
        float panel_width = floating ? slots * width + (slots - 1) * spacing + padding * 2f : safe.width;
        Rect panel = new Rect(safe.x, safe.y, panel_width, body_height + overhead);
        if (floating)
        {
            UpdateFloatingOrigin(state, hold);
            panel = PlaceFloatingPanel(state.floating_origin, state.floating_query_position, panel.size,
                safe, floating_panel_offset, ref state.floating_quadrant);
            state.floating_panel_rect = panel;
        }
        if (set_count > 1)
            state.column_set_pager_rect = new Rect(panel.x + padding, panel.y + padding,
                panel.width - padding * 2f, column_header_height);
        float stride = !floating && slots > 1 ? (safe.width - width) / (slots - 1) : width + spacing;
        int start = state.column_set_page * slots;
        int end = Mathf.Min(start + slots, state.prefab_columns.Count);
        for (int i = start; i < end; i++)
        {
            LabelColumn column = state.prefab_columns[i];
            ConfigureFloatingColumn(column, panel.x + padding + (i - start) * stride,
                panel.y + padding + pager_height, width, body_height);
            foreach (List<LabelDisplayInfo> page in column.pages)
            {
                PackColumnPage(page, column.body_rect, floating);
                foreach (LabelDisplayInfo label in page)
                    label.left_column = column.panel_rect.center.x < viewport.center.x;
            }
            state.visible_columns.Add(column);
        }
        ApplyVisibleColumnPages(state);

        if (!hold || !state.column_source_valid)
        {
            Vector2 min = state.column_candidates[0].anchor;
            Vector2 max = min;
            foreach (LabelCandidate c in state.column_candidates)
            {
                min = Vector2.Min(min, c.anchor);
                max = Vector2.Max(max, c.anchor);
            }
            state.column_source_valid = true;
            state.column_source_rect = ExpandRect(Rect.MinMaxRect(min.x, min.y, max.x, max.y), 16f);
        }
    }

    private static void clear_column_display(SceneViewState state)
    {
        state.labels.Clear();
        state.visible_labels.Clear();
        state.visible_columns.Clear();
        state.column_set_pager_rect = new Rect();
        state.floating_panel_rect = new Rect();
    }

    private static void UpdateFloatingOrigin(SceneViewState state, bool hold)
    {
        LabelCandidate current = state.column_candidates.Find(c => c.gameObject == state.floating_focus_object);
        bool moved_to_new_area = (state.mouse_position - state.floating_query_position).sqrMagnitude >
            floating_reanchor_distance * floating_reanchor_distance;
        if (!state.floating_origin_valid || current == null || (!hold && moved_to_new_area))
        {
            LabelCandidate closest = null;
            float best = float.PositiveInfinity;
            foreach (LabelCandidate c in state.column_candidates)
            {
                float distance = (c.anchor - state.mouse_position).sqrMagnitude;
                if (distance < best)
                {
                    closest = c;
                    best = distance;
                }
            }
            if (closest == null)
                return;
            current = closest;
            state.floating_focus_object = closest.gameObject;
            state.floating_origin = closest.anchor;
            state.floating_query_position = state.mouse_position;
            state.floating_quadrant = 0;
            state.floating_origin_valid = true;
        }
        if (!hold || !IsFinitePoint(state.floating_origin))
            state.floating_origin = current.anchor;
    }

    private static float GetLargestColumnPageHeight(LabelColumn column)
    {
        float maximum = 0f;
        foreach (List<LabelDisplayInfo> page in column.pages)
        {
            float height = 0f;
            for (int i = 0; i < page.Count; i++)
                height += page[i].block_rect.height + (i > 0 ? column_gap : 0f);
            maximum = Mathf.Max(maximum, height);
        }
        return maximum;
    }

    private static void ConfigureFloatingColumn(LabelColumn column, float x, float y, float width, float bodyHeight)
    {
        column.panel_rect = new Rect(x, y, width, column_header_height + column_gap + bodyHeight);
        column.body_rect = new Rect(x, y + column_header_height + column_gap, width, bodyHeight);
    }

    private static Rect PlaceFloatingPanel(Vector2 anchor, Vector2 query, Vector2 size, Rect safe,
        Vector2 offset, ref int previousQuadrant)
    {
        // Four quadrants, preferred one first. Clamp the PANEL as a unit, never
        // clamp individual labels. Extra cost protects the inspected bone/cursor.
        float dx = Mathf.Abs(offset.x);
        float dy = Mathf.Abs(offset.y);
        Rect anchor_guard = new Rect(anchor.x - 20f, anchor.y - 20f, 40f, 40f);
        Rect pointer_guard = new Rect(query.x - 16f, query.y - 16f, 32f, 32f);
        Rect best = new Rect();
        float best_score = float.PositiveInfinity;
        int best_quadrant = 0;
        for (int i = 0; i < 4; i++)
        {
            bool to_right = (offset.x >= 0f) != ((i & 1) != 0);
            bool to_bottom = (offset.y >= 0f) != ((i & 2) != 0);
            Vector2 desired = new Vector2(to_right ? anchor.x + dx : anchor.x - dx - size.x,
                to_bottom ? anchor.y + dy : anchor.y - dy - size.y);
            Rect rect = new Rect(
                Mathf.Clamp(desired.x, safe.xMin, Mathf.Max(safe.xMin, safe.xMax - size.x)),
                Mathf.Clamp(desired.y, safe.yMin, Mathf.Max(safe.yMin, safe.yMax - size.y)), size.x, size.y);
            float score = (rect.position - desired).sqrMagnitude * 4f + i * 64f +
                RectIntersectionArea(rect, anchor_guard) * 64f + RectIntersectionArea(rect, pointer_guard) * 64f;
            // A small hysteresis prevents a quadrant swap at a near-equal boundary.
            if (i != previousQuadrant)
                score += 32f;
            if (score < best_score)
            {
                best_score = score;
                best = rect;
                best_quadrant = i;
            }
        }
        previousQuadrant = best_quadrant;
        return best;
    }

    private static float RectIntersectionArea(Rect a, Rect b)
    {
        return Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) *
               Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
    }

    private static bool IsInFloatingTransferCorridor(Vector2 point, SceneViewState state)
    {
        if (!state.floating_origin_valid || state.column_layout_too_small || !IsFinitePoint(point))
            return false;
        Vector2 source = state.floating_query_position;
        // Allow normal inspection in a small area around the original source.
        if ((point - source).sqrMagnitude <= 24f * 24f)
            return false;
        Rect rect = ExpandRect(state.floating_panel_rect, 8f);
        if (rect.Contains(point))
            return true;
        Vector2 a = new Vector2(rect.xMin, rect.yMin);
        Vector2 b = new Vector2(rect.xMax, rect.yMin);
        Vector2 c = new Vector2(rect.xMax, rect.yMax);
        Vector2 d = new Vector2(rect.xMin, rect.yMax);
        // The rectangle + these triangles form the convex hull of source and panel.
        // Unlike v3's horizontal corridor this also works for a panel below/above.
        return PointInTriangle(point, source, a, b) || PointInTriangle(point, source, b, c) ||
               PointInTriangle(point, source, c, d) || PointInTriangle(point, source, d, a);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Cross2D(b - a, c - a);
        if (Mathf.Abs(area) < 0.001f)
            return false;
        float d1 = Cross2D(b - a, p - a);
        float d2 = Cross2D(c - b, p - b);
        float d3 = Cross2D(a - c, p - c);
        const float tolerance = 0.01f;
        return area > 0f ? d1 >= -tolerance && d2 >= -tolerance && d3 >= -tolerance
                         : d1 <= tolerance && d2 <= tolerance && d3 <= tolerance;
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool IsFinitePoint(Vector2 point)
    {
        return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
               !float.IsInfinity(point.x) && !float.IsInfinity(point.y);
    }

    private static void DrawFloatingPanelLeader(SceneView view, SceneViewState state)
    {
        if (Event.current.type != EventType.Repaint || state.column_candidates.Count == 0 || !state.floating_origin_valid)
            return;
        GameObject target = state.floating_focus_object;
        bool hovered = false;
        foreach (LabelDisplayInfo label in state.labels)
        {
            if (label.gameObject != null && label.block_rect.Contains(state.mouse_position))
            {
                target = label.gameObject;
                hovered = true;
                break;
            }
        }
        if (target == null || !target.activeInHierarchy ||
            !IsDepthVisible(view.camera, target.transform.position, out float depth))
            return;
        Vector2 anchor = HandleUtility.WorldToGUIPoint(target.transform.position);
        if (!IsFinitePoint(anchor) || state.floating_panel_rect.Contains(anchor))
            return;
        Rect rect = state.floating_panel_rect;
        Vector2 end = new Vector2(Mathf.Clamp(anchor.x, rect.xMin, rect.xMax),
            Mathf.Clamp(anchor.y, rect.yMin, rect.yMax));
        Color previous_color = Handles.color;
        CompareFunction previous_z_test = Handles.zTest;
        try
        {
            Color color = new Color(labelColor.r, labelColor.g, labelColor.b, hovered ? 1f : 0.7f);
            Handles.color = color;
            Handles.zTest = CompareFunction.Always;
            Handles.DrawAAPolyLine(hovered ? 2.5f : 1.5f,
                new Vector3(anchor.x, anchor.y), new Vector3(end.x, end.y));
            EditorGUI.DrawRect(new Rect(anchor.x - 2.5f, anchor.y - 2.5f, 5f, 5f), color);
        }
        finally
        {
            Handles.color = previous_color;
            Handles.zTest = previous_z_test;
        }
    }

    private static float GetColumnCandidateHeight(LabelCandidate c)
    {
        return column_padding * 2f + label_height + c.components.Count * component_row_height;
    }

    private static GameObject get_prefab_group_root(GameObject target)
    {
        // Added GameObjects can lack a prefab connection of their own. They still
        // belong to the nearest instance above them in the Transform hierarchy.
        for (Transform current = target.transform; current != null; current = current.parent)
        {
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(current.gameObject);
            if (root != null)
                return root;
        }
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null && target.scene == stage.scene ? stage.prefabContentsRoot : null;
    }

    private static void BuildColumnPages(LabelColumn column, List<LabelCandidate> candidates, bool left,
        bool compact = false, bool deferPacking = false)
    {
        int item_limit = Mathf.Clamp(column_items_per_page, 1, max_column_items_per_page);
        if (column.page_item_limit != item_limit)
            column.page_index = 0;
        column.page_item_limit = item_limit;
        column.pages.Clear();
        column.target_count = candidates.Count;
        candidates.Sort((a, b) =>
        {
            int order = Mathf.RoundToInt(a.anchor.y / 4f).CompareTo(Mathf.RoundToInt(b.anchor.y / 4f));
            return order != 0 ? order : a.gameObject.GetInstanceID().CompareTo(b.gameObject.GetInstanceID());
        });
        // Preserve spatial order within each instance and order groups by their
        // first visible member. Instance identity keeps same-name copies separate.
        var grouped_candidates = candidates.GroupBy(c => c.prefab_root).SelectMany(group => group);
        float capacity = column.body_rect.height;
        float used = 0f;
        var page = new List<LabelDisplayInfo>();
        foreach (LabelCandidate c in grouped_candidates)
        {
            float full_height = GetColumnCandidateHeight(c);
            bool show_header = page.Count == 0 || page[page.Count - 1].prefab_root != c.prefab_root;
            float header_height = show_header ? prefab_header_height : 0f;
            float required = full_height + header_height + (page.Count > 0 ? column_gap : 0f);
            // A header is part of its first Transform block, so it can never be
            // orphaned at a page bottom or consume the configured object count.
            if (page.Count > 0 && (page.Count >= item_limit || used + required > capacity))
            {
                column.pages.Add(page);
                page = new List<LabelDisplayInfo>();
                used = 0f;
                show_header = true;
                header_height = prefab_header_height;
            }
            if (full_height + header_height <= capacity)
            {
                used += full_height + header_height + (page.Count > 0 ? column_gap : 0f);
                page.Add(CreateColumnLabel(c, c.components, left, false, show_header));
                continue;
            }
            // A single object can have more RootRefs than fit on one screen.
            // Split its COMPONENT ROWS across pages; repeat the object title.
            if (page.Count > 0)
            {
                column.pages.Add(page);
                page = new List<LabelDisplayInfo>();
                used = 0f;
            }
            int rows_per_page = Mathf.Max(1, Mathf.FloorToInt((capacity - column_padding * 2f - prefab_header_height - label_height) / component_row_height));
            for (int start = 0; start < c.components.Count; start += rows_per_page)
            {
                int count = Mathf.Min(rows_per_page, c.components.Count - start);
                page.Add(CreateColumnLabel(c, c.components.GetRange(start, count), left, start > 0, true));
                column.pages.Add(page);
                page = new List<LabelDisplayInfo>();
            }
        }
        if (page.Count > 0)
            column.pages.Add(page);
        if (!deferPacking)
            foreach (List<LabelDisplayInfo> labels in column.pages)
                PackColumnPage(labels, column.body_rect, compact);
        column.page_index = Mathf.Clamp(column.page_index, 0, Mathf.Max(0, column.pages.Count - 1));
    }

    private static LabelDisplayInfo CreateColumnLabel(LabelCandidate c, List<ComponentDisplayInfo> rows, bool left, bool continuation,
        bool show_header)
    {
        return new LabelDisplayInfo
        {
            gameObject = c.gameObject, anchor_gui_position = c.anchor, alpha = c.alpha,
            prefab_root = c.prefab_root, show_prefab_header = show_header,
            components = rows, is_column_label = true, left_column = left, continuation = continuation,
            block_rect = new Rect(0f, 0f, 0f, column_padding * 2f + label_height + rows.Count * component_row_height
                + (show_header ? prefab_header_height : 0f))
        };
    }

    private static void position_column_label(LabelDisplayInfo label)
    {
        Rect block = label.block_rect;
        label.prefab_header_rect = new Rect(block.x + column_padding, block.y + column_padding,
            block.width - column_padding * 2f, label.show_prefab_header ? prefab_header_height : 0f);
        label.label_rect = new Rect(block.x + column_padding, label.prefab_header_rect.yMax,
            block.width - column_padding * 2f, label_height);
    }

    private static void PackColumnPage(List<LabelDisplayInfo> page, Rect bounds, bool compact = false)
    {
        int count = page.Count;
        if (count == 0)
            return;
        if (compact)
        {
            // A nearby table is top-aligned. World-space Y must not spread a short
            // list over the full viewport or make it move while reading a page.
            float y = bounds.y;
            foreach (LabelDisplayInfo label in page)
            {
                label.block_rect = new Rect(bounds.x, y, bounds.width, label.block_rect.height);
                position_column_label(label);
                y += label.block_rect.height + column_gap;
            }
            return;
        }
        // Bounded isotonic regression (pool adjacent violators).
        // Remove each preceding block's height + gap. The remaining positions
        // must be nondecreasing. Unlike independent clamping, this preserves gaps.
        float[] offsets = new float[count];
        float[] sums = new float[count];
        int[] weights = new int[count];
        int[] starts = new int[count];
        int[] ends = new int[count];
        float total = 0f;
        int groups = 0;
        float y_offset = float.IsNaN(guiOffset.y) || float.IsInfinity(guiOffset.y) ? 0f : guiOffset.y;
        for (int i = 0; i < count; i++)
        {
            offsets[i] = total;
            total += page[i].block_rect.height + (i + 1 < count ? column_gap : 0f);
            sums[groups] = page[i].anchor_gui_position.y + y_offset - offsets[i];
            weights[groups] = 1;
            starts[groups] = ends[groups] = i;
            groups++;
            while (groups >= 2 && sums[groups - 2] / weights[groups - 2] > sums[groups - 1] / weights[groups - 1])
            {
                sums[groups - 2] += sums[groups - 1];
                weights[groups - 2] += weights[groups - 1];
                ends[groups - 2] = ends[groups - 1];
                groups--;
            }
        }
        // Pagination guarantees total <= bounds.height. Only common group
        // offsets are clamped: no label is individually pushed over another.
        float max_base = Mathf.Max(bounds.yMin, bounds.yMax - total);
        for (int g = 0; g < groups; g++)
        {
            float baseline = Mathf.Clamp(sums[g] / weights[g], bounds.yMin, max_base);
            for (int i = starts[g]; i <= ends[g]; i++)
            {
                LabelDisplayInfo label = page[i];
                label.block_rect = new Rect(bounds.x, baseline + offsets[i], bounds.width, label.block_rect.height);
                position_column_label(label);
            }
        }
    }

    private static void ApplyVisibleColumnPages(SceneViewState state)
    {
        state.labels.Clear();
        state.visible_labels.Clear();
        foreach (LabelColumn column in state.visible_columns)
            AddVisibleColumnPage(state, column);
    }

    private static void AddVisibleColumnPage(SceneViewState state, LabelColumn column)
    {
        if (column.pages.Count == 0)
            return;
        column.page_index = Mathf.Clamp(column.page_index, 0, column.pages.Count - 1);
        foreach (LabelDisplayInfo label in column.pages[column.page_index])
        {
            state.labels.Add(label);
            state.visible_labels.Add(new LabelHitArea(label.label_rect, label.gameObject));
        }
    }

    private static void RebuildLocalLabelLayout(SceneView sceneView, SceneViewState state)
    {
        state.column_layout_active = false;
        state.floating_layout_active = false;
        state.floating_origin_valid = false;
        state.column_layout_too_small = false;
        state.visible_labels.Clear();
        state.labels.Clear();
        if (sceneView.camera == null || !state.has_mouse_position)
            return;

        List<Rect> occupied_blocks = new List<Rect>();
        Dictionary<Vector2Int, int> label_stack_counts = new Dictionary<Vector2Int, int>();
        foreach (GameObject go in GetDisplayTargetObjects(state))
        {
            if (go == null || !go.activeInHierarchy)
                continue;
            Vector3 position = go.transform.position;
            if (!IsDepthVisible(sceneView.camera, position, out float depth))
                continue;
            Vector2 gui_position = HandleUtility.WorldToGUIPoint(position);
            float distance = Vector2.Distance(state.mouse_position, gui_position);
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > label_hold_radius_pixels)
                continue;

            float alpha = Mathf.Lerp(maxAlpha, minAlpha, distance / label_hold_radius_pixels);
            List<ComponentDisplayInfo> components = GetDisplayComponentSnapshot(go, alpha);
            int stack_index = GetNextLabelStackIndex(label_stack_counts, gui_position);
            Rect block = ResolveLabelBlockRect(sceneView, gui_position, components.Count, occupied_blocks, stack_index);
            Rect label_rect = new Rect(block.x, block.y, label_width, label_height);
            state.labels.Add(new LabelDisplayInfo
            {
                gameObject = go, anchor_gui_position = gui_position, block_rect = block,
                label_rect = label_rect, alpha = alpha, components = components
            });
            state.visible_labels.Add(new LabelHitArea(label_rect, go));
            occupied_blocks.Add(block);
        }
    }

    private static List<ComponentDisplayInfo> GetDisplayComponentSnapshot(GameObject go, float alpha)
    {
        List<ComponentDisplayInfo> result = new List<ComponentDisplayInfo>();
        if (!show_vrc_phys_bone_components)
            return result;
        foreach (Component component in GetMatchingComponents(go, vrc_phys_bone_type_name))
            result.Add(new ComponentDisplayInfo(component, "", new Color(0f, 0.8f, 1f, alpha)));
        foreach (Component component in GetMatchingComponents(go, vrc_phys_bone_collider_type_name))
            result.Add(new ComponentDisplayInfo(component, "", new Color(0f, 0.8f, 0f, alpha)));
        foreach (Component component in GetPhysBoneRootReferences(go.transform))
            result.Add(new ComponentDisplayInfo(component, "RootRef: ", new Color(1f, 0.65f, 0f, alpha)));
        return result;
    }

    private static void DisplayObjectNames(SceneView sceneView, SceneViewState state)
    {
        Event e = Event.current;
        if (state.column_layout_too_small && two_column_layout)
        {
            GUI.Label(new Rect(8f, 28f, 400f, 40f), "プレハブ別カラム: 表示領域の幅・高さを広げてください。");
            return;
        }

        // Draw every leader first. A later leader cannot obscure an earlier label.
        if (e.type == EventType.Repaint)
        {
            if (state.floating_layout_active && !floating_all_leaders)
                DrawFloatingPanelLeader(sceneView, state);
            else
            {
                foreach (LabelDisplayInfo label in state.labels)
                {
                    if (label.gameObject == null || !label.gameObject.activeInHierarchy ||
                        !IsDepthVisible(sceneView.camera, label.gameObject.transform.position, out float depth))
                        continue;
                    Vector2 anchor = HandleUtility.WorldToGUIPoint(label.gameObject.transform.position);
                    bool highlight = label.block_rect.Contains(state.mouse_position) || Selection.Contains(label.gameObject);
                    float alpha = highlight ? Mathf.Max(label.alpha, 0.9f) : label.alpha * 0.65f;
                    Color line_color = new Color(labelColor.r, labelColor.g, labelColor.b, alpha);
                    bool attach_right = state.floating_layout_active
                        ? anchor.x >= label.label_rect.center.x
                        : label.is_column_label && label.left_column;
                    DrawLeaderLine(anchor, label.label_rect, line_color, attach_right);
                }
            }
            if (state.floating_layout_active && state.column_candidates.Count > 0)
            {
                EditorGUI.DrawRect(state.floating_panel_rect, new Color(0.07f, 0.07f, 0.07f, 0.9f));
                for (int i = 0; i + 1 < state.visible_columns.Count; i++)
                {
                    Rect column_rect = state.visible_columns[i].panel_rect;
                    float divider_x = column_rect.xMax + floating_column_spacing * 0.5f;
                    EditorGUI.DrawRect(new Rect(divider_x, column_rect.y, 1f, column_rect.height),
                        new Color(1f, 1f, 1f, 0.16f));
                }
            }
            // Column backgrounds conceal crossing leaders in the text area.
            foreach (LabelDisplayInfo label in state.labels)
            {
                if (label.is_column_label)
                {
                    bool hovered = label.block_rect.Contains(state.mouse_position);
                    EditorGUI.DrawRect(label.block_rect, hovered
                        ? new Color(0.22f, 0.26f, 0.30f, 0.92f)
                        : new Color(0.08f, 0.08f, 0.08f, 0.75f));
                }
            }
        }

        if (state.column_layout_active)
        {
            draw_column_set_pager(sceneView, state);
            foreach (LabelColumn column in state.visible_columns)
                DrawColumnHeader(sceneView, column);
        }
        if (object_name_style == null)
        {
            object_name_style = new GUIStyle(EditorStyles.label)
            {
                clipping = TextClipping.Clip, wordWrap = false
            };
            transform_icon = EditorGUIUtility.ObjectContent(null, typeof(Transform)).image;
        }
        GUIStyle style = object_name_style;
        style.fontSize = fontSize;
        foreach (LabelDisplayInfo label in state.labels)
        {
            bool hovered = label.block_rect.Contains(state.mouse_position);
            float alpha = hovered ? Mathf.Max(0.9f, label.alpha) : label.alpha;
            style.normal.textColor = new Color(labelColor.r, labelColor.g, labelColor.b, alpha);
            string name = label.gameObject != null ? label.gameObject.name : "Missing Object";
            if (label.continuation)
                name += " (続き)";
            if (label.is_column_label)
            {
                if (label.show_prefab_header)
                    draw_prefab_group_header(label);
                EditorGUI.LabelField(label.label_rect, new GUIContent(name, transform_icon,
                    name + " (Transform)\nクリックで選択。ドラッグしてインスペクターのTransform欄へ割り当て。"), style);
                if (!e.alt && !Tools.viewToolActive)
                    EditorGUIUtility.AddCursorRect(label.label_rect, MouseCursor.MoveArrow);
            }
            else
                EditorGUI.LabelField(label.label_rect, new GUIContent(name, name), style);

            Vector2 component_position = new Vector2(label.label_rect.x, label.label_rect.yMax);
            foreach (ComponentDisplayInfo row in label.components)
            {
                Color row_color = row.color;
                if (hovered)
                    row_color.a = Mathf.Max(row_color.a, 0.9f);
                DisplayComponentWithCheckbox(row.component, component_position, row_color, row.prefix, label.label_rect.width);
                component_position.y += component_row_height;
            }
        }
    }

    private static void draw_prefab_group_header(LabelDisplayInfo label)
    {
        if (prefab_header_style == null)
        {
            prefab_header_style = new GUIStyle(EditorStyles.boldLabel)
            {
                clipping = TextClipping.Clip, wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };
            prefab_header_style.normal.textColor = new Color(0.65f, 0.82f, 1f, 1f);
            prefab_icon = EditorGUIUtility.IconContent("Prefab Icon").image;
        }
        prefab_header_style.fontSize = fontSize;
        Rect rect = label.prefab_header_rect;
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.28f, 0.38f, 0.9f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 1f),
                new Color(0.4f, 0.65f, 0.9f, 0.45f));
        }
        bool has_prefab = label.prefab_root != null;
        string name = has_prefab ? label.prefab_root.name : "非プレハブ";
        string tooltip = has_prefab
            ? label.prefab_root.scene.name + "/" + AnimationUtility.CalculateTransformPath(label.prefab_root.transform, null)
            : "プレハブに属さないTransform";
        EditorGUI.LabelField(rect, new GUIContent(name, has_prefab ? prefab_icon : null, tooltip), prefab_header_style);
    }

    private static GUIStyle get_column_count_style()
    {
        if (column_count_style == null)
        {
            column_count_style = new GUIStyle(EditorStyles.miniLabel);
            column_count_style.normal.textColor = Color.white;
        }
        return column_count_style;
    }

    private static void draw_column_set_pager(SceneView view, SceneViewState state)
    {
        Rect rect = state.column_set_pager_rect;
        if (rect.height <= 0f)
            return;
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 0.92f));
        int start = state.column_set_page * state.columns_per_page;
        int end = Mathf.Min(start + state.columns_per_page, state.prefab_columns.Count);
        GUI.Label(new Rect(rect.x + 28f, rect.y + 2f, rect.width - 56f, 18f),
            "プレハブ列 " + (start + 1) + "–" + end + " / " + state.prefab_columns.Count, get_column_count_style());
        bool navigation = Event.current.alt || Tools.viewToolActive;
        using (new EditorGUI.DisabledScope(navigation || state.column_set_page == 0))
        {
            if (GUI.Button(new Rect(rect.x + 2f, rect.y + 1f, 22f, 20f), new GUIContent("<", "前のプレハブ列")))
            {
                state.pending_column_set_page = state.column_set_page - 1;
                view.Repaint();
            }
        }
        using (new EditorGUI.DisabledScope(navigation || end >= state.prefab_columns.Count))
        {
            if (GUI.Button(new Rect(rect.xMax - 24f, rect.y + 1f, 22f, 20f), new GUIContent(">", "次のプレハブ列")))
            {
                state.pending_column_set_page = state.column_set_page + 1;
                view.Repaint();
            }
        }
    }

    private static void DrawColumnHeader(SceneView view, LabelColumn column)
    {
        if (column.pages.Count == 0)
            return;
        Rect rect = new Rect(column.panel_rect.x, column.panel_rect.y, column.panel_rect.width, column_header_height);
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 0.92f));
        GUIStyle style = get_column_count_style();
        GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width - 114f, 18f),
            column.target_count + "件", style);
        float x = rect.xMax - 108f;
        bool navigation = Event.current.alt || Tools.viewToolActive;
        using (new EditorGUI.DisabledScope(navigation || column.page_index <= 0))
        {
            if (GUI.Button(new Rect(x, rect.y + 1f, 22f, 20f), new GUIContent("<", "前のページ")))
            {
                column.page_index--;
                view.Repaint(); // User action only. Apply on the next Layout.
            }
        }
        GUI.Label(new Rect(x + 26f, rect.y + 2f, 54f, 18f),
            (column.page_index + 1) + "/" + column.pages.Count, style);
        using (new EditorGUI.DisabledScope(navigation || column.page_index >= column.pages.Count - 1))
        {
            if (GUI.Button(new Rect(rect.xMax - 26f, rect.y + 1f, 22f, 20f), new GUIContent(">", "次のページ")))
            {
                column.page_index++;
                view.Repaint();
            }
        }
    }

    private static float SanitizeMaxDepth(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? default_max_depth : Mathf.Max(0f, value);
    }

    private static IEnumerable<GameObject> GetDisplayTargetObjects(SceneViewState state)
    {
        return state.ordered_bones.Where(is_visible_transform).Select(bone => bone.gameObject);
    }

    private static int GetNextLabelStackIndex(Dictionary<Vector2Int, int> labelStackCounts, Vector2 guiPos)
    {
        Vector2Int key = new Vector2Int(
            Mathf.RoundToInt(guiPos.x / label_anchor_group_pixels),
            Mathf.RoundToInt(guiPos.y / label_anchor_group_pixels));

        if (!labelStackCounts.TryGetValue(key, out int stackIndex))
        {
            labelStackCounts[key] = 1;
            return 0;
        }

        labelStackCounts[key] = stackIndex + 1;
        return stackIndex;
    }

    private static Vector2 GetLabelStackOffset(int stackIndex)
    {
        if (stackIndex <= 0)
        {
            return Vector2.zero;
        }

        int layer = (stackIndex + 1) / 2;
        float direction = (stackIndex % 2 == 1) ? 1f : -1f;
        return new Vector2(layer * leader_line_gap, direction * layer * (label_height + block_spacing));
    }

    private static Rect ResolveLabelBlockRect(SceneView sceneView, Vector2 guiPos, int componentCount, List<Rect> occupiedBlocks, int stackIndex)
    {
        float blockHeight = GetLabelBlockHeight(componentCount);
        Rect blockRect = new Rect(guiPos + guiOffset + GetLabelStackOffset(stackIndex), new Vector2(label_width, blockHeight));
        float maxX = Mathf.Max(0f, sceneView.position.width - blockRect.width - 8f);
        float maxY = Mathf.Max(0f, sceneView.position.height - blockRect.height - 8f);

        blockRect.x = Mathf.Clamp(blockRect.x, 0f, maxX);
        blockRect.y = Mathf.Clamp(blockRect.y, 0f, maxY);

        Rect overlapRect;
        int safetyCounter = 0;
        while (TryGetOverlappingRect(blockRect, occupiedBlocks, out overlapRect) && safetyCounter < 256)
        {
            blockRect.y = overlapRect.yMax + block_spacing;
            if (blockRect.y > maxY)
            {
                blockRect.y = Mathf.Clamp(guiPos.y + guiOffset.y - (safetyCounter + 1) * (label_height + block_spacing), 0f, maxY);
                blockRect.x = Mathf.Clamp(blockRect.x + leader_line_gap, 0f, maxX);
            }

            safetyCounter++;
        }

        return blockRect;
    }

    private static void ResetSettings()
    {
        isEnabled = true;
        showSettings = false;
        show_vrc_phys_bone_components = true;
        show_bone_spheres = true;
        show_bone_hierarchy = true;
        show_bone_spheres_near_cursor_only = true;
        enable_bone_sphere_click_selection = false;
        cursor_radius_pixels = default_cursor_radius_pixels;
        label_hold_radius_pixels = default_label_hold_radius_pixels;
        maxDepth = default_max_depth;
        bone_sphere_size = default_bone_sphere_size;
        guiOffset = default_gui_offset;
        fontSize = default_font_size;
        labelColor = default_label_color;
        minAlpha = default_min_alpha;
        maxAlpha = default_max_alpha;
        two_column_layout = true;
        column_items_per_page = default_column_items_per_page;
        pin_column_labels = false;
        column_width = 240f;
        column_gap = 6f;
        column_inset = 12f;
        column_vertical_margins = new Vector2(48f, 20f);
        floating_column_layout = true;
        floating_panel_offset = new Vector2(48f, 40f);
        floating_panel_max_height = 360f;
        floating_limit_height = false;
        floating_column_spacing = 8f;
        floating_all_leaders = false;
    }

    private static void LoadSessionState()
    {
        isEnabled = SessionState.GetBool(session_key_enabled, true);
        showSettings = SessionState.GetBool(session_key_show_settings, false);
        show_vrc_phys_bone_components = SessionState.GetBool(session_key_show_vrc_phys_bone_components, true);
        show_bone_spheres = SessionState.GetBool(session_key_show_bone_spheres, true);
        show_bone_hierarchy = SessionState.GetBool(session_key_show_bone_hierarchy, true);
        show_bone_spheres_near_cursor_only = SessionState.GetBool(session_key_show_bone_spheres_near_cursor_only, true);
        enable_bone_sphere_click_selection = SessionState.GetBool(session_key_enable_bone_sphere_click_selection, false);
        cursor_radius_pixels = SessionState.GetFloat(session_key_cursor_radius_pixels, default_cursor_radius_pixels);
        label_hold_radius_pixels = Mathf.Clamp(SessionState.GetFloat(session_key_label_hold_radius_pixels, default_label_hold_radius_pixels), 16f, 480f);
        maxDepth = SanitizeMaxDepth(SessionState.GetFloat(session_key_max_depth, default_max_depth));
        bone_sphere_size = Mathf.Clamp(SessionState.GetFloat(session_key_bone_sphere_size, default_bone_sphere_size), 0.01f, max_bone_sphere_size);
        guiOffset = GetSessionVector2(session_key_gui_offset_x, session_key_gui_offset_y, default_gui_offset);
        fontSize = SessionState.GetInt(session_key_font_size, default_font_size);
        labelColor = GetSessionColor(default_label_color);
        minAlpha = SessionState.GetFloat(session_key_min_alpha, default_min_alpha);
        maxAlpha = SessionState.GetFloat(session_key_max_alpha, default_max_alpha);
        ClampAlphaRange();
        two_column_layout = SessionState.GetBool(session_key_two_columns, true);
        column_items_per_page = Mathf.Clamp(SessionState.GetInt(session_key_column_items_per_page, default_column_items_per_page), 1, max_column_items_per_page);
        column_width = SessionState.GetFloat(session_key_column_width, 240f);
        column_gap = SessionState.GetFloat(session_key_column_gap, 6f);
        column_inset = SessionState.GetFloat(session_key_column_inset, 12f);
        column_vertical_margins = GetSessionVector2(session_key_column_top, session_key_column_bottom, new Vector2(48f, 20f));
        floating_column_layout = SessionState.GetBool(session_key_floating_columns, true);
        floating_panel_offset = GetSessionVector2(session_key_floating_offset_x, session_key_floating_offset_y, new Vector2(48f, 40f));
        floating_panel_max_height = SessionState.GetFloat(session_key_floating_max_height, 360f);
        floating_limit_height = SessionState.GetBool(session_key_floating_limit_height, false);
        floating_column_spacing = SessionState.GetFloat(session_key_floating_spacing, 8f);
        floating_all_leaders = SessionState.GetBool(session_key_floating_all_leaders, false);
        SanitizeColumnSettings();
    }

    private static void SaveSessionState()
    {
        SessionState.SetBool(session_key_enabled, isEnabled);
        SessionState.SetBool(session_key_show_settings, showSettings);
        SessionState.SetBool(session_key_show_vrc_phys_bone_components, show_vrc_phys_bone_components);
        SessionState.SetBool(session_key_show_bone_spheres, show_bone_spheres);
        SessionState.SetBool(session_key_show_bone_hierarchy, show_bone_hierarchy);
        SessionState.SetBool(session_key_show_bone_spheres_near_cursor_only, show_bone_spheres_near_cursor_only);
        SessionState.SetBool(session_key_enable_bone_sphere_click_selection, enable_bone_sphere_click_selection);
        SessionState.SetFloat(session_key_cursor_radius_pixels, cursor_radius_pixels);
        SessionState.SetFloat(session_key_label_hold_radius_pixels, Mathf.Clamp(label_hold_radius_pixels, 16f, 480f));
        SessionState.SetFloat(session_key_max_depth, SanitizeMaxDepth(maxDepth));
        SessionState.SetFloat(session_key_bone_sphere_size, Mathf.Clamp(bone_sphere_size, 0.01f, max_bone_sphere_size));
        SetSessionVector2(session_key_gui_offset_x, session_key_gui_offset_y, guiOffset);
        SessionState.SetInt(session_key_font_size, fontSize);
        SetSessionColor(labelColor);
        SessionState.SetFloat(session_key_min_alpha, minAlpha);
        SessionState.SetFloat(session_key_max_alpha, maxAlpha);
        SanitizeColumnSettings();
        SessionState.SetBool(session_key_two_columns, two_column_layout);
        SessionState.SetInt(session_key_column_items_per_page, column_items_per_page);
        SessionState.SetFloat(session_key_column_width, column_width);
        SessionState.SetFloat(session_key_column_gap, column_gap);
        SessionState.SetFloat(session_key_column_inset, column_inset);
        SetSessionVector2(session_key_column_top, session_key_column_bottom, column_vertical_margins);
        SessionState.SetBool(session_key_floating_columns, floating_column_layout);
        SetSessionVector2(session_key_floating_offset_x, session_key_floating_offset_y, floating_panel_offset);
        SessionState.SetFloat(session_key_floating_max_height, floating_panel_max_height);
        SessionState.SetBool(session_key_floating_limit_height, floating_limit_height);
        SessionState.SetFloat(session_key_floating_spacing, floating_column_spacing);
        SessionState.SetBool(session_key_floating_all_leaders, floating_all_leaders);
    }

    private static Vector2 GetSessionVector2(string xKey, string yKey, Vector2 defaultValue)
    {
        return new Vector2(
            SessionState.GetFloat(xKey, defaultValue.x),
            SessionState.GetFloat(yKey, defaultValue.y));
    }

    private static void SetSessionVector2(string xKey, string yKey, Vector2 value)
    {
        SessionState.SetFloat(xKey, value.x);
        SessionState.SetFloat(yKey, value.y);
    }

    private static Color GetSessionColor(Color defaultValue)
    {
        return new Color(
            SessionState.GetFloat(session_key_label_color_r, defaultValue.r),
            SessionState.GetFloat(session_key_label_color_g, defaultValue.g),
            SessionState.GetFloat(session_key_label_color_b, defaultValue.b),
            SessionState.GetFloat(session_key_label_color_a, defaultValue.a));
    }

    private static void SetSessionColor(Color value)
    {
        SessionState.SetFloat(session_key_label_color_r, value.r);
        SessionState.SetFloat(session_key_label_color_g, value.g);
        SessionState.SetFloat(session_key_label_color_b, value.b);
        SessionState.SetFloat(session_key_label_color_a, value.a);
    }

    private static void ClampAlphaRange()
    {
        minAlpha = Mathf.Clamp01(minAlpha);
        maxAlpha = Mathf.Clamp01(maxAlpha);

        if (minAlpha > maxAlpha)
        {
            float swap = minAlpha;
            minAlpha = maxAlpha;
            maxAlpha = swap;
        }
    }

    private static bool TryGetOverlappingRect(Rect targetRect, List<Rect> occupiedBlocks, out Rect overlapRect)
    {
        Rect paddedTarget = ExpandRect(targetRect, block_spacing);
        foreach (Rect occupiedRect in occupiedBlocks)
        {
            if (paddedTarget.Overlaps(ExpandRect(occupiedRect, block_spacing)))
            {
                overlapRect = occupiedRect;
                return true;
            }
        }

        overlapRect = new Rect();
        return false;
    }

    private static Rect ExpandRect(Rect rect, float padding)
    {
        return new Rect(
            rect.x - padding,
            rect.y - padding,
            rect.width + padding * 2f,
            rect.height + padding * 2f);
    }

    private static float GetLabelBlockHeight(int componentCount)
    {
        return label_height + (componentCount * component_row_height);
    }

    private static List<Component> GetMatchingComponents(GameObject go, string componentTypeName)
    {
        return go
            .GetComponents<Component>()
            .Where(component => component != null && component.GetType().Name == componentTypeName)
            .ToList();
    }

    private static List<Component> GetPhysBoneRootReferences(Transform rootTransform)
    {
        EnsurePhysBoneRootReferenceCache();
        if (rootTransform != null && phys_bone_root_reference_cache.TryGetValue(rootTransform, out List<Component> references))
        {
            return references;
        }

        return new List<Component>();
    }

    private static void EnsurePhysBoneRootReferenceCache()
    {
        if (!phys_bone_root_reference_cache_dirty)
        {
            return;
        }

        phys_bone_root_reference_cache.Clear();
        foreach (GameObject go in EditorObjectHelper.FindSceneObjects<GameObject>())
        {
            if (go == null)
            {
                continue;
            }

            foreach (Component component in GetMatchingComponents(go, vrc_phys_bone_type_name))
            {
                if (TryGetPhysBoneRootTransform(component, out Transform root_transform))
                {
                    AddPhysBoneRootReference(root_transform, component);
                }
            }
        }

        phys_bone_root_reference_cache_dirty = false;
    }

    private static bool TryGetPhysBoneRootTransform(Component component, out Transform rootTransform)
    {
        rootTransform = null;
        if (component == null)
        {
            return false;
        }

        try
        {
            var root_property = component.GetType().GetProperty("rootTransform", component_member_flags);
            if (root_property != null && root_property.PropertyType == typeof(Transform))
            {
                rootTransform = root_property.GetValue(component) as Transform;
                return rootTransform != null;
            }

            var root_field = component.GetType().GetField("rootTransform", component_member_flags);
            if (root_field != null && root_field.FieldType == typeof(Transform))
            {
                rootTransform = root_field.GetValue(component) as Transform;
                return rootTransform != null;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void AddPhysBoneRootReference(Transform rootTransform, Component component)
    {
        if (rootTransform == null || component == null)
        {
            return;
        }

        if (component.transform == rootTransform)
        {
            return;
        }

        if (!phys_bone_root_reference_cache.TryGetValue(rootTransform, out List<Component> references))
        {
            references = new List<Component>();
            phys_bone_root_reference_cache[rootTransform] = references;
        }

        if (!references.Contains(component))
        {
            references.Add(component);
        }
    }

    private static void DrawLeaderLine(Vector2 anchorGuiPos, Rect labelRect, Color color, bool attachToRight = false)
    {
        if (Event.current.type != EventType.Repaint ||
            float.IsNaN(anchorGuiPos.x) || float.IsNaN(anchorGuiPos.y) ||
            float.IsInfinity(anchorGuiPos.x) || float.IsInfinity(anchorGuiPos.y))
            return;

        Vector2 lineStart = anchorGuiPos;
        float edge_x = attachToRight ? labelRect.xMax : labelRect.xMin;
        float direction = attachToRight ? 1f : -1f;
        Vector2 elbowPoint = new Vector2(edge_x + direction * leader_line_gap, labelRect.center.y);
        Vector2 lineEnd = new Vector2(edge_x, labelRect.center.y);

        Color previous_color = Handles.color;
        CompareFunction previous_z_test = Handles.zTest;
        try
        {
            Handles.color = color;
            Handles.zTest = CompareFunction.Always;
            Handles.DrawAAPolyLine(2f, new Vector3(lineStart.x, lineStart.y),
                new Vector3(elbowPoint.x, elbowPoint.y), new Vector3(lineEnd.x, lineEnd.y));
            EditorGUI.DrawRect(new Rect(lineStart.x - 2f, lineStart.y - 2f, 4f, 4f), color);
        }
        finally
        {
            Handles.color = previous_color;
            Handles.zTest = previous_z_test;
        }
    }
    
    // VRCPhysBone と VRCPhysBoneCollider コンポーネントを表示する

    // コンポーネントをチェックボックス付きで表示する
    private static void DisplayComponentWithCheckbox(Component component, Vector2 position, Color color, string labelPrefix, float rowWidth = label_width)
    {
        if (component == null)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.Toggle(new Rect(position, new Vector2(20f, 20f)), false);
                EditorGUI.LabelField(new Rect(position.x + 20f, position.y, Mathf.Max(1f, rowWidth - 20f), component_row_height), "Missing Component");
            }
            return;
        }
        // コンポーネントが有効かどうかを取得
        TryGetComponentEnabled(component, out bool componentEnabled);
        
        // コンポーネント名のスタイル
        GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = fontSize,
            normal = { textColor = color }
        };
        
        // チェックボックスの矩形
        Rect checkboxRect = new Rect(position, new Vector2(20f, component_row_height));
        
        // コンポーネント名の矩形
        Rect labelRect = new Rect(position.x + 20f, position.y, Mathf.Max(1f, rowWidth - 20f), component_row_height);
        
        // チェックボックスを描画
        bool newEnabled = EditorGUI.Toggle(checkboxRect, componentEnabled);
        
        // コンポーネント名を描画
        string row_text = labelPrefix + component.GetType().Name;
        labelStyle.clipping = TextClipping.Clip;
        labelStyle.wordWrap = false;
        EditorGUI.LabelField(labelRect, new GUIContent(row_text, row_text), labelStyle);

        // チェックボックスの状態が変更された場合、コンポーネントの有効/無効を切り替え
        if (newEnabled != componentEnabled)
        {
            Undo.RecordObject(component, "Toggle Component Enabled");
            TrySetComponentEnabled(component, newEnabled);
        }
    }

    private static bool TryGetComponentEnabled(Component component, out bool componentEnabled)
    {
        componentEnabled = false;
        if (component == null)
        {
            return false;
        }

        try
        {
            if (component is Behaviour behaviour)
            {
                componentEnabled = behaviour.enabled;
                return true;
            }

            var enabledProperty = component.GetType().GetProperty("enabled");
            if (enabledProperty == null || !enabledProperty.CanRead || enabledProperty.PropertyType != typeof(bool))
            {
                return false;
            }

            componentEnabled = (bool)enabledProperty.GetValue(component);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetComponentEnabled(Component component, bool value)
    {
        if (component == null)
        {
            return false;
        }

        try
        {
            if (component is Behaviour behaviour)
            {
                behaviour.enabled = value;
                return true;
            }

            var enabledProperty = component.GetType().GetProperty("enabled");
            if (enabledProperty == null || !enabledProperty.CanWrite || enabledProperty.PropertyType != typeof(bool))
            {
                return false;
            }

            enabledProperty.SetValue(component, value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
