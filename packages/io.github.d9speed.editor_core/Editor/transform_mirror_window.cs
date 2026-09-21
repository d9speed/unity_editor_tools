using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils
{
    public class TransformMirrorTool : EditorWindow
    {
        [SerializeField] private List<UnityEngine.Object> targetObjects = new();
        [SerializeField] private bool useCustomPivot;
        [SerializeField] private Vector3 customPivot = Vector3.zero;
        [SerializeField] private bool mirrorRotation = true;
        [SerializeField] private bool CopyblendshapesWeightValue = true;
        private ListView listView;
        private ListView pair_list;
        private Label target_count;
        private Label empty_state;
        private Label search_roots;
        private Label pair_count;
        private HelpBox messages;
        private Button clear_button;
        private Button mirror_button;
        private TransformMirrorPlan plan;
        private bool preview_dirty = true;

        [MenuItem("D9speed/Transform Mirror Tool")]
        public static void ShowWindow() => GetWindow<TransformMirrorTool>("Transform Mirror");

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += invalidate_preview;
            Undo.undoRedoPerformed += invalidate_preview;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= invalidate_preview;
            Undo.undoRedoPerformed -= invalidate_preview;
        }

        private void invalidate_preview() => preview_dirty = true;

        public void CreateGUI()
        {
            minSize = new Vector2(340, 380);
            var root = rootVisualElement;
            root.Clear();
            EditorUiTheme.Apply(root);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "mirror_scroll" };
            scroll.AddToClassList("d9_scroll");
            root.Add(scroll);
            var content = new VisualElement();
            content.AddToClassList("d9_content");
            scroll.Add(content);
            content.Add(EditorUiControls.Header("Transform Mirror", "X軸を基準に、左右名・参照・物理設定を対称化したコピーを作成します。"));

            var targets = EditorUiControls.Section(content, "複製するオブジェクト");
            target_count = EditorUiControls.Label("", "d9_badge");
            target_count.name = "target_count";
            targets.Q(className: "d9_section_header").Add(target_count);
            targets.Add(EditorUiControls.Label("Hierarchy / Project からドラッグ＆ドロップ。子階層も一緒に複製します。"));
            var list_container = new VisualElement();
            list_container.AddToClassList("d9_list_container");
            targets.Add(list_container);
            listView = new ListView(targetObjects, 28, () => EditorUiControls.Label("", "d9_list_row"), (element, index) =>
            {
                var value = targetObjects[index] as GameObject;
                ((Label)element).text = value != null ? value.name : "<null>";
                element.tooltip = value != null ? TransformMirrorPlan.path(value.transform) : "";
            }) { name = "mirror_targets", selectionType = SelectionType.Multiple, reorderable = false };
            listView.AddToClassList("d9_target_list");
            listView.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!DragAndDrop.objectReferences.Any(obj => obj is GameObject)) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                listView.AddToClassList("d9_drop_active");
                evt.StopPropagation();
            });
            listView.RegisterCallback<DragLeaveEvent>(_ => listView.RemoveFromClassList("d9_drop_active"));
            listView.RegisterCallback<DragExitedEvent>(_ => listView.RemoveFromClassList("d9_drop_active"));
            listView.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!DragAndDrop.objectReferences.Any(obj => obj is GameObject)) return;
                DragAndDrop.AcceptDrag();
                add_targets(DragAndDrop.objectReferences);
                listView.RemoveFromClassList("d9_drop_active");
                evt.StopPropagation();
            });
            list_container.Add(listView);
            empty_state = EditorUiControls.Label("GameObject / Prefab をここへ追加", "d9_empty_state");
            empty_state.pickingMode = PickingMode.Ignore;
            list_container.Add(empty_state);
            var actions = EditorUiControls.Row();
            actions.Add(EditorUiControls.Button("選択中を追加", () => add_targets(Selection.gameObjects)));
            actions.Add(EditorUiControls.Button("選択行を外す", () =>
            {
                foreach (var index in listView.selectedIndices.OrderByDescending(i => i).ToArray()) targetObjects.RemoveAt(index);
                targets_changed();
            }));
            clear_button = EditorUiControls.Button("一覧をクリア", () => { targetObjects.Clear(); targets_changed(); });
            clear_button.name = "clear_targets";
            clear_button.tooltip = "一覧から外します。シーンやアセットは削除しません。";
            actions.Add(clear_button);
            targets.Add(actions);

            var preview = EditorUiControls.Section(content, "これから作る対称化ペア");
            pair_count = EditorUiControls.Label("", "d9_badge");
            pair_count.name = "pair_count";
            preview.Q(className: "d9_section_header").Add(pair_count);
            search_roots = EditorUiControls.Label("");
            search_roots.name = "mirror_search_roots";
            preview.Add(search_roots);
            preview.Add(EditorUiControls.Label("最寄りのPrefabルート（なければHierarchyルート）からDFS探索。左右の階層パス、一意な左右名の順で対応付けます。"));
            var refresh = EditorUiControls.Button("候補を更新", refresh_preview);
            refresh.name = "refresh_mirror_pairs";
            preview.Add(refresh);
            var headings = new VisualElement();
            headings.AddToClassList("d9_mirror_pair_row");
            headings.Add(make_cell("作成元 / 現在の参照", "d9_section_title"));
            headings.Add(make_cell("作成予定 / 対称化後の参照", "d9_section_title"));
            preview.Add(headings);
            pair_list = new ListView { name = "mirror_pairs", fixedItemHeight = 70, selectionType = SelectionType.Single };
            pair_list.AddToClassList("d9_mirror_pairs");
            pair_list.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("d9_mirror_pair_row");
                var left = make_cell("", "d9_mirror_path");
                left.name = "pair_source";
                var right = new VisualElement { name = "pair_destination" };
                right.AddToClassList("d9_mirror_cell");
                right.Add(EditorUiControls.Label("", "d9_mirror_path"));
                right.Add(EditorUiControls.Label("", "d9_description"));
                row.Add(left);
                row.Add(right);
                return row;
            };
            pair_list.bindItem = (row, index) =>
            {
                var pair = plan.pairs[index];
                var left = row.Q<Label>("pair_source");
                left.text = (pair.source != null ? pair.source.name + " · " : "") + pair.source_path;
                left.tooltip = pair.source_path;
                var labels = row.Q("pair_destination").Query<Label>().ToList();
                labels[0].text = pair.destination_path.Split('/').Last() + " · " + pair.destination_path;
                labels[0].tooltip = pair.destination_path;
                labels[1].text = pair.status;
                labels[1].EnableInClassList("d9_status_locationdiff", pair.warning);
                row.tooltip = pair.source_path + "\n→ " + pair.destination_path + "\n" + pair.status;
            };
            pair_list.itemsChosen += items =>
            {
                if (items.FirstOrDefault() is TransformMirrorPlan.Pair pair && pair.source != null) EditorGUIUtility.PingObject(pair.source);
            };
            preview.Add(pair_list);
            messages = new HelpBox("", HelpBoxMessageType.Info) { name = "mirror_messages" };
            preview.Add(messages);

            var settings = EditorUiControls.Section(content, "ミラー設定");
            var pivot_toggle = EditorUiControls.Toggle("基準点を指定する", useCustomPivot);
            pivot_toggle.name = "use_custom_pivot";
            settings.Add(pivot_toggle);
            settings.Add(EditorUiControls.Label("通常はワールド原点 (0, 0, 0) のYZ平面を使います。探索ルートは反転の基準点とは別です。"));
            var pivot_field = new Vector3Field("基準点") { name = "custom_pivot", value = customPivot };
            pivot_field.AddToClassList("d9_vector_field");
            pivot_field.SetEnabled(useCustomPivot);
            pivot_toggle.RegisterValueChangedCallback(evt => { useCustomPivot = evt.newValue; pivot_field.SetEnabled(useCustomPivot); });
            pivot_field.RegisterValueChangedCallback(evt => customPivot = evt.newValue);
            settings.Add(pivot_field);
            var rotation_toggle = EditorUiControls.Toggle("回転も反転する (Y / Z)", mirrorRotation, value => mirrorRotation = value);
            rotation_toggle.name = "mirror_rotation";
            settings.Add(rotation_toggle);
            var blendshape_toggle = EditorUiControls.Toggle("BlendShapeのウェイトを引き継ぐ", CopyblendshapesWeightValue, value => CopyblendshapesWeightValue = value);
            blendshape_toggle.name = "copy_blendshapes";
            settings.Add(blendshape_toggle);
            settings.Add(EditorUiControls.Label("Constraint / PhysBone / Colliderの設定も処理します。参照先が見つからない場合は元の参照を維持します。"));

            var footer = new VisualElement();
            footer.AddToClassList("d9_footer");
            mirror_button = EditorUiControls.Button("表示したペアのミラーを作成", InstantiateMirroredAll, true);
            mirror_button.name = "create_mirror";
            footer.Add(mirror_button);
            footer.Add(EditorUiControls.Label("シーンに新規複製します。既存の対称オブジェクトは上書きせず、Undoでまとめて戻せます。"));
            root.Add(footer);
            refresh_preview();
        }

        private static Label make_cell(string text, string style_class)
        {
            var cell = EditorUiControls.Label(text, style_class);
            cell.AddToClassList("d9_mirror_cell");
            return cell;
        }

        private void add_targets(IEnumerable<UnityEngine.Object> objects)
        {
            foreach (var value in objects.OfType<GameObject>())
                if (!targetObjects.Contains(value)) targetObjects.Add(value);
            targets_changed();
        }

        private void targets_changed()
        {
            listView.ClearSelection();
            listView.Rebuild();
            refresh_preview();
        }

        private void OnInspectorUpdate()
        {
            EditorUiTheme.RefreshTheme(rootVisualElement);
            if (preview_dirty && pair_list != null) refresh_preview();
            RefreshTargetState();
        }

        private void RefreshTargetState()
        {
            if (mirror_button == null) return;
            var count = targetObjects.OfType<GameObject>().Count(obj => obj != null);
            target_count.text = count + " 件";
            empty_state.EnableInClassList("d9_hidden", targetObjects.Count > 0);
            clear_button.SetEnabled(targetObjects.Count > 0);
            mirror_button.SetEnabled(count > 0 && !EditorApplication.isPlayingOrWillChangePlaymode);
        }

        private void refresh_preview()
        {
            plan = TransformMirrorPlan.build(targetObjects.OfType<GameObject>());
            preview_dirty = false;
            pair_list.itemsSource = plan.pairs;
            pair_list.Rebuild();
            pair_count.text = plan.planned_paths.Count + " 個作成 / " + plan.pairs.Count + " ペア";
            search_roots.text = plan.creations.Count == 0 ? "対象を追加すると候補を表示します。" :
                "探索ルート: " + string.Join("、", plan.creations.Select(c => TransformMirrorPlan.path(c.search_root)).Distinct());
            var warnings = plan.warnings.Distinct().ToArray();
            messages.text = warnings.Length == 0 ? "左右名: _L / _R、.L / .R（小文字・.001の連番にも対応）。行のダブルクリックで参照元を選択表示できます。" :
                string.Join("\n", warnings.Take(8)) + (warnings.Length > 8 ? "\nほか " + (warnings.Length - 8) + " 件。各ペアの状態を確認してください。" : "");
            messages.messageType = warnings.Length == 0 ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
            RefreshTargetState();
        }

        private void InstantiateMirroredAll()
        {
            var next = TransformMirrorPlan.build(targetObjects.OfType<GameObject>());
            if (plan != null && plan.creations.Count > 0 && plan.signature != next.signature)
            {
                refresh_preview();
                messages.text = "対象や参照が変わったため候補を更新しました。表示を確認して、もう一度作成してください。";
                messages.messageType = HelpBoxMessageType.Warning;
                return;
            }
            try
            {
                var result = TransformMirrorComponents.execute(next, useCustomPivot ? customPivot : Vector3.zero, mirrorRotation, CopyblendshapesWeightValue);
                refresh_preview();
                messages.text = result.Count + " 個のミラーを作成しました。Undoで元に戻せます。";
                messages.messageType = HelpBoxMessageType.Info;
                Debug.Log("Transform Mirror: " + result.Count + " 個の複製が完了しました。");
            }
            catch (Exception error)
            {
                messages.text = "ミラーを作成できませんでした: " + error.Message;
                messages.messageType = HelpBoxMessageType.Error;
                Debug.LogException(error);
            }
        }
    }
}
