using System;
using System.Collections.Generic;
using System.Linq;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public partial class HumanoidAliasComponentCopierWindow
{
    private enum copy_group { physbone, constraint, modular_avatar, other }
    [SerializeField] private copy_group active_copy_tab = copy_group.physbone;
    private readonly List<CopyRow> common_copy_rows = new List<CopyRow>();
    private readonly List<Button> copy_tab_buttons = new List<Button>();
    private MultiColumnListView common_copy_list;
    private Label common_summary, copy_empty_label, common_empty_label;
    private Button common_copy_button;
    private ScrollView warning_scroll;

    private static copy_group get_copy_group(Component component)
    {
        var category = GetComponentCategory(component);
        if (category == CategoryVrcPhysBone || category == CategoryVrcPhysBoneCollider) return copy_group.physbone;
        if (category == CategoryUnityConstraint || category == CategoryVrcConstraint) return copy_group.constraint;
        return category == CategoryModularAvatar ? copy_group.modular_avatar : copy_group.other;
    }

    private static string copy_group_label(copy_group group)
    {
        switch (group)
        {
            case copy_group.physbone: return "VRC PhysBone";
            case copy_group.constraint: return "Unity / VRC Constraint";
            case copy_group.modular_avatar: return "MA";
            default: return "その他・共通";
        }
    }

    private int count_selected(IEnumerable<CopyRow> rows) => rows.Count(row => row.SourceComponent != null
        && !(row.IsMaterial ? excluded_materials : excluded_components).Contains(row.SourceComponent));

    private void select_copy_tab(copy_group group)
    {
        if (group < copy_group.physbone || group > copy_group.modular_avatar) return;
        active_copy_tab = group;
        Scan();
    }

    private void refresh_copy_tabs()
    {
        var components = sourceScan?.Root != null
            ? sourceScan.Root.GetComponentsInChildren<Component>(true).Where(IsSupportedComponent).ToArray()
            : Array.Empty<Component>();
        for (var i = 0; i < copy_tab_buttons.Count; i++)
        {
            var group = (copy_group)i;
            copy_tab_buttons[i].text = $"{copy_group_label(group)}  ({components.Count(c => get_copy_group(c) == group)})";
            copy_tab_buttons[i].EnableInClassList("alias_tab_selected", group == active_copy_tab);
        }
        if (execute_button != null) execute_button.text = copy_group_label(active_copy_tab) + " の選択項目をコピー";
    }

    private void CreateUI()
    {
        var root = rootVisualElement;
        root.Clear();
        EditorUiTheme.Apply(root);
        root.AddToClassList("humanoid_alias_copy");
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.d9speed.humanoid_alias_copy/Editor/humanoid_alias_copy.uss");
        if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        if (active_copy_tab > copy_group.modular_avatar) active_copy_tab = copy_group.physbone;

        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "alias_content" };
        scroll.AddToClassList("d9_scroll");
        scroll.AddToClassList("d9_window_body");
        scroll.contentContainer.AddToClassList("d9_content");
        root.Add(scroll);
        scroll.Add(EditorUiControls.Header("Humanoid Alias Copy", "実ボーンの対応を確認し、種類ごとにコンポーネントをコピーします。"));

        var settings = EditorUiControls.Section(scroll, "コピー元とコピー先");
        var inputs = EditorUiControls.Row(false);
        inputs.AddToClassList("alias_inputs");
        sourceField = EditorUiControls.Field(new ObjectField("コピー元 (シーン上)")
            { name = "source_object", objectType = typeof(GameObject), allowSceneObjects = true });
        targetField = EditorUiControls.Field(new ObjectField("コピー先 (シーン上)")
            { name = "target_object", objectType = typeof(GameObject), allowSceneObjects = true });
        inputs.Add(sourceField); inputs.Add(targetField); settings.Add(inputs);
        sourceField.RegisterValueChangedCallback(_ => Scan());
        targetField.RegisterValueChangedCallback(_ => Scan());
        settings.Add(BuildCopyOptions());
        var actions = EditorUiControls.Row();
        actions.Add(EditorUiControls.Button("スキャン", Scan));
        actions.Add(EditorUiControls.Button("エイリアス再読込", LoadAliases));
        settings.Add(actions);

        warning_scroll = new ScrollView(ScrollViewMode.Vertical) { name = "alias_warnings" };
        warning_scroll.AddToClassList("alias_warnings");
        warning_scroll.AddToClassList("d9_hidden");
        warningBox = new HelpBox("", HelpBoxMessageType.Warning);
        warning_scroll.Add(warningBox); scroll.Add(warning_scroll);

        var preview = EditorUiControls.Section(scroll, "コピー一覧");
        var tabs = new VisualElement { name = "copy_tabs" };
        tabs.AddToClassList("alias_tabs");
        copy_tab_buttons.Clear();
        for (var i = 0; i < 3; i++)
        {
            var group = (copy_group)i;
            var button = EditorUiControls.Button(copy_group_label(group), () => select_copy_tab(group));
            button.name = "copy_tab_" + group;
            button.AddToClassList("alias_tab");
            button.tooltip = copy_group_label(group) + " のコピー元・コピー先を表示";
            button.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow) return;
                var index = ((int)group + (evt.keyCode == KeyCode.RightArrow ? 1 : 2)) % 3;
                select_copy_tab((copy_group)index); copy_tab_buttons[index].Focus(); evt.StopPropagation();
            });
            tabs.Add(button); copy_tab_buttons.Add(button);
        }
        preview.Add(tabs);
        copy_summary = EditorUiControls.Label("", "alias_summary"); preview.Add(copy_summary);
        preview.Add(make_copy_table(copyRows, "copy_preview", out copyListView, out copy_empty_label));

        var common = EditorUiControls.Foldout("その他・共通 (Unity Collider / Contact / Particle / マテリアル)");
        common.name = "common_copy_foldout";
        common.Add(EditorUiControls.Label("この欄の項目は、下の専用ボタンからコピーします。"));
        copySkinnedMeshMaterialsToggle = EditorUiControls.Toggle("対応するSkinnedMeshのマテリアルもコピー", copySkinnedMeshMaterials,
            value => { copySkinnedMeshMaterials = value; Scan(); });
        common.Add(copySkinnedMeshMaterialsToggle);
        common_summary = EditorUiControls.Label("", "alias_summary"); common.Add(common_summary);
        var common_table = make_copy_table(common_copy_rows, "common_copy_preview", out common_copy_list, out common_empty_label);
        common_table.AddToClassList("alias_common_table"); common.Add(common_table);
        common_copy_button = EditorUiControls.Button("その他・共通 の選択項目をコピー", () => execute_group_copy(copy_group.other));
        common_copy_button.name = "copy_common"; common.Add(common_copy_button); preview.Add(common);

        var details = EditorUiControls.Foldout("ボーン対応・参照の詳細");
        details.name = "resolution_details";
        details.Add(BuildResolutionPane()); scroll.Add(details);

        var footer = new VisualElement(); footer.AddToClassList("d9_footer");
        execute_button = EditorUiControls.Button("選択項目をコピー", ExecuteCopy, true);
        execute_button.name = "copy_active_tab"; footer.Add(execute_button);
        footer.Add(EditorUiControls.Label("表示中タブのチェック済み項目だけをコピーします。変更はUndoで戻せます。"));
        root.Add(footer); refresh_copy_tabs();
    }

    private VisualElement make_copy_table(List<CopyRow> rows, string name, out MultiColumnListView table, out Label empty)
    {
        var container = new VisualElement(); container.AddToClassList("alias_table_container");
        table = new MultiColumnListView { name = name, itemsSource = rows, fixedItemHeight = 36,
            selectionType = SelectionType.None, reorderable = false, horizontalScrollingEnabled = true };
        table.AddToClassList("d9_table"); table.columns.reorderable = false;
        table.columns.Add(new Column { name = "include", title = "適用", width = 48, minWidth = 48, maxWidth = 48, resizable = false,
            makeCell = () =>
            {
                var toggle = EditorUiControls.Toggle(""); toggle.AddToClassList("d9_table_check");
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!(toggle.userData is CopyRow item) || item.SourceComponent == null) return;
                    var excluded = item.IsMaterial ? excluded_materials : excluded_components;
                    if (evt.newValue) excluded.Remove(item.SourceComponent); else excluded.Add(item.SourceComponent);
                    RefreshPreview();
                });
                return toggle;
            },
            bindCell = (element, index) =>
            {
                var item = rows[index]; var toggle = (Toggle)element; toggle.userData = item;
                toggle.SetEnabled(item.SourceComponent != null);
                toggle.SetValueWithoutNotify(item.SourceComponent == null || !(item.IsMaterial ? excluded_materials : excluded_components).Contains(item.SourceComponent));
                toggle.tooltip = item.SourceComponent == null ? "選択したコンポーネントに必要な階層です。" : "この項目をコピー対象に含める";
            }
        });
        add_copy_column(table, rows, "operation", "操作", 112, 92, row => row.Operation.Split(':')[0]);
        add_copy_column(table, rows, "component", "コンポーネント", 210, 150,
            row => row.IsMaterial ? "マテリアル" : row.SourceComponent != null ? row.SourceComponent.GetType().Name : "GameObject階層");
        add_copy_column(table, rows, "source", "コピー元", 230, 140, row => row.SourceName, true);
        add_copy_column(table, rows, "target", "コピー先", 230, 140, row => row.TargetName, true);
        add_copy_column(table, rows, "method", "解決方法", 150, 100, row => row.Method);
        container.Add(table);
        empty = EditorUiControls.Label("この分類のコピー対象はありません。", "d9_empty_state");
        empty.pickingMode = PickingMode.Ignore; container.Add(empty);
        return container;
    }

    private static void add_copy_column(MultiColumnListView table, List<CopyRow> rows, string name, string title,
        float width, float minimum, Func<CopyRow, string> format, bool stretch = false)
    {
        table.columns.Add(new Column { name = name, title = title, width = width, minWidth = minimum, stretchable = stretch,
            resizable = true, makeCell = () => EditorUiControls.Label("", "d9_table_cell"),
            bindCell = (element, index) =>
            {
                var row = rows[index]; var label = (Label)element;
                label.text = format(row); label.tooltip = label.text + "\n" + row.Tooltip;
            }
        });
    }

    private VisualElement BuildResolutionPane()
    {
        var split = new TwoPaneSplitView(0, 410, TwoPaneSplitViewOrientation.Horizontal);
        split.AddToClassList("alias_resolution");
        var left = new VisualElement(); left.AddToClassList("alias_resolution_column");
        left.Add(CreateSubLabel("コピー元の候補ボーン")); left.Add(CreateMatchHeaderRow());
        sourceListView = BuildMatchListView(sourceRows); left.Add(sourceListView);
        left.Add(CreateSubLabel("自動対応できない標準キー（手動指定可）"));
        unresolvedListView = new ListView(unresolvedRows, 24, () => EditorUiControls.Label("", "d9_table_cell"),
            (e, i) => ((Label)e).text = unresolvedRows[i]);
        unresolvedListView.AddToClassList("alias_detail_list"); left.Add(unresolvedListView);
        var right = new VisualElement(); right.AddToClassList("alias_resolution_column");
        right.Add(CreateSubLabel("コピー先の候補ボーン")); right.Add(CreateMatchHeaderRow());
        targetListView = BuildMatchListView(targetRows); right.Add(targetListView);
        right.Add(CreateSubLabel("検出コンポーネント / 参照"));
        componentReportView = new ScrollView(); componentReportView.AddToClassList("alias_detail_list"); right.Add(componentReportView);
        split.Add(left); split.Add(right); return split;
    }

    private static void ApplyZebraBackground(VisualElement row, int index) => row.EnableInClassList("alias_zebra", index % 2 == 1);
    private static Label CreateSubLabel(string text) => EditorUiControls.Label(text, "alias_detail_title");
    private static VisualElement CreateMatchHeaderRow()
    {
        var row = new VisualElement(); row.AddToClassList("alias_match_row");
        row.Add(match_label("キー", "Key")); row.Add(match_label("点", "Score"));
        row.Add(match_label("名前", "Name")); row.Add(match_label("根拠", "Detail")); return row;
    }
    private static ListView BuildMatchListView(List<MatchRow> rows)
    {
        var list = new ListView(rows, 26, MakeMatchRow, (element, index) => BindMatchRow(element, rows[index]));
        list.selectionType = SelectionType.None; list.AddToClassList("alias_detail_list"); return list;
    }
    private static Label match_label(string text, string name)
    {
        var label = EditorUiControls.Label(text, "d9_table_cell"); label.name = name;
        label.AddToClassList("alias_match_" + name.ToLowerInvariant()); return label;
    }
    private static VisualElement MakeMatchRow()
    {
        var row = new VisualElement(); row.AddToClassList("alias_match_row");
        foreach (var name in new[] { "Key", "Score", "Name", "Detail" }) row.Add(match_label("", name));
        return row;
    }
    private static void BindMatchRow(VisualElement row, MatchRow item)
    {
        row.Q<Label>("Key").text = item.Key; row.Q<Label>("Score").text = item.Score;
        row.Q<Label>("Name").text = item.Name; row.Q<Label>("Detail").text = item.Detail;
        row.tooltip = item.Key + " / " + item.Name + "\n" + item.Detail + "\n" + item.Tooltip;
    }
    private void OnInspectorUpdate() => EditorUiTheme.RefreshTheme(rootVisualElement);
}
