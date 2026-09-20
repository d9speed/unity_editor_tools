// RenameToolWindow.cs (live preview + 2-pane before/after + Animator)
// UI Toolkit rename tool with live (instant) preview and a two-pane diff-like preview.
// Features:
// - Files & Folders rename (AssetDatabase.MoveAsset)
// - Folder scope (recursive on/off)
// - Animator: Parameters / States / State Machines / Layers (toggleable)
// - Two-pane Preview: LEFT = current (before) / RIGHT = new name (after)
// Place this file under an Editor/ folder.

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

    // Two-pane preview
    private ListView _beforeList;
    private ListView _afterList;

    // State
    private List<RenamePreviewItem> _previewItems = new();

    [MenuItem("D9speed/Rename Tool (UI Toolkit)")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<RenameToolWindow>();
        wnd.titleContent = new GUIContent("Rename Tool");
        wnd.minSize = new Vector2(820, 540);
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
        if (_findText == null || _beforeList == null || _afterList == null) return;
        RefreshPreviewLive();
    }

    private void CreateGUI()
    {
        CreateUI(rootVisualElement);
    }

    public void CreateUI(VisualElement root)
    {
        root.Clear();
        root.style.paddingLeft = 8; root.style.paddingRight = 8; root.style.paddingTop = 8; root.style.paddingBottom = 8;
        D9speedEditorFontUtility.Apply(root);

        // Find/Replace
        _findText = new TextField("Find") { style = { flexGrow = 1 } };
        _replaceText = new TextField("Replace") { style = { flexGrow = 1 } };
        var findRow = new VisualElement { style = { flexDirection = FlexDirection.Row} };
        findRow.Add(_findText);
        findRow.Add(_replaceText);
        root.Add(findRow);

        // Options
        var optRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
        _useRegex = new Toggle("Use Regex");
        _caseSensitive = new Toggle("Match Case");
        _includeExtensions = new Toggle("Include extensions");
        optRow.Add(_useRegex);
        optRow.Add(_caseSensitive);
        optRow.Add(_includeExtensions);
        root.Add(optRow);

        // Scope
        var scopeBox = new GroupBox { text = "Asset Scope", style = { marginTop = 8 } };
        
        _folderField = new ObjectField("Folder") { objectType = typeof(DefaultAsset), allowSceneObjects = false, style = { flexGrow = 1 } };
        _recursive = new Toggle("Recursive") { value = true, tooltip = "When a folder is selected, recurse into subfolders." };
        _hierarchySelection = new Toggle("Hierarchy Selection Mode") { tooltip = "When enabled, rename uses the currently selected hierarchy GameObjects instead of project assets." };
        var scopeRow = new VisualElement { style = { flexDirection = FlexDirection.Row,  } };
        scopeRow.Add(_recursive);
        scopeBox.Add(_hierarchySelection);
        scopeBox.Add(scopeRow);
        scopeBox.Add(_folderField);
        root.Add(scopeBox);

        // Animator
        var animBox = new GroupBox { text = "Animator Controller (optional)", style = { marginTop = 8 } };
        _animatorField = new ObjectField("AnimatorController") { objectType = typeof(AnimatorController), allowSceneObjects = false, style = { flexGrow = 1 } };
        var animTogRow1 = new VisualElement { style = { flexDirection = FlexDirection.Row,  } };
        var animTogRow2 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
        _renameAnimatorStates = new Toggle("Rename States") { value = true };
        _renameAnimatorParams = new Toggle("Rename Parameters") { value = true };
        _renameAnimatorStateMachines = new Toggle("Rename State Machines") { value = true };
        _renameAnimatorLayers = new Toggle("Rename Layers") { value = true };
        animTogRow1.Add(_renameAnimatorStates);
        animTogRow1.Add(_renameAnimatorParams);
        animTogRow2.Add(_renameAnimatorStateMachines);
        animTogRow2.Add(_renameAnimatorLayers);
        animBox.Add(_animatorField);
        animBox.Add(animTogRow1);
        animBox.Add(animTogRow2);
        root.Add(animBox);

        // Buttons
        var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row,marginTop = 8 } };
        _previewButton = new Button(OnPreview) { text = "Preview" };
        _applyButton = new Button(OnApply) { text = "Apply", tooltip = "Apply the renames shown in the list." };
        _selectAllBtn = new Button(() => SetAllSelected(true)) { text = "Select All" };
        _selectNoneBtn = new Button(() => SetAllSelected(false)) { text = "Select None" };
        btnRow.Add(_previewButton);
        btnRow.Add(_applyButton);
        btnRow.Add(_selectAllBtn);
        btnRow.Add(_selectNoneBtn);
        root.Add(btnRow);

        // Two-pane preview (Before / After)
        var split = new TwoPaneSplitView(0, 400, TwoPaneSplitViewOrientation.Horizontal)
        { style = { flexGrow = 1, marginTop = 8 } };

        var beforeGroup = new GroupBox { text = "Before (current)", style = { flexGrow = 1 } };
        var afterGroup  = new GroupBox { text = "After (preview)",  style = { flexGrow = 1 } };

        _beforeList = MakeListView();
        _afterList  = MakeListView();

        beforeGroup.Add(_beforeList);
        afterGroup.Add(_afterList);
        split.Add(beforeGroup);
        split.Add(afterGroup);
        root.Add(split);

        var help = new HelpBox("2-Pane Live Preview: LEFT shows current names, RIGHT shows the renamed result. If Project selection exists, it is prioritized over Folder scope.", HelpBoxMessageType.Info);
        root.Add(help);

        // Live update wiring
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

    private void UpdateScopeFieldStates()
    {
        if ( _folderField == null || _recursive == null) return;
        bool hierarchyMode = UsingHierarchySelection;
        _recursive.SetEnabled(!hierarchyMode);
        
    }

    private bool UsingHierarchySelection => _hierarchySelection != null && _hierarchySelection.value;

    private static ListView MakeListView()
    {
        var lv = new ListView
        {
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
            selectionType = SelectionType.None,
            showBorder = true,
            showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            style = { flexGrow = 1 }
        };
        lv.makeItem = () => new Label { style = { whiteSpace = WhiteSpace.Normal } };
        // bindItem will be assigned per-pane
        return lv;
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var it in _previewItems) it.selected = selected;
        _beforeList.Rebuild();
        _afterList.Rebuild();
    }

    private void OnPreview() => OnPreviewImpl();

    private void OnApply()
    {
        if (_previewItems.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing to apply", "Nothing to rename.", "OK");
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
                    Debug.LogWarning($"Skip (exists): {it.newPath}");
                    continue;
                }
                string err = AssetDatabase.MoveAsset(it.oldPath, it.newPath);
                if (!string.IsNullOrEmpty(err)) Debug.LogError($"Failed to move {it.oldPath} -> {it.newPath}: {err}");
                else Debug.Log($"Renamed: {it.oldPath} -> {it.newPath}");
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

        EditorUtility.DisplayDialog("Done", "Renaming complete. See Console for details.", "OK");
    }

    private void RefreshPreviewLive()
    {
        if (_useRegex.value && !string.IsNullOrEmpty(_findText.value))
        {
            try { _ = new Regex(_findText.value); }
            catch (Exception ex) { Debug.LogWarning($"[RenameTool] Invalid regex: {_findText.value} => {ex.Message}"); }
        }
        OnPreviewImpl();
    }

    private void OnPreviewImpl()
    {
        _previewItems.Clear();

        var find = _findText.value ?? string.Empty;
        var repl = _replaceText.value ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            // clear
            _beforeList.itemsSource = _previewItems;
            _afterList.itemsSource = _previewItems;
            _beforeList.bindItem = (ve, i) => ((Label)ve).text = string.Empty;
            _afterList.bindItem  = (ve, i) => ((Label)ve).text = string.Empty;
            _beforeList.Rebuild();
            _afterList.Rebuild();
            ShowCountSummary();
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

        // Bind before/after
        _beforeList.itemsSource = _previewItems;
        _afterList.itemsSource  = _previewItems;
        _beforeList.bindItem = (ve, i) => ((Label)ve).text = FormatBefore(_previewItems[i]);
        _afterList.bindItem  = (ve, i) => ((Label)ve).text = FormatAfter(_previewItems[i]);
        _beforeList.Rebuild();
        _afterList.Rebuild();
        ShowCountSummary();
    }

    private static string FormatBefore(RenamePreviewItem it)
    {
        return it.kind switch
        {
            RenameKind.File   => $"[FILE]  {it.oldPath}",
            RenameKind.Folder => $"[FOLDER]{it.oldPath}",
            RenameKind.HierarchyObject => $"[HIER]  {it.hierarchyPath}",
            RenameKind.AnimatorState => $"[STATE] {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorParam => $"[PARAM] {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorStateMachine => $"[SM]    {it.animatorController?.name}: {it.oldName}",
            RenameKind.AnimatorLayer => $"[LAYER] {it.animatorController?.name}: {it.oldName}",
            _ => string.Empty
        };
    }

    private static string FormatAfter(RenamePreviewItem it)
    {
        return it.kind switch
        {
            RenameKind.File   => $"{it.newPath}",
            RenameKind.Folder => $"{it.newPath}",
            RenameKind.HierarchyObject => $"[HIER]  {BuildHierarchyAfterPath(it)}",
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
        int files = _previewItems.Count(i => i.kind == RenameKind.File);
        int folders = _previewItems.Count(i => i.kind == RenameKind.Folder);
        int hierarchy = _previewItems.Count(i => i.kind == RenameKind.HierarchyObject);
        int st = _previewItems.Count(i => i.kind == RenameKind.AnimatorState);
        int sm = _previewItems.Count(i => i.kind == RenameKind.AnimatorStateMachine);
        int ly = _previewItems.Count(i => i.kind == RenameKind.AnimatorLayer);
        int pr = _previewItems.Count(i => i.kind == RenameKind.AnimatorParam);
        Debug.Log($"Preview: files={files}, folders={folders}, hierarchy={hierarchy}, states={st}, stateMachines={sm}, layers={ly}, params={pr}");
    }

    private void ApplyHierarchyChanges(List<RenamePreviewItem> hierarchyChanges)
    {
        var targets = hierarchyChanges
            .Select(it => it.hierarchyObject)
            .Where(go => go != null)
            .Distinct()
            .ToArray();

        if (targets.Length == 0) return;

        Undo.RecordObjects(targets, "Rename Hierarchy Objects");

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

            Undo.RegisterCompleteObjectUndo(ac, "Rename Animator Items");

            // Parameters first
            var paramMap = new Dictionary<string, string>();
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorParam))
            {
                string old = it.oldName;
                string neu = it.newName;
                if (ac.parameters.Any(p => p.name == neu))
                {
                    Debug.LogWarning($"Animator '{ac.name}': parameter '{neu}' already exists. Skipping rename from '{old}'.");
                    continue;
                }
                var p = ac.parameters.FirstOrDefault(pp => pp.name == old);
                if (p != null)
                {
                    p.name = neu;
                    SetParameter(ac, p);
                    paramMap[old] = neu;
                    Debug.Log($"Animator '{ac.name}': Parameter {old} -> {neu}");
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
                Debug.Log($"Animator '{ac.name}': State {it.oldName} -> {it.newName}");
            }

            // State Machines
            foreach (var it in grp.Where(i => i.kind == RenameKind.AnimatorStateMachine))
            {
                var sm = it.animatorStateMachine;
                if (sm == null) continue;
                sm.name = it.newName;
                EditorUtility.SetDirty(ac);
                Debug.Log($"Animator '{ac.name}': StateMachine {it.oldName} -> {it.newName}");
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
                Debug.Log($"Animator '{ac.name}': Layer {it.oldName} -> {it.newName}");
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
            RenameKind.File => $"[FILE]  {oldPath}  =>  {newPath}",
            RenameKind.Folder => $"[FOLDER]{oldPath}  =>  {newPath}",
            RenameKind.HierarchyObject => $"[HIER]  {hierarchyPath}  =>  {newName}",
            RenameKind.AnimatorState => $"[STATE] {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorParam => $"[PARAM] {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorStateMachine => $"[SM]    {animatorController?.name}: {oldName}  =>  {newName}",
            RenameKind.AnimatorLayer => $"[LAYER] {animatorController?.name}: {oldName}  =>  {newName}",
            _ => string.Empty
        };
    }
}
