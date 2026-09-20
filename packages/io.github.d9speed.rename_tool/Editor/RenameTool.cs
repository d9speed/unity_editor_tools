// UI Toolkit rename tool with a live before/after table and Animator support.
// Features:
// - Files & Folders rename (AssetDatabase.MoveAsset)
// - Folder scope (recursive on/off)
// - Animator: Parameters / States / State Machines / Layers (toggleable)
// - Two-pane Preview: LEFT = current (before) / RIGHT = new name (after)
// Place this file under an Editor/ folder.

using D9speed_BaseEditorUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements; // TwoPaneSplitView
using UnityEngine;
using UnityEngine.UIElements;

internal static class D9speed_HelperUtility
{
    public static string ApplyReplace(string input, string find, string replace, bool useRegex, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(find))
            return input ?? string.Empty;

        string replacement = replace ?? string.Empty;

        if (useRegex)
        {
            try
            {
                var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.Replace(input, find, replacement, regexOptions);
            }
            catch (ArgumentException)
            {
                // Invalid regex: keep original text so preview/apply never crashes.
                return input;
            }
        }

        if (caseSensitive)
            return input.Replace(find, replacement);

        return Regex.Replace(input, Regex.Escape(find), _ => replacement, RegexOptions.IgnoreCase);
    }
}

public class RenameToolWindow : EditorWindow
{
    // UI fields
    private TextField _findText;
    private TextField _replaceText;
    private Toggle _useRegex;
    private Toggle _caseSensitive;
    private Toggle _includeExtensions;

    private ObjectField _folderField;
    private Toggle _recursive;
    private Toggle _hierarchySelection;

    private ObjectField _animatorField;
    private Toggle _renameAnimatorStates;
    private Toggle _renameAnimatorParams;
    private Toggle _renameAnimatorStateMachines;
    private Toggle _renameAnimatorLayers;

    private Button _previewButton;
    private Button _applyButton;
    private Button _selectAllBtn;
    private Button _selectNoneBtn;

    private MultiColumnListView _previewList;
    private Label _countLabel;
    private Label _emptyLabel;
    private HelpBox _regexWarning;
    private bool _invalidRegex;

    // State
    private List<RenamePreviewItem> _previewItems = new();

    [MenuItem("D9speed/Rename Tool (UI Toolkit)")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<RenameToolWindow>();
        wnd.titleContent = new GUIContent("リネームツール");
        wnd.minSize = new Vector2(680, 600);
        wnd.Show();
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnUnitySelectionChanged;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnUnitySelectionChanged;
    }

    private void OnUnitySelectionChanged()
    {
        if (_findText == null || _previewList == null) return;
        RefreshPreviewLive();
    }

    private void CreateGUI()
    {
        CreateUI(rootVisualElement);
    }

    public void CreateUI(VisualElement root)
    {
        root.Clear();
        EditorUiTheme.Apply(root);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.d9speed.rename_tool/Editor/rename_tool.uss");
        if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        root.AddToClassList("rename_tool");

        var content = new VisualElement();
        content.AddToClassList("d9_content");
        content.AddToClassList("d9_grow");
        content.AddToClassList("d9_window_body");
        root.Add(content);
        content.Add(EditorUiControls.Header("リネームツール", "ファイル・Hierarchy・Animator の名前をまとめて置換します。"));
        var settings = new ScrollView(ScrollViewMode.Vertical) { name = "rename_settings" };
        settings.AddToClassList("rename_settings");
        content.Add(settings);
        var replaceBox = EditorUiControls.Section(settings, "検索と置換");
        var findRow = EditorUiControls.Row(false);
        _findText = EditorUiControls.Field(new TextField("検索する文字列") { name = "find_text" });
        _replaceText = EditorUiControls.Field(new TextField("置換後の文字列") { name = "replace_text" });
        findRow.Add(_findText);
        findRow.Add(_replaceText);
        replaceBox.Add(findRow);
        var optRow = EditorUiControls.Row();
        _useRegex = EditorUiControls.Toggle("正規表現");
        _caseSensitive = EditorUiControls.Toggle("大文字・小文字を区別");
        _includeExtensions = EditorUiControls.Toggle("拡張子も置換する");
        optRow.Add(_useRegex);
        optRow.Add(_caseSensitive);
        optRow.Add(_includeExtensions);
        replaceBox.Add(optRow);
        _regexWarning = new HelpBox("正規表現が無効です。検索する文字列を修正してください。", HelpBoxMessageType.Warning);
        _regexWarning.AddToClassList("d9_hidden");
        replaceBox.Add(_regexWarning);

        var scopeBox = EditorUiControls.Section(settings, "対象を選ぶ");
        _folderField = EditorUiControls.Field(new ObjectField("対象フォルダ") { objectType = typeof(DefaultAsset), allowSceneObjects = false });
        _recursive = EditorUiControls.Toggle("サブフォルダも含める", true);
        _recursive.tooltip = "フォルダを対象にするとき、下位のフォルダも再帰的に検索します。";
        _hierarchySelection = EditorUiControls.Toggle("Hierarchyで選択したオブジェクトを対象にする");
        _hierarchySelection.tooltip = "有効にすると、アセットの代わりにHierarchyで選択したGameObjectを対象にします。";
        scopeBox.Add(_hierarchySelection);
        var scopeRow = EditorUiControls.Row(false);
        scopeRow.Add(_folderField);
        scopeRow.Add(_recursive);
        scopeBox.Add(scopeRow);
        scopeBox.Add(EditorUiControls.Label("Projectでアセットを選択している場合は、その選択が対象フォルダより優先されます。"));
        var animBox = EditorUiControls.Foldout("Animator の名前も置換する（任意）");
        animBox.viewDataKey = "rename_animator";
        _animatorField = EditorUiControls.Field(new ObjectField("Animator Controller") { objectType = typeof(AnimatorController), allowSceneObjects = false });
        _renameAnimatorStates = EditorUiControls.Toggle("ステート", true);
        _renameAnimatorParams = EditorUiControls.Toggle("パラメーター", true);
        _renameAnimatorStateMachines = EditorUiControls.Toggle("ステートマシン", true);
        _renameAnimatorLayers = EditorUiControls.Toggle("レイヤー", true);
        animBox.Add(_animatorField);
        var animRow = EditorUiControls.Row();
        animRow.Add(_renameAnimatorStates);
        animRow.Add(_renameAnimatorParams);
        animRow.Add(_renameAnimatorStateMachines);
        animRow.Add(_renameAnimatorLayers);
        animBox.Add(animRow);
        scopeBox.Add(animBox);

        var toolbar = EditorUiControls.Row();
        _countLabel = EditorUiControls.Label("", "d9_section_title");
        toolbar.Add(_countLabel);
        _previewButton = EditorUiControls.Button("再確認", OnPreview);
        _selectAllBtn = EditorUiControls.Button("すべて選択", () => SetAllSelected(true));
        _selectNoneBtn = EditorUiControls.Button("選択解除", () => SetAllSelected(false));
        toolbar.Add(_previewButton);
        toolbar.Add(_selectAllBtn);
        toolbar.Add(_selectNoneBtn);
        content.Add(toolbar);
        var preview = new VisualElement();
        preview.AddToClassList("d9_grow");
        preview.AddToClassList("d9_table_container");
        _previewList = new MultiColumnListView
        {
            name = "rename_preview",
            fixedItemHeight = 40,
            selectionType = SelectionType.None,
            reorderable = false
        };
        _previewList.AddToClassList("d9_table");
        _previewList.columns.reorderable = false;
        _previewList.columns.Add(new Column
        {
            name = "include", title = "適用", width = 52, minWidth = 52, maxWidth = 52,
            resizable = false,
            makeCell = () =>
            {
                var toggle = EditorUiControls.Toggle("");
                toggle.AddToClassList("d9_table_check");
                toggle.tooltip = "この変更を適用対象に含める";
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (toggle.userData is RenamePreviewItem item)
                    {
                        item.selected = evt.newValue;
                        _previewList.RefreshItems();
                        ShowCountSummary();
                    }
                });
                return toggle;
            },
            bindCell = (element, index) =>
            {
                var item = _previewItems[index];
                element.userData = item;
                ((Toggle)element).SetValueWithoutNotify(item.selected);
            }
        });
        AddPreviewColumn("before", "変更前", FormatBefore);
        AddPreviewColumn("after", "変更後", FormatAfter);
        preview.Add(_previewList);
        _emptyLabel = EditorUiControls.Label("", "d9_empty_state");
        _emptyLabel.pickingMode = PickingMode.Ignore;
        preview.Add(_emptyLabel);
        content.Add(preview);

        var footer = new VisualElement();
        footer.AddToClassList("d9_footer");
        _applyButton = EditorUiControls.Button("選択した変更を適用", OnApply, true);
        _applyButton.name = "apply_renames";
        footer.Add(_applyButton);
        footer.Add(EditorUiControls.Label("変更前後を確認し、適用する行にチェックを入れてください。"));
        root.Add(footer);

        _findText.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _replaceText.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _useRegex.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _caseSensitive.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _includeExtensions.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _folderField.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _recursive.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _hierarchySelection.RegisterValueChangedCallback(_ => { UpdateScopeFieldStates(); RefreshPreviewLive(); });
        _animatorField.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _renameAnimatorStates.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _renameAnimatorParams.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _renameAnimatorStateMachines.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        _renameAnimatorLayers.RegisterValueChangedCallback(_ => RefreshPreviewLive());
        UpdateScopeFieldStates();
        RefreshPreviewLive();
    }

    private void AddPreviewColumn(string name, string title, Func<RenamePreviewItem, string> format)
    {
        _previewList.columns.Add(new Column
        {
            name = name, title = title, width = 300, minWidth = 180, stretchable = true, resizable = true,
            makeCell = () => EditorUiControls.Label("", "d9_table_cell"),
            bindCell = (element, index) =>
            {
                var item = _previewItems[index];
                var label = (Label)element;
                label.text = format(item);
                label.tooltip = label.text;
                label.EnableInClassList("d9_muted", !item.selected);
            }
        });
    }

    private void OnInspectorUpdate()
    {
        EditorUiTheme.RefreshTheme(rootVisualElement);
    }

    private void UpdateScopeFieldStates()
    {
        if ( _folderField == null || _recursive == null) return;
        bool hierarchyMode = UsingHierarchySelection;
        _recursive.SetEnabled(!hierarchyMode);
        _folderField.SetEnabled(!hierarchyMode);
        
    }

    private bool UsingHierarchySelection => _hierarchySelection != null && _hierarchySelection.value;

    private void SetAllSelected(bool selected)
    {
        foreach (var it in _previewItems) it.selected = selected;
        _previewList.RefreshItems();
        ShowCountSummary();
    }

    private void OnPreview() => RefreshPreviewLive();

    private void OnApply()
    {
        if (!_previewItems.Any(item => item.selected) || _invalidRegex)
        {
            EditorUtility.DisplayDialog("適用する変更がありません", "検索条件と、適用対象のチェックを確認してください。", "閉じる");
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();

            // Files/folders
            foreach (var it in _previewItems.Where(i => i.selected && (i.kind == RenameKind.File || i.kind == RenameKind.Folder)))
            {
                if (it.oldPath == it.newPath) continue;
                if (AssetDatabase.LoadMainAssetAtPath(it.newPath) != null || AssetDatabase.IsValidFolder(it.newPath))
                {
                    Debug.LogWarning($"同名のアセットが存在するためスキップ: {it.newPath}");
                    continue;
                }
                string err = AssetDatabase.MoveAsset(it.oldPath, it.newPath);
                if (!string.IsNullOrEmpty(err)) Debug.LogError($"移動に失敗しました: {it.oldPath} -> {it.newPath}: {err}");
                else Debug.Log($"名前を変更しました: {it.oldPath} -> {it.newPath}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        var hierarchyChanges = _previewItems.Where(i => i.selected && i.kind == RenameKind.HierarchyObject).ToList();
        if (hierarchyChanges.Count > 0) ApplyHierarchyChanges(hierarchyChanges);

        // Animator changes
        var animatorChanges = _previewItems.Where(i => i.selected && i.IsAnimatorRelated).ToList();
        if (animatorChanges.Count > 0) ApplyAnimatorChanges(animatorChanges);

        EditorUtility.DisplayDialog("リネーム処理が完了しました", "詳細はConsoleを確認してください。", "閉じる");
        RefreshPreviewLive();
    }

    private void RefreshPreviewLive()
    {
        _invalidRegex = false;
        if (_useRegex.value && !string.IsNullOrEmpty(_findText.value))
        {
            try { _ = new Regex(_findText.value); }
            catch (ArgumentException) { _invalidRegex = true; }
        }
        _regexWarning.EnableInClassList("d9_hidden", !_invalidRegex);
        OnPreviewImpl();
    }

    private void RefreshPreviewTable()
    {
        _previewList.itemsSource = _previewItems;
        _previewList.Rebuild();
        ShowCountSummary();
    }

    private void OnPreviewImpl()
    {
        _previewItems.Clear();

        var find = _findText.value ?? string.Empty;
        var repl = _replaceText.value ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            RefreshPreviewTable();
            return;
        }

        // Assets / Hierarchy
        if (UsingHierarchySelection)
        {
            foreach (var go in EnumerateHierarchySelection())
            {
                var newName = D9speed_HelperUtility.ApplyReplace(go.name, find, repl, _useRegex.value, _caseSensitive.value);
                if (newName != go.name)
                {
                    var hierarchyPath = BuildHierarchyPath(go);
                    _previewItems.Add(RenamePreviewItem.HierarchyObject(go, hierarchyPath, newName));
                }
            }
        }
        else
        {
            foreach (var path in EnumerateAssetPaths())
            {
                string name = Path.GetFileName(path);
                string stem = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                string target = _includeExtensions.value ? name : stem;
                string newTarget = D9speed_HelperUtility.ApplyReplace(target, find, repl, _useRegex.value, _caseSensitive.value);
                if (newTarget != target)
                {
                    string newName = _includeExtensions.value ? newTarget : newTarget + ext;
                    string newPath = Path.Combine(Path.GetDirectoryName(path)!, newName).Replace('\\', '/');
                    _previewItems.Add(new RenamePreviewItem
                    {
                        kind = AssetDatabase.IsValidFolder(path) ? RenameKind.Folder : RenameKind.File,
                        oldPath = path,
                        oldName = name,
                        newName = newName,
                        newPath = newPath,
                        selected = true
                    });
                }
            }
        }

        // Animator
        var ac = _animatorField.value as AnimatorController;
        if (ac)
        {
            if (_renameAnimatorParams.value)
            {
                foreach (var prm in ac.parameters)
                {
                    var newName = D9speed_HelperUtility.ApplyReplace(prm.name, find, repl, _useRegex.value, _caseSensitive.value);
                    if (newName != prm.name)
                        _previewItems.Add(RenamePreviewItem.AnimatorParam(prm, ac, newName));
                }
            }

            if (_renameAnimatorStates.value)
            {
                foreach (var (state, p) in EnumerateAnimatorStates(ac))
                {
                    var newName = D9speed_HelperUtility.ApplyReplace(state.name, find, repl, _useRegex.value, _caseSensitive.value);
                    if (newName != state.name)
                        _previewItems.Add(RenamePreviewItem.AnimatorState(state, p, newName));
                }
            }

            if (_renameAnimatorStateMachines.value)
            {
                for (int i = 0; i < ac.layers.Length; i++)
                {
                    foreach (var sm in EnumerateStateMachines(ac.layers[i].stateMachine))
                    {
                        var newName = D9speed_HelperUtility.ApplyReplace(sm.name, find, repl, _useRegex.value, _caseSensitive.value);
                        if (newName != sm.name)
                            _previewItems.Add(RenamePreviewItem.AnimatorStateMachine(sm, ac, newName));
                    }
                }
            }

            if (_renameAnimatorLayers.value)
            {
                for (int i = 0; i < ac.layers.Length; i++)
                {
                    var layer = ac.layers[i];
                    var newName = D9speed_HelperUtility.ApplyReplace(layer.name, find, repl, _useRegex.value, _caseSensitive.value);
                    if (newName != layer.name)
                        _previewItems.Add(RenamePreviewItem.AnimatorLayer(i, layer.name, newName, ac));
                }
            }
        }

        _previewItems = _previewItems
            .OrderBy(it => it.kind)
            .ThenBy(it => it.DisplaySortKey)
            .ToList();

        RefreshPreviewTable();
    }

    private static string FormatBefore(RenamePreviewItem it)
    {
        return it.kind switch
        {
            RenameKind.File   => $"[ファイル]  {it.oldPath}",
            RenameKind.Folder => $"[フォルダ]{it.oldPath}",
            RenameKind.HierarchyObject => $"[Hierarchy]  {it.hierarchyPath}",
            RenameKind.AnimatorState => $"[ステート] {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorParam => $"[パラメーター] {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorStateMachine => $"[ステートマシン]    {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorLayer => $"[レイヤー] {it.animatorController?.name}: {it.oldName}",
            _ => string.Empty
        };
    }

    private static string FormatAfter(RenamePreviewItem it)
    {
        return it.kind switch
        {
            RenameKind.File   => $"{it.newPath}",
            RenameKind.Folder => $"{it.newPath}",
            RenameKind.HierarchyObject => $"[Hierarchy]  {BuildHierarchyAfterPath(it)}",
            RenameKind.AnimatorState => $"{it.animatorController?.name}: {it.newName}",
            RenameKind.AnimatorParam => $"{it.animatorController?.name}: {it.newName}",
            RenameKind.AnimatorStateMachine => $"{it.animatorController?.name}: {it.newName}",
            RenameKind.AnimatorLayer => $"{it.animatorController?.name}: {it.newName}",
            _ => string.Empty
        };
    }

    private static string BuildHierarchyAfterPath(RenamePreviewItem item)
    {
        if (string.IsNullOrEmpty(item.hierarchyPath)) return item.newName;
        int slash = item.hierarchyPath.LastIndexOf('/');
        if (slash < 0) return item.newName;
        return $"{item.hierarchyPath.Substring(0, slash + 1)}{item.newName}";
    }

    private IEnumerable<string> EnumerateAssetPaths()
    {
        // Priority 1: Project window selection (assets/folders)
        var selectedPaths = GetProjectSelectionPaths();
        if (selectedPaths.Count > 0)
        {
            foreach (var p in ExpandAssetTargets(selectedPaths, _recursive.value))
                yield return p;
            yield break;
        }

        // Priority 2: Folder field
        var folderAsset = _folderField.value as DefaultAsset;
        var folderPath = AssetDatabase.GetAssetPath(folderAsset);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) yield break;

        foreach (var p in ExpandAssetTargets(new[] { folderPath }, _recursive.value))
            yield return p;
    }

    private static List<string> GetProjectSelectionPaths()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets))
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
            if (!seen.Add(path)) continue;
            result.Add(path);
        }

        return result;
    }

    private IEnumerable<string> ExpandAssetTargets(IEnumerable<string> roots, bool recursive)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var path in EnumerateAssetTarget(root, recursive))
            {
                if (!seen.Add(path)) continue;
                yield return path;
            }
        }
    }

    private IEnumerable<string> EnumerateAssetTarget(string path, bool recursive)
    {
        if (string.IsNullOrEmpty(path)) yield break;

        if (AssetDatabase.IsValidFolder(path))
        {
            yield return path; // include folder itself
            foreach (var child in EnumerateFolderContents(path, recursive))
                yield return child;
            yield break;
        }

        yield return path;
    }

    private IEnumerable<string> EnumerateFolderContents(string folderPath, bool recursive)
    {
        if (recursive)
        {
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(p)) yield return p;
            }
            yield break;
        }

        var absFolder = Path.Combine(Directory.GetCurrentDirectory(), folderPath).Replace('\\', '/');
        if (!Directory.Exists(absFolder)) yield break;

        foreach (var dir in Directory.GetDirectories(absFolder))
        {
            var rel = ToAssetsRelative(dir);
            if (!string.IsNullOrEmpty(rel)) yield return rel;
        }
        foreach (var file in Directory.GetFiles(absFolder))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = ToAssetsRelative(file);
            if (!string.IsNullOrEmpty(rel)) yield return rel;
        }
    }

    private IEnumerable<GameObject> EnumerateHierarchySelection()
    {
        var selection = Selection.gameObjects;
        if (selection == null || selection.Length == 0) yield break;

        var seen = new HashSet<GameObject>();
        foreach (var go in selection)
        {
            if (go == null || !seen.Add(go)) continue;
            yield return go;
        }
    }

    private static string BuildHierarchyPath(GameObject go)
    {
        if (go == null) return string.Empty;
        var stack = new Stack<string>();
        var current = go.transform;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        var path = string.Join("/", stack);
        var scene = go.scene;
        if (scene.IsValid() && !string.IsNullOrEmpty(scene.name))
            return $"{scene.name}/{path}";
        return path;
    }

    private static string ToAssetsRelative(string absolute)
    {
        absolute = absolute.Replace('\\', '/');
        var proj = Directory.GetCurrentDirectory().Replace('\\', '/');
        if (!absolute.StartsWith(proj)) return string.Empty;
        var rel = absolute.Substring(proj.Length + 1);
        return rel;
    }

    private void ShowCountSummary()
    {
        int selected = _previewItems.Count(item => item.selected);
        _countLabel.text = $"変更 { _previewItems.Count } 件 · 選択 {selected} 件";
        _countLabel.tooltip = $"ファイル {_previewItems.Count(i => i.kind == RenameKind.File)} / フォルダ {_previewItems.Count(i => i.kind == RenameKind.Folder)} / Hierarchy {_previewItems.Count(i => i.kind == RenameKind.HierarchyObject)} / Animator {_previewItems.Count(i => i.IsAnimatorRelated)}";
        _emptyLabel.text = string.IsNullOrEmpty(_findText.value)
            ? "対象を選び、検索する文字列を入力すると変更内容が表示されます。"
            : "変更対象がありません。検索条件と対象を確認してください。";
        _emptyLabel.EnableInClassList("d9_hidden", _previewItems.Count > 0);
        _applyButton.text = $"選択した {selected} 件を適用";
        _applyButton.SetEnabled(selected > 0 && !_invalidRegex);
        _selectAllBtn.SetEnabled(_previewItems.Count > 0);
        _selectNoneBtn.SetEnabled(selected > 0);
    }

    private void ApplyHierarchyChanges(List<RenamePreviewItem> hierarchyChanges)
    {
        var targets = hierarchyChanges
            .Select(it => it.hierarchyObject)
            .Where(go => go != null)
            .Distinct()
            .ToArray();

        if (targets.Length == 0) return;

        Undo.RecordObjects(targets, "Hierarchyの名前を変更");

        foreach (var it in hierarchyChanges)
        {
            var go = it.hierarchyObject;
            if (!go) continue;
            go.name = it.newName;
            EditorUtility.SetDirty(go);
            Debug.Log($"Hierarchy '{it.hierarchyPath}': {it.oldName} -> {it.newName}");
        }
    }

    #region Animator Helpers

    private static IEnumerable<(AnimatorState state, string path)> EnumerateAnimatorStates(AnimatorController ac)
    {
        for (int i = 0; i < ac.layers.Length; i++)
        {
            string rootPath = $"Layer[{i}]";
            foreach (var t in TraverseSMStates(ac.layers[i].stateMachine, rootPath))
                yield return t;
        }
    }

    private static IEnumerable<(AnimatorState state, string path)> TraverseSMStates(AnimatorStateMachine sm, string path)
    {
        foreach (var st in sm.states)
            yield return (st.state, $"{path}/{st.state.name}");
        foreach (var ssm in sm.stateMachines)
            foreach (var t in TraverseSMStates(ssm.stateMachine, $"{path}/{ssm.stateMachine.name}"))
                yield return t;
    }

    private static IEnumerable<AnimatorStateMachine> EnumerateStateMachines(AnimatorStateMachine root)
    {
        var stack = new Stack<AnimatorStateMachine>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            yield return sm;
            foreach (var ssm in sm.stateMachines)
                stack.Push(ssm.stateMachine);
        }
    }

    private static void ApplyAnimatorChanges(List<RenamePreviewItem> changes)
    {
        var byController = changes.GroupBy(c => c.animatorController).ToList();

        foreach (var grp in byController)
        {
            var ac = grp.Key;
            if (ac == null) continue;

            Undo.RegisterCompleteObjectUndo(ac, "Animatorの名前を変更");

            // Parameters first
            var paramMap = new Dictionary<string, string>();
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorParam))
            {
                string old = it.oldName;
                string neu = it.newName;
                if (ac.parameters.Any(p => p.name == neu))
                {
                    Debug.LogWarning($"Animator '{ac.name}': パラメーター '{neu}' が既に存在するため、'{old}' の変更をスキップしました。");
                    continue;
                }
                var p = ac.parameters.FirstOrDefault(pp => pp.name == old);
                if (p != null)
                {
                    p.name = neu;
                    SetParameter(ac, p);
                    paramMap[old] = neu;
                    Debug.Log($"Animator '{ac.name}': パラメーター {old} -> {neu}");
                }
            }

            if (paramMap.Count > 0)
            {
                foreach (var layer in ac.layers)
                    UpdateConditionsRecursive(layer.stateMachine, paramMap);
            }

            // States
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorState))
            {
                var state = it.animatorState;
                if (state == null) continue;
                state.name = it.newName;
                EditorUtility.SetDirty(ac);
                Debug.Log($"Animator '{ac.name}': ステート {it.oldName} -> {it.newName}");
            }

            // State Machines
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorStateMachine))
            {
                var sm = it.animatorStateMachine;
                if (sm == null) continue;
                sm.name = it.newName;
                EditorUtility.SetDirty(ac);
                Debug.Log($"Animator '{ac.name}': ステートマシン {it.oldName} -> {it.newName}");
            }

            // Layers
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorLayer))
            {
                int idx = it.animatorLayerIndex;
                if (idx < 0 || idx >= ac.layers.Length) continue;
                var layer = ac.layers[idx];
                layer.name = it.newName;
                ac.layers[idx] = layer;
                EditorUtility.SetDirty(ac);
                Debug.Log($"Animator '{ac.name}': レイヤー {it.oldName} -> {it.newName}");
            }

            EditorUtility.SetDirty(ac);
        }

        AssetDatabase.SaveAssets();
    }

    private static void UpdateConditionsRecursive(AnimatorStateMachine sm, Dictionary<string, string> paramMap)
    {
        foreach (var st in sm.states)
        {
            foreach (var tr in st.state.transitions)
            {
                for (int i = 0; i < tr.conditions.Length; i++)
                {
                    var c = tr.conditions[i];
                    if (paramMap.TryGetValue(c.parameter, out var newName))
                    {
                        c.parameter = newName;
                        tr.conditions[i] = c;
                        EditorUtility.SetDirty(tr);
                    }
                }
            }
        }
        foreach (var sub in sm.stateMachines)
            UpdateConditionsRecursive(sub.stateMachine, paramMap);
    }

    private static void SetParameter(AnimatorController ac, AnimatorControllerParameter p)
    {
        for (int i = ac.parameters.Length - 1; i >= 0; i--)
            if (ac.parameters[i].name == p.name)
                ac.RemoveParameter(i);
        ac.AddParameter(p);
    }

    #endregion
}

public enum RenameKind
{
    Folder = 0,
    File = 1,
    HierarchyObject = 2,
    AnimatorState = 3,
    AnimatorParam = 4,
    AnimatorStateMachine = 5,
    AnimatorLayer = 6,
}

[Serializable]
public class RenamePreviewItem
{
    public RenameKind kind;

    // For assets
    public string oldPath;
    public string newPath;
    public string oldName;
    public string newName;

    public bool selected;

    // Hierarchy refs
    public GameObject hierarchyObject;
    public string hierarchyPath;

    // Animator refs
    public AnimatorController animatorController;
    public AnimatorState animatorState;
    public AnimatorStateMachine animatorStateMachine;
    public int animatorLayerIndex = -1;

    public bool IsAnimatorRelated => kind == RenameKind.AnimatorState || kind == RenameKind.AnimatorParam || kind == RenameKind.AnimatorStateMachine || kind == RenameKind.AnimatorLayer;

    public string DisplaySortKey => kind switch
    {
        RenameKind.File => oldPath,
        RenameKind.Folder => oldPath,
        RenameKind.HierarchyObject => hierarchyPath,
        _ => $"{animatorController?.name}/{oldName}"
    };

    public static RenamePreviewItem HierarchyObject(GameObject go, string path, string newName)
    {
        return new RenamePreviewItem
        {
            kind = RenameKind.HierarchyObject,
            oldName = go.name,
            newName = newName,
            hierarchyObject = go,
            hierarchyPath = path,
            selected = true
        };
    }

    public static RenamePreviewItem AnimatorState(AnimatorState st, string path, string newName)
    {
        return new RenamePreviewItem
        {
            kind = RenameKind.AnimatorState,
            oldName = st.name,
            newName = newName,
            animatorState = st,
            animatorController = GetOwningController(st),
            selected = true
        };
    }

    public static RenamePreviewItem AnimatorParam(AnimatorControllerParameter p, AnimatorController ac, string newName)
    {
        return new RenamePreviewItem
        {
            kind = RenameKind.AnimatorParam,
            oldName = p.name,
            newName = newName,
            animatorController = ac,
            selected = true
        };
    }

    public static RenamePreviewItem AnimatorStateMachine(AnimatorStateMachine sm, AnimatorController ac, string newName)
    {
        return new RenamePreviewItem
        {
            kind = RenameKind.AnimatorStateMachine,
            oldName = sm.name,
            newName = newName,
            animatorStateMachine = sm,
            animatorController = ac,
            selected = true
        };
    }

    public static RenamePreviewItem AnimatorLayer(int index, string oldName, string newName, AnimatorController ac)
    {
        return new RenamePreviewItem
        {
            kind = RenameKind.AnimatorLayer,
            oldName = oldName,
            newName = newName,
            animatorLayerIndex = index,
            animatorController = ac,
            selected = true
        };
    }

    private static AnimatorController GetOwningController(AnimatorState st)
    {
        var acs = Resources.FindObjectsOfTypeAll<AnimatorController>();
        foreach (var ac in acs)
        {
            foreach (var layer in ac.layers)
                if (ContainsState(layer.stateMachine, st)) return ac;
        }
        return null;
    }

    private static bool ContainsState(AnimatorStateMachine sm, AnimatorState target)
    {
        foreach (var s in sm.states)
            if (s.state == target) return true;
        foreach (var sub in sm.stateMachines)
            if (ContainsState(sub.stateMachine, target)) return true;
        return false;
    }

    public string ToDisplayString()
    {
        // Not used in 2-pane mode, but kept for compatibility.
        return kind switch
        {
            RenameKind.File => $"[ファイル]  {oldPath}  =>  {newPath}",
            RenameKind.Folder => $"[フォルダ]{oldPath}  =>  {newPath}",
            RenameKind.HierarchyObject => $"[Hierarchy]  {hierarchyPath}  =>  {newName}",
            RenameKind.AnimatorState => $"[ステート] {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorParam => $"[パラメーター] {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorStateMachine => $"[ステートマシン]    {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorLayer => $"[レイヤー] {animatorController?.name}: {oldName}  =>  {newName}",
            _ => string.Empty
        };
    }
}
