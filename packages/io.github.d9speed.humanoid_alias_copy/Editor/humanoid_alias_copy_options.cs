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
    private ComponentCopyMode copy_mode = ComponentCopyMode.UpdateOrAdd;
    private readonly HashSet<Component> excluded_components = new HashSet<Component>();
    private readonly HashSet<Component> excluded_materials = new HashSet<Component>();
    private readonly Dictionary<Transform, Transform> manual_targets = new Dictionary<Transform, Transform>();
    private VisualElement manual_list;
    private Button execute_button;
    private Label copy_summary;
    private string preview_stamp;
    private GameObject scanned_source;
    private GameObject scanned_target;

    private VisualElement BuildCopyOptions()
    {
        var panel = new VisualElement();
        var modes = new List<string> { "既存を更新・不足分を追加", "常に追加", "既存はスキップ・不足分を追加" };
        var mode = new DropdownField("コンポーネントのコピー方法", modes, (int)copy_mode);
        mode.RegisterValueChangedCallback(evt => { copy_mode = (ComponentCopyMode)modes.IndexOf(evt.newValue); RefreshPreview(); });
        panel.Add(mode);
        mode.tooltip = "同じ型のコンポーネントは、チェックされたコピー元の並び順でコピー先に対応付けます。";
        var manual = new Foldout { text = "対応先を手動指定（ボーン・メッシュ・その他の階層）", value = false };
        var from = new ObjectField("コピー元Transform") { objectType = typeof(Transform), allowSceneObjects = true };
        var to = new ObjectField("対応先Transform") { objectType = typeof(Transform), allowSceneObjects = true };
        manual.Add(from);
        manual.Add(to);
        manual.Add(new Button(() =>
        {
            var source = from.value as Transform;
            var target = to.value as Transform;
            if (sourceScan == null || targetScan == null || source == null || target == null
                || !source.IsChildOf(sourceScan.Root.transform) || !target.IsChildOf(targetScan.Root.transform))
            {
                warnings.Add("スキャン後、コピー元・コピー先それぞれの配下にあるTransformを指定してください。");
                UpdateUI();
                return;
            }
            if (source == sourceScan.Root.transform || target == targetScan.Root.transform)
            {
                warnings.Add("ルート同士の対応は固定です。配下のTransformを指定してください。");
                UpdateUI();
                return;
            }
            if (manual_targets.Any(pair => pair.Key != source && pair.Value == target))
            {
                warnings.Add("そのコピー先は別の手動対応で使用中です。");
                UpdateUI();
                return;
            }
            manual_targets[source] = target;
            Scan();
        }) { text = "この対応を使用" });
        manual_list = new VisualElement();
        manual.Add(manual_list);
        panel.Add(manual);
        panel.Add(new HelpBox("一覧のチェックでコンポーネント・マテリアルを除外できます。不足する階層は、選択対象だけを含む通常のGameObjectとして作成します。", HelpBoxMessageType.Info));
        return panel;
    }

    private void RefreshPreview()
    {
        Scan();
    }

    private void RefreshManualList()
    {
        manual_list?.Clear();
        foreach (var pair in manual_targets.ToArray())
        {
            if (pair.Key == null || pair.Value == null) { manual_targets.Remove(pair.Key); continue; }
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label($"{HierarchyPathHelper.GetHierarchyPath(pair.Key)} → {HierarchyPathHelper.GetHierarchyPath(pair.Value)}") { style = { flexGrow = 1 } });
            row.Add(new Button(() => { manual_targets.Remove(pair.Key); Scan(); }) { text = "解除" });
            manual_list?.Add(row);
        }
    }

    private string CapturePreviewStamp()
    {
        return EditorComponentCopyHelper.GetHierarchyStamp(sourceScan?.Root)
            + ":" + EditorComponentCopyHelper.GetHierarchyStamp(targetScan?.Root);
    }

    private bool ValidateRoots(GameObject source, GameObject target)
    {
        if (source == null || target == null) return false;
        if (EditorUtility.IsPersistent(source) || EditorUtility.IsPersistent(target)
            || !source.scene.IsValid() || !target.scene.IsValid() || !source.scene.isLoaded || !target.scene.isLoaded
            || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(source) != null
            || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(target) != null)
        {
            warnings.Add("シーン上のオブジェクトを指定してください。Prefabアセット・Prefab編集モードは対象外です。");
            return false;
        }
        if (source == target || source.transform.IsChildOf(target.transform) || target.transform.IsChildOf(source.transform))
        {
            warnings.Add("コピー元とコピー先には、互いに独立した階層を指定してください。");
            return false;
        }
        return true;
    }

    private static bool IsAmbiguousPath(Transform source_transform, ScanResult source, ScanResult target)
    {
        for (var current = source_transform; current != null && current != source.Root.transform; current = current.parent)
        {
            if (source.ManualTargets.ContainsKey(current)) return false;
            if (source.AmbiguousTransforms.Contains(current)) return true;
            var key = FindResolvedKey(source.ResolvedByKey, current);
            if (key != null && target.AmbiguousKeys.Contains(key)) return true;
        }
        return false;
    }

    private void AddReferencePreview(Component component, CopyPlan plan)
    {
        var issues = new List<string>();
        using (var serialized = new SerializedObject(component))
        {
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_GameObject" || property.propertyPath == "m_Script") continue;
                var reference = property.objectReferenceValue;
                if (!EditorComponentCopyHelper.IsInside(reference, sourceScan.Root.transform)) continue;
                var transform = reference is GameObject go ? go.transform : ((Component)reference).transform;
                if (reference is Component excluded && excluded_components.Contains(excluded)
                    && plan.Components.Any(item => item != null && !excluded_components.Contains(item)
                        && item.gameObject == excluded.gameObject && item.GetType() == excluded.GetType()))
                {
                    issues.Add($"{property.propertyPath}: 除外した同型コンポーネントへの参照");
                    continue;
                }
                if (plan.AddedObjects.Contains(transform.gameObject))
                {
                    if (reference is GameObject || reference is Transform
                        || reference is Component selected && plan.Components.Contains(selected) && !excluded_components.Contains(selected)) continue;
                }
                var resolved = ResolveExistingTargetTransform(transform, sourceScan, targetScan);
                if (resolved != null)
                {
                    if (reference is GameObject || reference is Transform) continue;
                    var source_component = (Component)reference;
                    if (EditorComponentCopyHelper.FindCounterpart(source_component, resolved.gameObject) != null
                        || plan.Components.Contains(source_component) && !excluded_components.Contains(source_component)) continue;
                }
                issues.Add($"{property.propertyPath}: {HierarchyPathHelper.GetRelativePath(sourceScan.Root.transform, transform)}");
            }
        }
        if (issues.Count == 0) return;
        var foldout = new Foldout { text = $"コピー元への参照が残る可能性: {component.name} / {component.GetType().Name} ({issues.Count})", value = true };
        foreach (var issue in issues) foldout.Add(new Label(issue));
        componentReportView?.Add(foldout);
    }
}
