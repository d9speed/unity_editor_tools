#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class AnimatorPlaybackPreviewWindow : EditorWindow
{
    private const string menu_path = "D9speed/Animation/Animator Playback Preview";
    private const string no_search_folder_label = "None";
    private const string registered_folder_session_key = "D9speed.AnimatorPlaybackPreview.RegisteredFolders";

    [SerializeField] private Animator target_animator;
    [SerializeField] private DefaultAsset search_folder;
    [SerializeField] private string search_folder_path = string.Empty;
    [SerializeField] private string target_animator_path;
    [SerializeField] private string target_animator_name;
    [SerializeField] private string target_avatar_name;
    [SerializeField] private bool include_sub_folders = true;
    [SerializeField] private bool humanoid_clips_only = true;
    [SerializeField] private bool auto_resolve_target_on_playmode = true;
    [SerializeField] private bool auto_scan_on_playmode = true;
    [SerializeField] private AnimatorController selected_controller;
    [SerializeField] private bool use_controller_mask;
    [SerializeField] private AvatarMask controller_mask;
    [SerializeField] private AnimationClip selected_clip;
    [SerializeField] private int selected_state_index;
    [SerializeField] private int selected_controller_index;
    [SerializeField] private int selected_clip_index;
    [SerializeField] private int selected_registered_folder_index;

    private readonly List<AnimatorController> controllers = new List<AnimatorController>();
    private readonly List<AnimationClip> clips = new List<AnimationClip>();
    private readonly List<State_entry> states = new List<State_entry>();
    private readonly List<string> registered_folder_paths = new List<string>();

    private RuntimeAnimatorController remembered_controller;
    private Animator last_target_animator;
    private PlayableGraph clip_graph;
    private AvatarMask temporary_hands_fingers_mask;

    private ObjectField target_animator_field;
    private Label avatar_label;
    private Label humanoid_label;
    private Label current_controller_label;
    private Label saved_path_label;
    private Button restore_controller_button;
    private Button resolve_target_button;

    private ObjectField search_folder_field;
    private TextField search_folder_path_field;
    private PopupField<string> registered_folder_popup;
    private Button apply_registered_folder_button;
    private Button remove_registered_folder_button;
    private Label saved_folder_label;
    private Label controller_count_label;
    private Label clip_count_label;

    private ObjectField selected_controller_field;
    private PopupField<string> controller_popup;
    private Toggle use_controller_mask_toggle;
    private ObjectField controller_mask_field;
    private HelpBox mask_help_box;
    private PopupField<string> state_popup;
    private Button play_selected_state_button;
    private Button play_random_state_button;
    private Button play_random_controller_button;
    private Button play_random_controller_state_button;
    private HelpBox controller_help_box;

    private ObjectField selected_clip_field;
    private PopupField<string> clip_popup;
    private Button play_selected_clip_button;
    private Button play_random_clip_button;
    private Button stop_clip_button;
    private HelpBox clip_help_box;

    private Label play_mode_label;
    private Label playable_graph_label;

    [MenuItem(menu_path)]
    private static void open()
    {
        AnimatorPlaybackPreviewWindow window = GetWindow<AnimatorPlaybackPreviewWindow>();
        window.titleContent = new GUIContent("Animator Playback Preview");
        window.minSize = new Vector2(540, 560);
        window.try_use_selection();
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += on_play_mode_state_changed;
        load_registered_folders();
        restore_search_folder_object();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= on_play_mode_state_changed;
        stop_clip_graph();
        destroy_temporary_mask();
    }

    public void CreateGUI()
    {
        build_ui();
        refresh_ui();
    }

    private void build_ui()
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingTop = 8;
        root.style.paddingBottom = 8;
        D9speedEditorFontUtility.Apply(root);

        ScrollView scroll = new ScrollView();
        root.Add(scroll);

        build_target_area(scroll);
        build_search_area(scroll);
        build_controller_area(scroll);
        build_clip_area(scroll);
        build_status_area(scroll);
    }

    private void build_target_area(VisualElement parent)
    {
        VisualElement box = create_section(parent, "Target");

        target_animator_field = new ObjectField("Animator") { objectType = typeof(Animator), allowSceneObjects = true };
        target_animator_field.RegisterValueChangedCallback(evt =>
        {
            target_animator = evt.newValue as Animator;
            cache_target_animator_key();
            remember_current_controller();
            refresh_ui();
        });
        box.Add(target_animator_field);

        VisualElement row = create_row();
        row.Add(create_button("選択を使用", () =>
        {
            try_use_selection();
            refresh_ui();
        }));
        resolve_target_button = create_button("保存情報から再取得", () =>
        {
            try_resolve_cached_target_animator();
            refresh_ui();
        });
        row.Add(resolve_target_button);
        box.Add(row);

        restore_controller_button = create_button("元の Controller に戻す", () =>
        {
            assign_controller(remembered_controller);
            refresh_ui();
        });
        box.Add(restore_controller_button);

        avatar_label = new Label();
        humanoid_label = new Label();
        current_controller_label = new Label();
        saved_path_label = new Label();
        box.Add(avatar_label);
        box.Add(humanoid_label);
        box.Add(current_controller_label);

        Toggle auto_resolve_toggle = new Toggle("プレイモード突入時に Animator を再取得") { value = auto_resolve_target_on_playmode };
        auto_resolve_toggle.RegisterValueChangedCallback(evt => auto_resolve_target_on_playmode = evt.newValue);
        box.Add(auto_resolve_toggle);
        box.Add(saved_path_label);
    }

    private void build_search_area(VisualElement parent)
    {
        VisualElement box = create_section(parent, "Search");

        search_folder_field = new ObjectField("Folder") { objectType = typeof(DefaultAsset), allowSceneObjects = false };
        search_folder_field.RegisterValueChangedCallback(evt =>
        {
            search_folder = evt.newValue as DefaultAsset;
            cache_search_folder_path();
            refresh_ui();
        });
        box.Add(search_folder_field);

        search_folder_path_field = new TextField("Folder Path") { value = search_folder_path };
        search_folder_path_field.RegisterValueChangedCallback(evt => search_folder_path = evt.newValue);
        box.Add(search_folder_path_field);

        registered_folder_popup = new PopupField<string>("Registered Folder", new List<string> { "None" }, 0);
        registered_folder_popup.RegisterValueChangedCallback(evt =>
        {
            int index = get_index_by_name(registered_folder_paths, evt.newValue);
            if (index >= 0)
            {
                selected_registered_folder_index = index;
            }
        });
        box.Add(registered_folder_popup);

        VisualElement registered_row = create_row();
        registered_row.Add(create_button("現在フォルダを登録", () =>
        {
            register_current_folder();
            refresh_ui();
        }));
        apply_registered_folder_button = create_button("登録フォルダを使用", () =>
        {
            apply_selected_registered_folder();
            refresh_ui();
        });
        remove_registered_folder_button = create_button("登録フォルダを削除", () =>
        {
            remove_selected_registered_folder();
            refresh_ui();
        });
        registered_row.Add(apply_registered_folder_button);
        registered_row.Add(remove_registered_folder_button);
        box.Add(registered_row);

        Toggle include_sub_folders_toggle = new Toggle("サブフォルダを含める") { value = include_sub_folders };
        include_sub_folders_toggle.RegisterValueChangedCallback(evt => include_sub_folders = evt.newValue);
        box.Add(include_sub_folders_toggle);

        Toggle humanoid_clips_only_toggle = new Toggle("Humanoid Clip のみ表示") { value = humanoid_clips_only };
        humanoid_clips_only_toggle.RegisterValueChangedCallback(evt => humanoid_clips_only = evt.newValue);
        box.Add(humanoid_clips_only_toggle);

        Toggle auto_scan_toggle = new Toggle("プレイモード突入時に再スキャン") { value = auto_scan_on_playmode };
        auto_scan_toggle.RegisterValueChangedCallback(evt => auto_scan_on_playmode = evt.newValue);
        box.Add(auto_scan_toggle);

        VisualElement row = create_row();
        row.Add(create_button("指定フォルダをスキャン", () =>
        {
            scan_assets();
            refresh_ui();
        }));
        row.Add(create_button("一覧クリア", () =>
        {
            clear_assets();
            refresh_ui();
        }));
        row.Add(create_button("フォルダ参照を復元", () =>
        {
            restore_search_folder_object();
            refresh_ui();
        }));
        box.Add(row);

        saved_folder_label = new Label();
        controller_count_label = new Label();
        clip_count_label = new Label();
        box.Add(saved_folder_label);
        box.Add(controller_count_label);
        box.Add(clip_count_label);
    }

    private void build_controller_area(VisualElement parent)
    {
        VisualElement box = create_section(parent, "Animator Controller");

        selected_controller_field = new ObjectField("Controller") { objectType = typeof(AnimatorController), allowSceneObjects = false };
        selected_controller_field.RegisterValueChangedCallback(evt =>
        {
            selected_controller = evt.newValue as AnimatorController;
            refresh_states();
            refresh_ui();
        });
        box.Add(selected_controller_field);

        controller_popup = new PopupField<string>("Scanned Controller", new List<string> { "None" }, 0);
        controller_popup.RegisterValueChangedCallback(evt =>
        {
            int index = get_index_by_name(controllers.Select(controller => controller.name), evt.newValue);
            if (index < 0)
            {
                return;
            }

            selected_controller_index = index;
            selected_controller = controllers[selected_controller_index];
            refresh_states();
            refresh_ui();
        });
        box.Add(controller_popup);

        box.Add(create_button("Controller の State を更新", () =>
        {
            refresh_states();
            refresh_ui();
        }));

        use_controller_mask_toggle = new Toggle("Controller をマスク再生する") { value = use_controller_mask };
        use_controller_mask_toggle.RegisterValueChangedCallback(evt =>
        {
            use_controller_mask = evt.newValue;
            refresh_ui();
        });
        box.Add(use_controller_mask_toggle);

        controller_mask_field = new ObjectField("Avatar Mask") { objectType = typeof(AvatarMask), allowSceneObjects = false };
        controller_mask_field.RegisterValueChangedCallback(evt =>
        {
            controller_mask = evt.newValue as AvatarMask;
            refresh_ui();
        });
        box.Add(controller_mask_field);

        mask_help_box = new HelpBox("Avatar Mask 未指定時は、手・指用の一時マスクを使います。脚は動かしません。", HelpBoxMessageType.None);
        box.Add(mask_help_box);

        state_popup = new PopupField<string>("State", new List<string> { "None" }, 0);
        state_popup.RegisterValueChangedCallback(evt =>
        {
            int index = get_index_by_name(states.Select(state => state.display_name), evt.newValue);
            if (index >= 0)
            {
                selected_state_index = index;
            }
        });
        box.Add(state_popup);

        VisualElement state_row = create_row();
        play_selected_state_button = create_button("選択 State を再生", () =>
        {
            play_selected_state();
            refresh_ui();
        });
        play_random_state_button = create_button("ランダム State を再生", () =>
        {
            play_random_state();
            refresh_ui();
        });
        state_row.Add(play_selected_state_button);
        state_row.Add(play_random_state_button);
        box.Add(state_row);

        play_random_controller_button = create_button("ランダム Controller を差し替え再生", () =>
        {
            play_random_controller();
            refresh_ui();
        });
        play_random_controller_state_button = create_button("ランダム Controller / State を再生", () =>
        {
            play_random_controller_state();
            refresh_ui();
        });
        box.Add(play_random_controller_button);
        box.Add(play_random_controller_state_button);

        controller_help_box = new HelpBox(string.Empty, HelpBoxMessageType.Info);
        box.Add(controller_help_box);
    }

    private void build_clip_area(VisualElement parent)
    {
        VisualElement box = create_section(parent, "Animation Clip");

        selected_clip_field = new ObjectField("Clip") { objectType = typeof(AnimationClip), allowSceneObjects = false };
        selected_clip_field.RegisterValueChangedCallback(evt =>
        {
            selected_clip = evt.newValue as AnimationClip;
            refresh_ui();
        });
        box.Add(selected_clip_field);

        clip_popup = new PopupField<string>("Scanned Clip", new List<string> { "None" }, 0);
        clip_popup.RegisterValueChangedCallback(evt =>
        {
            int index = get_index_by_name(clips.Select(get_clip_label), evt.newValue);
            if (index >= 0)
            {
                selected_clip_index = index;
                selected_clip = clips[selected_clip_index];
                refresh_ui();
            }
        });
        box.Add(clip_popup);

        VisualElement row = create_row();
        play_selected_clip_button = create_button("選択 Clip を再生", () =>
        {
            play_clip(selected_clip);
            refresh_ui();
        });
        play_random_clip_button = create_button("ランダム Clip を再生", () =>
        {
            play_random_clip();
            refresh_ui();
        });
        stop_clip_button = create_button("Clip 停止", () =>
        {
            stop_clip_graph();
            refresh_ui();
        });
        row.Add(play_selected_clip_button);
        row.Add(play_random_clip_button);
        row.Add(stop_clip_button);
        box.Add(row);

        clip_help_box = new HelpBox(string.Empty, HelpBoxMessageType.Info);
        box.Add(clip_help_box);
        box.Add(new HelpBox("Clip 単体再生は PlayableGraph を使います。手動の Controller 差し替え再現は Controller の State 再生側で行います。", HelpBoxMessageType.None));
    }

    private void build_status_area(VisualElement parent)
    {
        VisualElement box = create_section(parent, "Status");
        play_mode_label = new Label();
        playable_graph_label = new Label();
        box.Add(play_mode_label);
        box.Add(playable_graph_label);
    }

    private void refresh_ui()
    {
        if (target_animator_field == null)
        {
            return;
        }

        target_animator_field.SetValueWithoutNotify(target_animator);
        resolve_target_button.SetEnabled(!string.IsNullOrEmpty(target_animator_path));
        restore_controller_button.SetEnabled(target_animator != null && remembered_controller != null);

        string avatar_name = target_animator != null && target_animator.avatar != null ? target_animator.avatar.name : "None";
        string humanoid_text = target_animator != null && target_animator.avatar != null && target_animator.avatar.isHuman ? "Yes" : "No";
        string controller_name = target_animator != null && target_animator.runtimeAnimatorController != null ? target_animator.runtimeAnimatorController.name : "None";
        avatar_label.text = "Avatar: " + avatar_name;
        humanoid_label.text = "Humanoid: " + humanoid_text;
        current_controller_label.text = "Current Controller: " + controller_name;
        saved_path_label.text = "Saved Path: " + (string.IsNullOrEmpty(target_animator_path) ? "None" : target_animator_path);

        search_folder_field.SetValueWithoutNotify(search_folder);
        search_folder_path_field.SetValueWithoutNotify(search_folder_path);
        selected_registered_folder_index = Mathf.Clamp(selected_registered_folder_index, 0, Mathf.Max(0, registered_folder_paths.Count - 1));
        update_popup(registered_folder_popup, registered_folder_paths.ToList(), selected_registered_folder_index);
        bool has_registered_folder = registered_folder_paths.Count > 0;
        apply_registered_folder_button.SetEnabled(has_registered_folder);
        remove_registered_folder_button.SetEnabled(has_registered_folder);
        saved_folder_label.text = "Saved Folder: " + (string.IsNullOrEmpty(search_folder_path) ? no_search_folder_label : search_folder_path);
        controller_count_label.text = "Controllers: " + controllers.Count;
        clip_count_label.text = "Clips: " + clips.Count;

        selected_controller_field.SetValueWithoutNotify(selected_controller);
        selected_controller_index = Mathf.Clamp(selected_controller_index, 0, Mathf.Max(0, controllers.Count - 1));
        update_popup(controller_popup, controllers.Select(controller => controller.name).ToList(), selected_controller_index);

        use_controller_mask_toggle.SetValueWithoutNotify(use_controller_mask);
        controller_mask_field.SetValueWithoutNotify(controller_mask);
        controller_mask_field.SetEnabled(use_controller_mask);
        mask_help_box.style.display = use_controller_mask && controller_mask == null ? DisplayStyle.Flex : DisplayStyle.None;

        selected_state_index = Mathf.Clamp(selected_state_index, 0, Mathf.Max(0, states.Count - 1));
        update_popup(state_popup, states.Select(state => state.display_name).ToList(), selected_state_index);

        string controller_reason;
        bool can_play_selected_controller = can_play_controller(out controller_reason);
        string random_reason;
        bool can_play_scanned_controller = can_play_random_controller(out random_reason);
        play_selected_state_button.SetEnabled(can_play_selected_controller);
        play_random_state_button.SetEnabled(can_play_selected_controller);
        play_random_controller_button.SetEnabled(can_play_scanned_controller);
        play_random_controller_state_button.SetEnabled(can_play_scanned_controller);
        string controller_help = !string.IsNullOrEmpty(controller_reason) ? controller_reason : random_reason;
        set_help(controller_help_box, controller_help);

        selected_clip_field.SetValueWithoutNotify(selected_clip);
        selected_clip_index = Mathf.Clamp(selected_clip_index, 0, Mathf.Max(0, clips.Count - 1));
        update_popup(clip_popup, clips.Select(get_clip_label).ToList(), selected_clip_index);

        string clip_reason;
        bool can_play_selected_clip = can_play_clip(out clip_reason);
        play_selected_clip_button.SetEnabled(can_play_selected_clip);
        play_random_clip_button.SetEnabled(can_play_selected_clip);
        stop_clip_button.SetEnabled(clip_graph.IsValid());
        set_help(clip_help_box, clip_reason);

        play_mode_label.text = "Play Mode: " + (EditorApplication.isPlaying ? "Playing" : "Stopped");
        playable_graph_label.text = "Playable Graph: " + (clip_graph.IsValid() ? "Running" : "Stopped");
    }

    private static VisualElement create_section(VisualElement parent, string title)
    {
        Label label = new Label(title);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = 8;
        label.style.marginBottom = 3;
        parent.Add(label);

        VisualElement box = new VisualElement();
        box.style.borderTopWidth = 1;
        box.style.borderBottomWidth = 1;
        box.style.borderLeftWidth = 1;
        box.style.borderRightWidth = 1;
        box.style.borderTopColor = new Color(0.23f, 0.23f, 0.23f);
        box.style.borderBottomColor = new Color(0.23f, 0.23f, 0.23f);
        box.style.borderLeftColor = new Color(0.23f, 0.23f, 0.23f);
        box.style.borderRightColor = new Color(0.23f, 0.23f, 0.23f);
        box.style.paddingLeft = 6;
        box.style.paddingRight = 6;
        box.style.paddingTop = 6;
        box.style.paddingBottom = 6;
        box.style.marginBottom = 8;
        parent.Add(box);
        return box;
    }

    private static VisualElement create_row()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginTop = 2;
        row.style.marginBottom = 2;
        return row;
    }

    private static Button create_button(string text, Action action)
    {
        Button button = new Button(action) { text = text };
        button.style.flexGrow = 1;
        button.style.marginLeft = 2;
        button.style.marginRight = 2;
        return button;
    }

    private static void update_popup(PopupField<string> popup, List<string> labels, int selected_index)
    {
        if (labels == null || labels.Count == 0)
        {
            labels = new List<string> { "None" };
            selected_index = 0;
        }

        selected_index = Mathf.Clamp(selected_index, 0, labels.Count - 1);
        popup.choices = labels;
        popup.SetValueWithoutNotify(labels[selected_index]);
        popup.SetEnabled(!(labels.Count == 1 && labels[0] == "None"));
    }

    private static void set_help(HelpBox help_box, string message)
    {
        bool has_message = !string.IsNullOrEmpty(message);
        help_box.text = has_message ? message : string.Empty;
        help_box.style.display = has_message ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static int get_index_by_name(IEnumerable<string> names, string target_name)
    {
        int index = 0;
        foreach (string name in names)
        {
            if (string.Equals(name, target_name, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private void try_use_selection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            return;
        }

        target_animator = selected.GetComponent<Animator>();
        if (target_animator == null)
        {
            target_animator = selected.GetComponentInParent<Animator>();
        }

        if (target_animator == null)
        {
            target_animator = selected.GetComponentInChildren<Animator>();
        }

        cache_target_animator_key();
        remember_current_controller();
    }

    private void scan_assets()
    {
        clear_assets();

        string folder_path = get_search_folder_path();
        if (string.IsNullOrEmpty(folder_path))
        {
            return;
        }

        string[] folders = new[] { folder_path };

        controllers.AddRange(load_assets<AnimatorController>("t:AnimatorController", folders));
        clips.AddRange(load_assets<AnimationClip>("t:AnimationClip", folders)
            .Where(clip => !humanoid_clips_only || is_humanoid_clip(clip)));

        if (!include_sub_folders)
        {
            controllers.RemoveAll(controller => is_in_sub_folder(controller, folder_path));
            clips.RemoveAll(clip => is_in_sub_folder(clip, folder_path));
        }

        controllers.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

        selected_controller_index = 0;
        selected_clip_index = 0;

        if (selected_controller == null && controllers.Count > 0)
        {
            selected_controller = controllers[0];
        }

        if (selected_clip == null && clips.Count > 0)
        {
            selected_clip = clips[0];
        }

        refresh_states();
    }

    private void clear_assets()
    {
        controllers.Clear();
        clips.Clear();
        states.Clear();
    }

    private void refresh_states()
    {
        states.Clear();

        if (selected_controller == null)
        {
            return;
        }

        for (int i = 0; i < selected_controller.layers.Length; i++)
        {
            AnimatorControllerLayer layer = selected_controller.layers[i];
            collect_states(layer.stateMachine, layer.name, i, string.Empty, states);
        }

        selected_state_index = Mathf.Clamp(selected_state_index, 0, Mathf.Max(0, states.Count - 1));
    }

    private void play_selected_state()
    {
        if (!can_play_controller(out _))
        {
            return;
        }

        selected_state_index = Mathf.Clamp(selected_state_index, 0, states.Count - 1);
        play_state(states[selected_state_index]);
    }

    private void play_random_state()
    {
        if (!can_play_controller(out _))
        {
            return;
        }

        selected_state_index = UnityEngine.Random.Range(0, states.Count);
        play_state(states[selected_state_index]);
    }

    private void play_random_controller_state()
    {
        if (!EditorApplication.isPlaying || controllers.Count == 0 || !is_valid_humanoid_target(out _))
        {
            return;
        }

        selected_controller_index = UnityEngine.Random.Range(0, controllers.Count);
        selected_controller = controllers[selected_controller_index];
        refresh_states();

        if (states.Count == 0)
        {
            return;
        }

        selected_state_index = UnityEngine.Random.Range(0, states.Count);
        play_state(states[selected_state_index]);
    }

    private void play_random_controller()
    {
        if (!EditorApplication.isPlaying || controllers.Count == 0 || !is_valid_humanoid_target(out _))
        {
            return;
        }

        selected_controller_index = UnityEngine.Random.Range(0, controllers.Count);
        selected_controller = controllers[selected_controller_index];
        refresh_states();

        if (use_controller_mask)
        {
            play_masked_controller(false, default(State_entry));
        }
        else
        {
            stop_clip_graph();
            assign_controller(selected_controller);
        }

        if (target_animator != null)
        {
            target_animator.Update(0f);
        }
    }

    private void play_state(State_entry state)
    {
        if (use_controller_mask)
        {
            play_masked_controller(true, state);
            return;
        }

        stop_clip_graph();
        assign_controller(selected_controller);

        if (target_animator == null)
        {
            return;
        }

        target_animator.Play(state.full_path_hash, state.layer_index, 0f);
        target_animator.Update(0f);
    }

    private void play_masked_controller(bool play_state_after_start, State_entry state)
    {
        if (target_animator == null || selected_controller == null)
        {
            return;
        }

        stop_clip_graph();

        clip_graph = PlayableGraph.Create("Animator Controller Mask Preview");
        clip_graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimatorControllerPlayable controller_playable = AnimatorControllerPlayable.Create(clip_graph, selected_controller);
        AnimationLayerMixerPlayable mixer = AnimationLayerMixerPlayable.Create(clip_graph, 1);
        AvatarMask mask = controller_mask != null ? controller_mask : create_hands_fingers_avatar_mask();

        mixer.SetLayerMaskFromAvatarMask(0, mask);
        clip_graph.Connect(controller_playable, 0, mixer, 0);
        mixer.SetInputWeight(0, 1.0f);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(clip_graph, "Masked Controller", target_animator);
        output.SetSourcePlayable(mixer);

        clip_graph.Play();

        if (play_state_after_start)
        {
            controller_playable.Play(state.full_path_hash, state.layer_index, 0f);
        }

        target_animator.Update(0f);
    }

    private AvatarMask create_hands_fingers_avatar_mask()
    {
        if (temporary_hands_fingers_mask != null)
        {
            return temporary_hands_fingers_mask;
        }

        temporary_hands_fingers_mask = new AvatarMask
        {
            name = "Temporary Hands Fingers Mask",
        };

        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            temporary_hands_fingers_mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        }

        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
        temporary_hands_fingers_mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
        return temporary_hands_fingers_mask;
    }

    private void destroy_temporary_mask()
    {
        if (temporary_hands_fingers_mask == null)
        {
            return;
        }

        DestroyImmediate(temporary_hands_fingers_mask);
        temporary_hands_fingers_mask = null;
    }

    private void assign_controller(RuntimeAnimatorController controller)
    {
        if (target_animator == null)
        {
            return;
        }

        Undo.RecordObject(target_animator, "Assign Animator Controller");
        target_animator.runtimeAnimatorController = controller;
        EditorUtility.SetDirty(target_animator);
    }

    private void play_random_clip()
    {
        if (!can_play_clip(out _) || clips.Count == 0)
        {
            return;
        }

        selected_clip_index = UnityEngine.Random.Range(0, clips.Count);
        selected_clip = clips[selected_clip_index];
        play_clip(selected_clip);
    }

    private void play_clip(AnimationClip clip)
    {
        if (target_animator == null || clip == null)
        {
            return;
        }

        stop_clip_graph();

        clip_graph = PlayableGraph.Create("Animator Playback Preview");
        clip_graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimationClipPlayable clip_playable = AnimationClipPlayable.Create(clip_graph, clip);
        clip_playable.SetApplyFootIK(true);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(clip_graph, "Animation", target_animator);
        output.SetSourcePlayable(clip_playable);

        clip_graph.Play();
    }

    private void stop_clip_graph()
    {
        if (!clip_graph.IsValid())
        {
            return;
        }

        clip_graph.Destroy();
    }

    private void remember_current_controller()
    {
        if (target_animator == last_target_animator)
        {
            return;
        }

        last_target_animator = target_animator;
        remembered_controller = target_animator != null ? target_animator.runtimeAnimatorController : null;
    }

    private bool can_play_controller(out string reason)
    {
        reason = string.Empty;

        if (!EditorApplication.isPlaying)
        {
            reason = "プレイモード中のみ再生できます。";
            return false;
        }

        if (!is_valid_humanoid_target(out reason))
        {
            return false;
        }

        if (selected_controller == null)
        {
            reason = "AnimatorController を指定してください。";
            return false;
        }

        if (states.Count == 0)
        {
            reason = "再生できる State がありません。";
            return false;
        }

        return true;
    }

    private bool can_play_clip(out string reason)
    {
        reason = string.Empty;

        if (!EditorApplication.isPlaying)
        {
            reason = "プレイモード中のみ再生できます。";
            return false;
        }

        if (!is_valid_humanoid_target(out reason))
        {
            return false;
        }

        if (selected_clip == null && clips.Count == 0)
        {
            reason = "AnimationClip を指定してください。";
            return false;
        }

        if (selected_clip != null && humanoid_clips_only && !is_humanoid_clip(selected_clip))
        {
            reason = "Humanoid Clip のみ表示が有効です。選択 Clip は Humanoid ではありません。";
            return false;
        }

        return true;
    }

    private bool can_play_random_controller(out string reason)
    {
        reason = string.Empty;

        if (!EditorApplication.isPlaying)
        {
            reason = "プレイモード中のみ再生できます。";
            return false;
        }

        if (!is_valid_humanoid_target(out reason))
        {
            return false;
        }

        if (controllers.Count == 0)
        {
            reason = "スキャン済み AnimatorController がありません。";
            return false;
        }

        return true;
    }

    private bool is_valid_humanoid_target(out string reason)
    {
        reason = string.Empty;

        if (target_animator == null)
        {
            try_resolve_cached_target_animator();
        }

        if (target_animator == null)
        {
            reason = "Animator を指定してください。";
            return false;
        }

        if (target_animator.avatar == null)
        {
            reason = "Animator に Avatar がありません。";
            return false;
        }

        if (!target_animator.avatar.isHuman)
        {
            reason = "Humanoid Avatar の Animator を指定してください。";
            return false;
        }

        return true;
    }

    private void on_play_mode_state_changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            cache_target_animator_key();
            cache_search_folder_path();
        }

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (auto_resolve_target_on_playmode)
            {
                try_resolve_cached_target_animator();
            }

            if (auto_scan_on_playmode)
            {
                restore_search_folder_object();
                scan_assets();
            }
        }

        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            stop_clip_graph();
        }

        refresh_ui();
    }

    private void cache_target_animator_key()
    {
        if (target_animator == null)
        {
            return;
        }

        target_animator_path = get_transform_path(target_animator.transform);
        target_animator_name = target_animator.name;
        target_avatar_name = target_animator.avatar != null ? target_animator.avatar.name : string.Empty;
    }

    private bool try_resolve_cached_target_animator()
    {
        if (target_animator != null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(target_animator_path) && string.IsNullOrEmpty(target_animator_name))
        {
            return false;
        }

        List<Animator> candidates = Resources.FindObjectsOfTypeAll<Animator>()
            .Where(is_scene_animator)
            .OrderBy(animator => animator.gameObject.activeInHierarchy ? 0 : 1)
            .ToList();

        Animator resolved = candidates.FirstOrDefault(animator =>
            string.Equals(normalize_path(get_transform_path(animator.transform)), normalize_path(target_animator_path), StringComparison.OrdinalIgnoreCase));

        if (resolved == null)
        {
            string saved_path_without_root = get_path_without_root(normalize_path(target_animator_path));
            resolved = candidates.FirstOrDefault(animator =>
                string.Equals(get_path_without_root(normalize_path(get_transform_path(animator.transform))), saved_path_without_root, StringComparison.OrdinalIgnoreCase));
        }

        if (resolved == null)
        {
            List<Animator> name_matches = candidates
                .Where(animator => string.Equals(animator.name, target_animator_name, StringComparison.OrdinalIgnoreCase))
                .Where(animator => string.IsNullOrEmpty(target_avatar_name) || animator.avatar == null || string.Equals(animator.avatar.name, target_avatar_name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (name_matches.Count == 1)
            {
                resolved = name_matches[0];
            }
        }

        if (resolved == null)
        {
            return false;
        }

        target_animator = resolved;
        remember_current_controller();
        return true;
    }

    private void cache_search_folder_path()
    {
        if (search_folder == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(search_folder);
        if (AssetDatabase.IsValidFolder(path))
        {
            search_folder_path = path;
        }
    }

    private void load_registered_folders()
    {
        registered_folder_paths.Clear();

        string saved_value = SessionState.GetString(registered_folder_session_key, string.Empty);
        if (string.IsNullOrEmpty(saved_value))
        {
            return;
        }

        registered_folder_paths.AddRange(saved_value
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void save_registered_folders()
    {
        SessionState.SetString(registered_folder_session_key, string.Join("\n", registered_folder_paths.ToArray()));
    }

    private void register_current_folder()
    {
        string path = get_search_folder_path();
        if (!AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        if (registered_folder_paths.Any(registered_path => string.Equals(registered_path, path, StringComparison.OrdinalIgnoreCase)))
        {
            selected_registered_folder_index = registered_folder_paths.FindIndex(registered_path => string.Equals(registered_path, path, StringComparison.OrdinalIgnoreCase));
            return;
        }

        registered_folder_paths.Add(path);
        registered_folder_paths.Sort(StringComparer.OrdinalIgnoreCase);
        selected_registered_folder_index = registered_folder_paths.FindIndex(registered_path => string.Equals(registered_path, path, StringComparison.OrdinalIgnoreCase));
        save_registered_folders();
    }

    private void apply_selected_registered_folder()
    {
        if (registered_folder_paths.Count == 0)
        {
            return;
        }

        selected_registered_folder_index = Mathf.Clamp(selected_registered_folder_index, 0, registered_folder_paths.Count - 1);
        search_folder_path = registered_folder_paths[selected_registered_folder_index];
        restore_search_folder_object();
    }

    private void remove_selected_registered_folder()
    {
        if (registered_folder_paths.Count == 0)
        {
            return;
        }

        selected_registered_folder_index = Mathf.Clamp(selected_registered_folder_index, 0, registered_folder_paths.Count - 1);
        registered_folder_paths.RemoveAt(selected_registered_folder_index);
        selected_registered_folder_index = Mathf.Clamp(selected_registered_folder_index, 0, Mathf.Max(0, registered_folder_paths.Count - 1));
        save_registered_folders();
    }

    private void restore_search_folder_object()
    {
        string path = search_folder_path;
        if (!AssetDatabase.IsValidFolder(path))
        {
            search_folder = null;
            return;
        }

        search_folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
    }

    private string get_search_folder_path()
    {
        if (search_folder != null)
        {
            string object_path = AssetDatabase.GetAssetPath(search_folder);
            if (AssetDatabase.IsValidFolder(object_path))
            {
                search_folder_path = object_path;
                return object_path;
            }
        }

        if (!string.IsNullOrEmpty(search_folder_path) && AssetDatabase.IsValidFolder(search_folder_path))
        {
            return search_folder_path;
        }

        search_folder_path = string.Empty;
        search_folder = null;
        return string.Empty;
    }

    private static bool is_scene_animator(Animator animator)
    {
        if (animator == null || EditorUtility.IsPersistent(animator))
        {
            return false;
        }

        Scene scene = animator.gameObject.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private static string get_transform_path(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        List<string> names = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static string normalize_path(string path)
    {
        return string.IsNullOrEmpty(path)
            ? string.Empty
            : path.Replace("(Clone)", string.Empty).Trim();
    }

    private static string get_path_without_root(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        int slash_index = path.IndexOf('/');
        return slash_index >= 0 && slash_index + 1 < path.Length
            ? path.Substring(slash_index + 1)
            : path;
    }

    private static List<T> load_assets<T>(string filter, string[] folders) where T : UnityEngine.Object
    {
        return AssetDatabase.FindAssets(filter, folders)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>())
            .Distinct()
            .Where(asset => asset != null)
            .ToList();
    }

    private static bool is_in_sub_folder(UnityEngine.Object asset, string folder_path)
    {
        string asset_path = AssetDatabase.GetAssetPath(asset);
        string folder = System.IO.Path.GetDirectoryName(asset_path).Replace("\\", "/");
        return !string.Equals(folder, folder_path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool is_humanoid_clip(AnimationClip clip)
    {
        return clip != null && clip.humanMotion;
    }

    private static string get_clip_label(AnimationClip clip)
    {
        if (clip == null)
        {
            return "None";
        }

        return is_humanoid_clip(clip) ? clip.name : clip.name + " [Generic]";
    }

    private static void collect_states(
        AnimatorStateMachine state_machine,
        string layer_name,
        int layer_index,
        string parent_path,
        List<State_entry> results)
    {
        if (state_machine == null)
        {
            return;
        }

        foreach (ChildAnimatorState child_state in state_machine.states)
        {
            AnimatorState state = child_state.state;
            if (state == null)
            {
                continue;
            }

            string state_path = string.IsNullOrEmpty(parent_path) ? state.name : parent_path + "." + state.name;
            string full_path = layer_name + "." + state_path;
            results.Add(new State_entry
            {
                layer_index = layer_index,
                full_path_hash = Animator.StringToHash(full_path),
                display_name = full_path,
            });
        }

        foreach (ChildAnimatorStateMachine child_machine in state_machine.stateMachines)
        {
            string child_path = string.IsNullOrEmpty(parent_path)
                ? child_machine.stateMachine.name
                : parent_path + "." + child_machine.stateMachine.name;
            collect_states(child_machine.stateMachine, layer_name, layer_index, child_path, results);
        }
    }

    [Serializable]
    private struct State_entry
    {
        public int layer_index;
        public int full_path_hash;
        public string display_name;
    }
}

#endif
