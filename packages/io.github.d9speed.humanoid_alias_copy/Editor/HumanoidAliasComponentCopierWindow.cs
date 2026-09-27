using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UIElements;
using D9speed_BaseEditorUtils;

public partial class HumanoidAliasComponentCopierWindow : EditorWindow
{
    // ---- マッチングスコア ----
    private const int MinimumScore = 70;          // この値以上で「解決済み」とみなす
    private const int CandidateScoreThreshold = 35; // 候補一覧に載せる下限
    private const int ScoreExact = 100;
    private const int ScoreSuffix = 82;
    private const int ScoreContains = 72;
    private const int ScoreParent = 45;
    private const int AuxiliaryBonePenalty = 35;  // twist/end/roll/helper への減点
    private const int MaxCandidatesPerKey = 5;

    private ObjectField sourceField;
    private ObjectField targetField;
    private Toggle copySkinnedMeshMaterialsToggle;
    private HelpBox warningBox;
    private ListView sourceListView;
    private ListView targetListView;
    private ListView unresolvedListView;
    private ScrollView componentReportView;
    private MultiColumnListView copyListView;

    private readonly List<string> warnings = new List<string>();
    private readonly List<MatchRow> sourceRows = new List<MatchRow>();
    private readonly List<MatchRow> targetRows = new List<MatchRow>();
    private readonly List<string> unresolvedRows = new List<string>();
    private readonly List<CopyRow> copyRows = new List<CopyRow>();
    private bool copySkinnedMeshMaterials = true;
    private Dictionary<string, List<string>> aliases = new Dictionary<string, List<string>>();
    private ScanResult sourceScan;
    private ScanResult targetScan;

    private class TransformInfo
    {
        public Transform Transform;
        public string RelativePath;
        public string Name;
        public string ParentName;
        public int Depth;
    }

    private class BoneCandidate
    {
        public string StandardKey;
        public Transform Transform;
        public string RelativePath;
        public string Name;
        public string ParentName;
        public int Depth;
        public int Score;
        public int evidence_priority;
        public string MatchedAlias;
        public string Reason;
    }

    private class ScanResult
    {
        public GameObject Root;
        public readonly Dictionary<Transform, Transform> ManualTargets = new Dictionary<Transform, Transform>();
        public readonly HashSet<Transform> AmbiguousTransforms = new HashSet<Transform>();
        public readonly HashSet<string> AmbiguousKeys = new HashSet<string>();
        public Transform Armature; // Hipsの親。コピーせず対応付けのみ行う
        public Dictionary<string, List<BoneCandidate>> CandidatesByKey = new Dictionary<string, List<BoneCandidate>>();
        public Dictionary<string, BoneCandidate> ResolvedByKey = new Dictionary<string, BoneCandidate>();
        public Dictionary<Transform, Transform> SkinnedMeshTargetBySource = new Dictionary<Transform, Transform>();
        public Dictionary<string, Transform> NameMap = new Dictionary<string, Transform>();
        public readonly HashSet<Transform> skin_bones = new HashSet<Transform>();
        public readonly HashSet<Transform> bone_hierarchy = new HashSet<Transform>();
        public readonly Dictionary<string, HashSet<Transform>> humanoid_bones = new Dictionary<string, HashSet<Transform>>();
        public readonly HashSet<Transform> humanoid_transforms = new HashSet<Transform>();
    }

    private class MatchRow
    {
        public string Key;
        public string Name;
        public string Score;
        public string Detail;
        public string Tooltip;
    }

    // コピー一覧の1行: 何を / どこから / どこへ / どう解決したか
    private class CopyRow
    {
        public Component SourceComponent;
        public bool IsMaterial;
        public string Operation;
        public string SourceName;
        public string TargetName;
        public string Method;
        public string Tooltip;
    }

    private class CopyPlan
    {
        public HashSet<GameObject> AddedObjects = new HashSet<GameObject>();
        public List<Component> Components = new List<Component>();
    }

    private sealed class ComponentCategory
    {
        public readonly string Label;
        public readonly int Order;

        public ComponentCategory(string label, int order)
        {
            Label = label;
            Order = order;
        }
    }

    // カテゴリは固定の静的インスタンスを返すので、GroupBy は参照比較で正しく動く
    private static readonly ComponentCategory CategoryVrcPhysBone = new ComponentCategory("VRC PhysBone", 10);
    private static readonly ComponentCategory CategoryVrcPhysBoneCollider = new ComponentCategory("VRC PhysBone Collider", 20);
    private static readonly ComponentCategory CategoryVrcConstraint = new ComponentCategory("VRC Constraint", 30);
    private static readonly ComponentCategory CategoryUnityConstraint = new ComponentCategory("Unity Constraint", 40);
    private static readonly ComponentCategory CategoryModularAvatar = new ComponentCategory("Modular Avatar", 50);
    private static readonly ComponentCategory CategoryUnityCollider = new ComponentCategory("Unity Collider", 60);
    private static readonly ComponentCategory CategoryVrcContact = new ComponentCategory("VRC Contact", 70);
    private static readonly ComponentCategory CategoryParticle = new ComponentCategory("Particle", 80);

    private static readonly Regex AliasEntryRegex = new Regex(
        "\"(?<key>(?:\\\\.|[^\"])*)\"\\s*:\\s*\\[(?<values>.*?)\\]", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AliasValueRegex = new Regex(
        "\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);
    private static readonly Regex LeftTokenRegex = new Regex(@"(^|[\s._\-])l($|[\s._\-])", RegexOptions.Compiled);
    private static readonly Regex RightTokenRegex = new Regex(@"(^|[\s._\-])r($|[\s._\-])", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumericRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);

    [MenuItem("D9speed/Tools/Humanoid Alias Component Copier")]
    public static void ShowWindow()
    {
        var window = GetWindow<HumanoidAliasComponentCopierWindow>();
        window.titleContent = new GUIContent("Humanoidエイリアスコピー");
        window.minSize = new Vector2(860, 620);
        window.Show();
    }

    private void OnEnable()
    {
        CreateUI();
        LoadAliases();
    }

    // ================================================================
    // UI構築
    // ================================================================

    private void LoadAliases()
    {
        warnings.Clear();
        aliases = LoadAliasJson(get_dictionary_path(), warnings);
        if (aliases.Count == 0)
        {
            warnings.Add("aliases が読み込めませんでした。JSONパスを確認してください。");
        }
        Scan();
    }

    private void Scan()
    {
        warnings.Clear();
        if (aliases.Count == 0)
        {
            aliases = LoadAliasJson(get_dictionary_path(), warnings);
        }

        var sourceRoot = ResolvePrefabRoot(sourceField.value as GameObject, "コピー元", warnings);
        var targetRoot = ResolvePrefabRoot(targetField.value as GameObject, "コピー先", warnings);
        preview_stamp = null;
        if (sourceRoot != scanned_source || targetRoot != scanned_target)
        {
            manual_targets.Clear();
            excluded_components.Clear();
            excluded_materials.Clear();
            scanned_source = sourceRoot;
            scanned_target = targetRoot;
        }
        if (!ValidateRoots(sourceRoot, targetRoot))
        {
            sourceScan = targetScan = null;
            BuildRows();
            BuildPreview();
            UpdateUI();
            return;
        }
        sourceScan = BuildScanResult(sourceRoot, aliases);
        targetScan = BuildScanResult(targetRoot, aliases);
        if (sourceScan != null && targetScan != null)
        {
            foreach (var pair in manual_targets.Where(pair => pair.Key != null && pair.Value != null
                && pair.Key.IsChildOf(sourceRoot.transform) && pair.Value.IsChildOf(targetRoot.transform)))
                sourceScan.ManualTargets[pair.Key] = pair.Value;
            foreach (var key in sourceScan.AmbiguousKeys) warnings.Add($"コピー元 {key}: 同点の候補があります。手動対応を指定してください。");
            foreach (var key in targetScan.AmbiguousKeys) warnings.Add($"コピー先 {key}: 同点の候補があります。手動対応を指定してください。");
        }
        RefreshManualList();

        BuildRows();
        BuildPreview();
        UpdateUI();
    }

    private void BuildRows()
    {
        sourceRows.Clear();
        targetRows.Clear();
        unresolvedRows.Clear();
        AddRows(sourceRows, sourceScan);
        AddRows(targetRows, targetScan);

        foreach (var key in aliases.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var sourceOk = sourceScan != null && sourceScan.ResolvedByKey.ContainsKey(key);
            var targetOk = targetScan != null && targetScan.ResolvedByKey.ContainsKey(key);
            if (!sourceOk || !targetOk)
            {
                unresolvedRows.Add($"{key}  コピー元:{(sourceOk ? "○" : "✕")}  コピー先:{(targetOk ? "○" : "✕")}");
            }
        }
    }

    private static void AddRows(List<MatchRow> rows, ScanResult scan)
    {
        if (scan == null) return;

        foreach (var pair in scan.CandidatesByKey.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var candidate in pair.Value.Take(MaxCandidatesPerKey))
            {
                rows.Add(new MatchRow
                {
                    Key = candidate.StandardKey,
                    Score = candidate.Score.ToString(CultureInfo.InvariantCulture),
                    Name = candidate.Name,
                    Detail = $"親:{candidate.ParentName} / 別名:{candidate.MatchedAlias} / {candidate.Reason}",
                    Tooltip = $"パス: {(string.IsNullOrEmpty(candidate.RelativePath) ? scan.Root.name : candidate.RelativePath)}\n深さ: {candidate.Depth}"
                });
            }
        }
    }

    private void BuildPreview()
    {
        preview_stamp = null;
        copyRows.Clear();
        common_copy_rows.Clear();
        if (copy_summary != null) copy_summary.text = "コピー元とコピー先を指定してください。";
        if (common_summary != null) common_summary.text = "コピー元とコピー先を指定してください。";
        if (componentReportView != null) componentReportView.Clear();
        refresh_copy_tabs();
        if (sourceScan == null || targetScan == null) return;

        var plan = BuildCopyPlan(sourceScan, targetScan, active_copy_tab);
        var common_plan = BuildCopyPlan(sourceScan, targetScan, copy_group.other);
        foreach (var ambiguous in sourceScan.AmbiguousTransforms.Where(transform => transform != null
            && IsAmbiguousPath(transform, sourceScan, targetScan)))
        {
            var message = $"対応が曖昧: {HierarchyPathHelper.GetRelativePath(sourceScan.Root.transform, ambiguous)}（手動指定または対象から除外）";
            if (!warnings.Contains(message)) warnings.Add(message);
        }
        RebuildComponentReport(plan.Components);
        append_preview_rows(plan, copyRows);
        append_preview_rows(common_plan, common_copy_rows);
        AddSkinnedMeshMaterialPreview(common_copy_rows);
        preview_stamp = CapturePreviewStamp();
        copy_summary.text = $"このタブ: 選択 {count_selected(copyRows)} / {plan.Components.Count}件　作成予定の階層: {plan.AddedObjects.Count}件";
        common_summary.text = $"その他・共通: 選択 {count_selected(common_copy_rows)}件　作成予定の階層: {common_plan.AddedObjects.Count}件";
    }

    private void append_preview_rows(CopyPlan plan, List<CopyRow> rows)
    {
        foreach (var added in plan.AddedObjects.OrderBy(go => HumanoidMappingHelper.GetDepth(go.transform)))
        {
            var parentName = added.transform.parent != null ? added.transform.parent.name : "";
            rows.Add(new CopyRow
            {
                Operation = "選択対象の階層を作成",
                SourceName = added.name,
                TargetName = $"{added.name} (新規 / 親: {parentName})",
                Method = "新規作成",
                Tooltip = $"コピー元パス: {HumanoidMappingHelper.GetRelativePath(sourceScan.Root.transform, added.transform)}"
            });
        }

        foreach (var component in plan.Components)
        {
            var target = ResolveTargetTransform(component.transform, sourceScan, targetScan, plan.AddedObjects, null, out var method);
            string targetName;
            string targetPath;
            if (target != null)
            {
                targetName = target.name;
                targetPath = HumanoidMappingHelper.GetRelativePath(targetScan.Root.transform, target);
            }
            else if (plan.AddedObjects.Contains(component.gameObject))
            {
                // 実行時に新規作成されるオブジェクトに載るコンポーネント
                targetName = $"{component.gameObject.name} (新規)";
                targetPath = "(実行時に作成)";
                method = "新規作成";
            }
            else
            {
                targetName = "(コピー先なし)";
                targetPath = "(コピー先なし)";
            }

            var existing = target != null ? EditorComponentCopyHelper.FindCounterpart(component, target.gameObject,
                plan.Components.Where(item => !excluded_components.Contains(item))) : null;
            var operation = IsAmbiguousPath(component.transform, sourceScan, targetScan) ? "要手動対応"
                : existing == null ? "追加" : copy_mode == ComponentCopyMode.UpdateOrAdd ? "更新"
                : copy_mode == ComponentCopyMode.SkipExisting ? "スキップ" : "追加";
            if (component is ParticleSystemRenderer && existing != null && copy_mode != ComponentCopyMode.SkipExisting) operation = "更新（自動付属Renderer）";
            rows.Add(new CopyRow
            {
                SourceComponent = component,
                Operation = operation + ": " + component.GetType().Name,
                SourceName = component.gameObject.name,
                TargetName = targetName,
                Method = method ?? "未解決",
                Tooltip = $"コピー元: {HumanoidMappingHelper.GetRelativePath(sourceScan.Root.transform, component.transform)}\nコピー先: {targetPath}"
            });
            if (!excluded_components.Contains(component) && operation != "スキップ") AddReferencePreview(component, plan);
        }
    }

    private void RebuildComponentReport(List<Component> components)
    {
        if (componentReportView == null) return;
        componentReportView.Clear();

        AddSkinnedMeshPairReport();

        if (components.Count == 0)
        {
            componentReportView.Add(new Label("対象コンポーネントなし"));
            return;
        }

        var grouped = components
            .GroupBy(GetComponentCategory)
            .OrderBy(group => group.Key.Order)
            .ThenBy(group => group.Key.Label, StringComparer.Ordinal);

        foreach (var group in grouped)
        {
            var foldout = EditorUiControls.Foldout($"{group.Key.Label} ({group.Count()}件)");

            var rowIndex = 0;
            foreach (var component in group.OrderBy(c => HumanoidMappingHelper.GetRelativePath(sourceScan.Root.transform, c.transform), StringComparer.Ordinal))
            {
                var path = HumanoidMappingHelper.GetRelativePath(sourceScan.Root.transform, component.transform);
                var row = new Label($"{component.GetType().Name}: {(string.IsNullOrEmpty(path) ? sourceScan.Root.name : path)}");
                row.style.marginLeft = 16;
                ApplyZebraBackground(row, rowIndex++);
                foldout.Add(row);
            }

            componentReportView.Add(foldout);
        }
    }

    private void AddSkinnedMeshMaterialPreview(List<CopyRow> rows)
    {
        if (!copySkinnedMeshMaterials || sourceScan == null || targetScan == null) return;

        foreach (var pair in sourceScan.SkinnedMeshTargetBySource.OrderBy(p => p.Key.name, StringComparer.Ordinal))
        {
            var sourceRenderer = pair.Key != null ? pair.Key.GetComponent<SkinnedMeshRenderer>() : null;
            var targetRenderer = pair.Value != null ? pair.Value.GetComponent<SkinnedMeshRenderer>() : null;
            if (sourceRenderer == null || targetRenderer == null) continue;
            if (!HasMaterialArrayDifference(sourceRenderer, targetRenderer)) continue;

            rows.Add(new CopyRow
            {
                SourceComponent = sourceRenderer,
                IsMaterial = true,
                Operation = "マテリアル",
                SourceName = $"{sourceRenderer.name} ({sourceRenderer.sharedMaterials.Length}枠)",
                TargetName = $"{targetRenderer.name} ({targetRenderer.sharedMaterials.Length}枠)",
                Method = "SkinnedMeshペア"
            });
        }
    }

    private void AddSkinnedMeshPairReport()
    {
        if (sourceScan == null || targetScan == null) return;
        var pairs = sourceScan.SkinnedMeshTargetBySource
            .OrderBy(pair => pair.Key.name, StringComparer.Ordinal)
            .ToList();
        if (pairs.Count == 0) return;

        var foldout = EditorUiControls.Foldout($"SkinnedMesh ペア ({pairs.Count}件)");

        var rowIndex = 0;
        foreach (var pair in pairs)
        {
            var row = new Label($"{pair.Key.name} → {pair.Value.name}");
            row.tooltip = $"コピー元: {HumanoidMappingHelper.GetRelativePath(sourceScan.Root.transform, pair.Key)}\n"
                + $"コピー先: {HumanoidMappingHelper.GetRelativePath(targetScan.Root.transform, pair.Value)}";
            row.style.marginLeft = 16;
            ApplyZebraBackground(row, rowIndex++);
            foldout.Add(row);
        }

        componentReportView.Add(foldout);
    }

    private void ExecuteCopy()
    {
        execute_group_copy(active_copy_tab);
    }

    private void execute_group_copy(copy_group group_to_copy)
    {
        if (EditorApplication.isPlaying || sourceScan == null || targetScan == null
            || !ValidateRoots(sourceScan.Root, targetScan.Root))
        {
            warnings.Add("有効なコピー元・コピー先をスキャンしてください。");
            UpdateUI();
            return;
        }
        if (preview_stamp == null || CapturePreviewStamp() != preview_stamp)
        {
            Scan();
            warnings.Add("プレビュー後に対象が変更されました。更新された一覧を確認してから、再度コピーを実行してください。");
            UpdateUI();
            return;
        }
        var plan = BuildCopyPlan(sourceScan, targetScan, group_to_copy);
        var selected = plan.Components.Where(component => component != null && !excluded_components.Contains(component)).ToList();
        var copy_materials = group_to_copy == copy_group.other && copySkinnedMeshMaterials;
        if (selected.Count == 0 && !(copy_materials && common_copy_rows.Any(row => row.IsMaterial && !excluded_materials.Contains(row.SourceComponent)))) return;
        if (selected.Any(component => IsAmbiguousPath(component.transform, sourceScan, targetScan)))
        {
            warnings.Add("曖昧な対応が残っています。手動指定するか、そのコンポーネントをチェックから外してください。");
            UpdateUI();
            return;
        }
        var addedMap = new Dictionary<GameObject, GameObject>();
        var componentMap = new Dictionary<Component, Component>();
        var copiedPairs = new List<KeyValuePair<Component, Component>>();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Humanoid Alias Copy");
        try
        {
            Undo.RegisterFullObjectHierarchyUndo(targetScan.Root, "Humanoid Alias Copy");
            foreach (var added in plan.AddedObjects.OrderBy(go => HumanoidMappingHelper.GetDepth(go.transform)))
                if (!addedMap.ContainsKey(added)) CreateMappedObject(added, sourceScan, targetScan, plan.AddedObjects, addedMap, warnings);
            if (copy_materials) CopyPairedSkinnedMeshMaterials(sourceScan);
            var used_targets = new HashSet<Component>();
            foreach (var component in selected)
            {
                var transform = ResolveTargetTransform(component.transform, sourceScan, targetScan, plan.AddedObjects, addedMap);
                if (transform == null) throw new InvalidOperationException($"コピー先が未解決です: {component.name}");
                var existing = EditorComponentCopyHelper.FindCounterpart(component, transform.gameObject, selected);
                if (copy_mode == ComponentCopyMode.SkipExisting && existing != null)
                {
                    componentMap[component] = existing;
                    continue;
                }
                var destination = copy_mode == ComponentCopyMode.UpdateOrAdd && existing != null
                    ? existing : AddOrReuseComponent(transform.gameObject, component);
                if (destination == null) throw new InvalidOperationException($"追加できません: {component.GetType().Name} / {transform.name}");
                if (!used_targets.Add(destination)) throw new InvalidOperationException($"複数のコピー元が同じコンポーネントを更新します: {destination.name}");
                componentMap[component] = destination;
                copiedPairs.Add(new KeyValuePair<Component, Component>(component, destination));
            }
            foreach (var pair in copiedPairs)
            {
                Undo.RecordObject(pair.Value, "Copy component values");
                if (!TryCopySerializedComponent(pair.Key, pair.Value, warnings)) throw new InvalidOperationException("値のコピーに失敗しました。");
            }
            foreach (var pair in copiedPairs)
            {
                RemapSerializedObjectReferences(pair.Value, sourceScan, targetScan, plan.AddedObjects, addedMap, componentMap);
                EditorComponentCopyHelper.RecordPrefabChanges(pair.Value);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(targetScan.Root.scene);
            warnings.Add($"コピー完了: {copy_group_label(group_to_copy)} / コンポーネント {copiedPairs.Count}件。Undoで一括して戻せます。");
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(group);
            warnings.Add($"コピーを取り消しました: {exception.Message}");
        }
        finally
        {
            Undo.IncrementCurrentGroup();
            var result = warnings.ToList();
            Scan();
            warnings.AddRange(result);
            UpdateUI();
        }
    }

    // ================================================================
    // コピー処理
    // ================================================================

    private static bool TryCopySerializedComponent(Component sourceComponent, Component targetComponent, List<string> warningsOut)
    {
        if (sourceComponent == null || targetComponent == null)
        {
            warningsOut.Add("CopySerialized をスキップしました: コピー元またはコピー先が null です。");
            return false;
        }

        try
        {
            EditorUtility.CopySerialized(sourceComponent, targetComponent);
            return true;
        }
        catch (Exception e)
        {
            warningsOut.Add($"CopySerialized に失敗: {sourceComponent.GetType().Name} / {sourceComponent.gameObject.name} / {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    private void CopyPairedSkinnedMeshMaterials(ScanResult source)
    {
        foreach (var pair in source.SkinnedMeshTargetBySource)
        {
            var sourceRenderer = pair.Key != null ? pair.Key.GetComponent<SkinnedMeshRenderer>() : null;
            var targetRenderer = pair.Value != null ? pair.Value.GetComponent<SkinnedMeshRenderer>() : null;
            if (sourceRenderer == null || targetRenderer == null) continue;
            if (!HasMaterialArrayDifference(sourceRenderer, targetRenderer)) continue;

            if (excluded_materials.Contains(sourceRenderer)) continue;
            Undo.RecordObject(targetRenderer, "Copy SkinnedMesh Materials");
            targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials.ToArray();
            EditorComponentCopyHelper.RecordPrefabChanges(targetRenderer);
        }
    }

    // ================================================================
    // エイリアスJSON読み込み
    // ================================================================

    private static string get_dictionary_path()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HumanoidAliasComponentCopierWindow).Assembly);
        return package == null ? string.Empty : Path.Combine(package.resolvedPath, "Editor", "Data", "humanoid_bone_dictionary.json");
    }

    private static Dictionary<string, List<string>> LoadAliasJson(string path, List<string> warningsOut)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            warningsOut.Add($"JSONが見つかりません: {path}");
            return result;
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        foreach (Match entry in AliasEntryRegex.Matches(json))
        {
            var key = UnescapeJson(entry.Groups["key"].Value);
            if (string.IsNullOrEmpty(key)) continue;

            var values = new List<string> { key };
            foreach (Match value in AliasValueRegex.Matches(entry.Groups["values"].Value))
            {
                values.Add(UnescapeJson(value.Groups["value"].Value));
            }
            result[key] = values.Distinct(StringComparer.Ordinal).ToList();
        }
        return result;
    }

    private static string UnescapeJson(string value)
    {
        return Regex.Unescape(value.Replace("\\/", "/"));
    }

    // ================================================================
    // ボーンスキャン / スコアリング
    // ================================================================

    private static ScanResult BuildScanResult(GameObject root, Dictionary<string, List<string>> aliasMap)
    {
        if (root == null || aliasMap == null) return null;
        var scan = new ScanResult { Root = root };
        collect_bone_evidence(scan);
        var transforms = CollectTransformInfos(root.transform);

        foreach (var pair in aliasMap)
        {
            var candidates = new List<BoneCandidate>();
            foreach (var info in transforms)
            {
                var candidate = score_bone_candidate(scan, pair.Key, pair.Value, info);
                if (candidate != null && candidate.Score >= CandidateScoreThreshold)
                {
                    candidates.Add(candidate);
                }
            }

            candidates = candidates
                .OrderByDescending(c => c.evidence_priority)
                .ThenByDescending(c => c.Score)
                .ThenBy(c => c.Depth)
                .ThenBy(c => c.RelativePath, StringComparer.Ordinal)
                .ToList();
            scan.CandidatesByKey[pair.Key] = candidates;
            var best = candidates.FirstOrDefault(c => c.Score >= MinimumScore);
            if (best != null)
            {
                var tied = candidates.Where(candidate => candidate.evidence_priority == best.evidence_priority
                    && candidate.Score == best.Score).ToList();
                if (tied.Count == 1) scan.ResolvedByKey[pair.Key] = best;
                else
                {
                    scan.AmbiguousKeys.Add(pair.Key);
                    foreach (var candidate in tied) scan.AmbiguousTransforms.Add(candidate.Transform);
                }
            }
        }

        scan.NameMap = root.GetComponentsInChildren<Transform>(true)
            .GroupBy(transform => transform.name).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First());
        scan.Armature = FindArmature(scan);
        return scan;
    }

    // Hipsの親をアーマチュアとみなす。アーマチュア自体はコピーせず、対応付けのみに使う。
    private static Transform FindArmature(ScanResult scan)
    {
        foreach (var pair in scan.ResolvedByKey)
        {
            if (NormalizeBoneName(pair.Key) != "hips") continue;
            var hips = pair.Value.Transform;
            return hips != null ? hips.parent : null;
        }
        return null;
    }

    private static List<TransformInfo> CollectTransformInfos(Transform root)
    {
        var list = new List<TransformInfo>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            list.Add(new TransformInfo
            {
                Transform = transform,
                RelativePath = HumanoidMappingHelper.GetRelativePath(root, transform),
                Name = transform.name,
                ParentName = transform.parent != null ? transform.parent.name : "",
                Depth = HumanoidMappingHelper.GetDepthFrom(root, transform)
            });
        }
        return list;
    }

    private static BoneCandidate ScoreTransform(string key, List<string> keyAliases, TransformInfo info)
    {
        var normalizedName = NormalizeBoneName(info.Name);
        var normalizedParent = NormalizeBoneName(info.ParentName);
        var penalty = GetPenalty(normalizedName);
        var bestScore = 0;
        var bestAlias = "";
        var bestReason = "";

        foreach (var alias in keyAliases)
        {
            var normalizedAlias = NormalizeBoneName(alias);
            var score = 0;
            var reason = "";

            if (normalizedName == normalizedAlias)
            {
                score = ScoreExact;
                reason = "完全一致";
            }
            else if (normalizedName.EndsWith(normalizedAlias, StringComparison.Ordinal)
                || normalizedAlias.EndsWith(normalizedName, StringComparison.Ordinal))
            {
                score = ScoreSuffix;
                reason = "末尾一致";
            }
            else if (normalizedName.Contains(normalizedAlias) || normalizedAlias.Contains(normalizedName))
            {
                score = ScoreContains;
                reason = "部分一致";
            }
            else if (!string.IsNullOrEmpty(normalizedParent) && normalizedParent.Contains(normalizedAlias))
            {
                score = ScoreParent;
                reason = "親名一致";
            }

            score -= penalty;
            if (score > bestScore)
            {
                bestScore = score;
                bestAlias = alias;
                bestReason = reason;
            }
        }

        return new BoneCandidate
        {
            StandardKey = key,
            Transform = info.Transform,
            RelativePath = info.RelativePath,
            Name = info.Name,
            ParentName = info.ParentName,
            Depth = info.Depth,
            Score = Mathf.Clamp(bestScore, 0, 100),
            MatchedAlias = bestAlias,
            Reason = bestReason
        };
    }

    private static string NormalizeBoneName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        text = text.Replace("左", " left ").Replace("右", " right ");
        text = LeftTokenRegex.Replace(text, " left ");
        text = RightTokenRegex.Replace(text, " right ");
        text = NonAlphanumericRegex.Replace(text, "");
        return text;
    }

    // 補助ボーン(twist/end/roll/helper)を弾くための減点。normalizedName は正規化済みであること。
    private static int GetPenalty(string normalizedName)
    {
        var penalty = 0;
        if (normalizedName.Contains("twist")) penalty += AuxiliaryBonePenalty;
        if (normalizedName.Contains("end")) penalty += AuxiliaryBonePenalty;
        if (normalizedName.Contains("roll")) penalty += AuxiliaryBonePenalty;
        if (normalizedName.Contains("helper")) penalty += AuxiliaryBonePenalty;
        return penalty;
    }

    // ================================================================
    // コピー計画
    // ================================================================

    private CopyPlan BuildCopyPlan(ScanResult source, ScanResult target, copy_group group_to_copy)
    {
        var plan = new CopyPlan();
        if (source == null || target == null) return plan;

        RebuildSkinnedMeshPairs(source, target);

        foreach (var component in source.Root.GetComponentsInChildren<Component>(true))
        {
            if (!IsSupportedComponent(component) || get_copy_group(component) != group_to_copy) continue;
            plan.Components.Add(component);
            if (!excluded_components.Contains(component) && !IsAmbiguousPath(component.transform, source, target))
                AddMissingHierarchy(component.transform, source, target, plan.AddedObjects);
        }

        return plan;
    }

    private static void RebuildSkinnedMeshPairs(ScanResult source, ScanResult target)
    {
        source.SkinnedMeshTargetBySource.Clear();
        if (source.Root == null || target.Root == null) return;

        var sourceRenderers = source.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var targetRenderers = target.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (sourceRenderers.Length == 0 || targetRenderers.Length == 0) return;

        var sourceByName = sourceRenderers
            .GroupBy(renderer => renderer.name)
            .ToDictionary(group => group.Key, group => new Queue<SkinnedMeshRenderer>(group));
        var targetByName = targetRenderers
            .GroupBy(renderer => renderer.name)
            .ToDictionary(group => group.Key, group => new Queue<SkinnedMeshRenderer>(group));

        var ignoreTokens = BuildSkinnedMeshIgnoreTokens(source.Root.name, target.Root.name);
        var pairs = GameObjectNamePairingUniqueFinder.PairByHeuristicKeys(
            sourceRenderers.Select(renderer => renderer.name),
            targetRenderers.Select(renderer => renderer.name),
            ignoreTokens);
        var ambiguous_pairs = new HashSet<string>(pairs.GroupBy(pair => pair.Key)
            .Where(group => group.Count() > 1).Select(group => group.Key));

        foreach (var pair in pairs)
        {
            if (string.IsNullOrEmpty(pair.A) || string.IsNullOrEmpty(pair.B)) continue;
            if (!sourceByName.TryGetValue(pair.A, out var sourceQueue) || sourceQueue.Count == 0) continue;
            if (!targetByName.TryGetValue(pair.B, out var targetQueue) || targetQueue.Count == 0) continue;

            // 同名が複数あるペアは出現順で決めず、手動対応を要求する。
            if (ambiguous_pairs.Contains(pair.Key) || sourceQueue.Count != 1 || targetQueue.Count != 1)
            {
                foreach (var renderer in sourceQueue) source.AmbiguousTransforms.Add(renderer.transform);
                continue;
            }
            var sourceRenderer = sourceQueue.Dequeue();
            var targetRenderer = targetQueue.Dequeue();
            if (sourceRenderer != null && targetRenderer != null)
            {
                source.SkinnedMeshTargetBySource[sourceRenderer.transform] = targetRenderer.transform;
            }
        }
        foreach (var pair in source.ManualTargets)
        {
            if (pair.Key.GetComponent<SkinnedMeshRenderer>() == null || pair.Value.GetComponent<SkinnedMeshRenderer>() == null) continue;
            foreach (var auto in source.SkinnedMeshTargetBySource.Where(item => item.Value == pair.Value).Select(item => item.Key).ToArray())
                source.SkinnedMeshTargetBySource.Remove(auto);
            source.SkinnedMeshTargetBySource[pair.Key] = pair.Value;
        }
    }

    private static HashSet<string> BuildSkinnedMeshIgnoreTokens(params string[] names)
    {
        var ignoreTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "armature"
        };

        foreach (var name in names)
        {
            foreach (var token in GameObjectNamePairingUniqueFinder.Tokenize(name))
            {
                ignoreTokens.Add(token);
            }
        }

        return ignoreTokens;
    }

    private static bool IsSupportedComponent(Component component)
    {
        return GetComponentCategory(component) != null;
    }

    private static ComponentCategory GetComponentCategory(Component component)
    {
        if (component == null || component is Transform) return null;
        var type = component.GetType();
        var fullName = type.FullName ?? "";
        var typeName = type.Name ?? "";

        if (fullName.Contains("VRCPhysBoneCollider") || typeName.Contains("VRCPhysBoneCollider"))
        {
            return CategoryVrcPhysBoneCollider;
        }

        if (fullName.Contains("VRCPhysBone") || typeName.Contains("VRCPhysBone"))
        {
            return CategoryVrcPhysBone;
        }

        if (fullName.StartsWith("VRC.SDK3.Dynamics.Constraint.Components.", StringComparison.Ordinal))
        {
            return CategoryVrcConstraint;
        }

        if (component is IConstraint)
        {
            return CategoryUnityConstraint;
        }

        if (fullName.StartsWith("nadena.dev.modular_avatar.core.", StringComparison.OrdinalIgnoreCase)
            || fullName.IndexOf("ModularAvatar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CategoryModularAvatar;
        }

        if (component is Collider || component is Collider2D)
        {
            return CategoryUnityCollider;
        }

        if (fullName.StartsWith("VRC.SDK3.Dynamics.Contact.Components.", StringComparison.Ordinal))
        {
            return CategoryVrcContact;
        }

        if (component is ParticleSystem || component is ParticleSystemRenderer)
        {
            return CategoryParticle;
        }

        return null;
    }

    private static Component AddOrReuseComponent(GameObject targetObject, Component sourceComponent)
    {
        if (targetObject == null || sourceComponent == null) return null;

        if (sourceComponent is ParticleSystemRenderer)
        {
            var existingRenderer = targetObject.GetComponent<ParticleSystemRenderer>();
            if (existingRenderer != null)
            {
                Undo.RecordObject(existingRenderer, "Reuse ParticleSystemRenderer");
                return existingRenderer;
            }
        }

        try
        {
            return Undo.AddComponent(targetObject, sourceComponent.GetType());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasMaterialArrayDifference(Renderer sourceRenderer, Renderer targetRenderer)
    {
        if (sourceRenderer == null || targetRenderer == null) return false;
        var sourceMaterials = sourceRenderer.sharedMaterials;
        var targetMaterials = targetRenderer.sharedMaterials;
        if (sourceMaterials.Length != targetMaterials.Length) return true;

        for (var i = 0; i < sourceMaterials.Length; i++)
        {
            if (sourceMaterials[i] != targetMaterials[i]) return true;
        }

        return false;
    }

    // ================================================================
    // Transform解決
    // ================================================================

    private static void AddMissingHierarchy(Transform start, ScanResult source, ScanResult target, HashSet<GameObject> addedObjects)
    {
        var current = start;
        while (current != null && current != source.Root.transform)
        {
            // アーマチュアはオブジェクトとして作成しない(対応付けのみ)
            if (current == source.Armature) break;
            if (ResolveExistingTargetTransform(current, source, target) != null) break;
            addedObjects.Add(current.gameObject);
            current = current.parent;
        }
    }

    private static GameObject CreateMappedObject(
        GameObject sourceObject, ScanResult source, ScanResult target, HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap, List<string> warningsOut)
    {
        if (addedMap.TryGetValue(sourceObject, out var existing)) return existing;
        var parent = ResolveTargetParent(sourceObject.transform.parent, source, target, addedObjects, addedMap, warningsOut);
        if (parent == null) throw new InvalidOperationException($"親が未解決です: {sourceObject.name}");
        var created = new GameObject(sourceObject.name);
        Undo.RegisterCreatedObjectUndo(created, "Create mapped object");
        Undo.SetTransformParent(created.transform, parent, "Parent mapped object");
        HumanoidMappingHelper.CopyTransformValues(sourceObject.transform, created.transform);
        created.layer = sourceObject.layer;
        created.tag = sourceObject.tag;
        created.SetActive(sourceObject.activeSelf);
        addedMap[sourceObject] = created;
        return created;
    }

    private static Transform ResolveTargetParent(
        Transform sourceParent,
        ScanResult source,
        ScanResult target,
        HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap,
        List<string> warningsOut)
    {
        if (sourceParent == null || sourceParent == source.Root.transform) return target.Root.transform;
        if (addedObjects.Contains(sourceParent.gameObject))
        {
            var parentObject = CreateMappedObject(sourceParent.gameObject, source, target, addedObjects, addedMap, warningsOut);
            return parentObject != null ? parentObject.transform : target.Root.transform;
        }

        var resolved = ResolveExistingTargetTransform(sourceParent, source, target);
        if (resolved != null) return resolved;

        throw new InvalidOperationException($"親Transformを解決できません: {sourceParent.name}");
    }

    private static Transform ResolveTargetTransform(
        Transform sourceTransform,
        ScanResult source,
        ScanResult target,
        HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap)
    {
        return ResolveTargetTransform(sourceTransform, source, target, addedObjects, addedMap, out _);
    }

    private static Transform ResolveTargetTransform(
        Transform sourceTransform,
        ScanResult source,
        ScanResult target,
        HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap,
        out string method)
    {
        method = null;
        if (sourceTransform == null) return null;
        if (addedObjects != null && addedObjects.Contains(sourceTransform.gameObject))
        {
            method = "新規作成";
            return addedMap != null && addedMap.TryGetValue(sourceTransform.gameObject, out var mapped) ? mapped.transform : null;
        }
        return ResolveExistingTargetTransform(sourceTransform, source, target, out method);
    }

    private static Transform ResolveExistingTargetTransform(Transform sourceTransform, ScanResult source, ScanResult target)
    {
        return ResolveExistingTargetTransform(sourceTransform, source, target, out _);
    }

    // 対応するコピー先Transformを探し、どの方法で解決したかを method に返す
    private static Transform ResolveExistingTargetTransform(Transform sourceTransform, ScanResult source, ScanResult target, out string method)
    {
        method = null;
        if (sourceTransform == null || source == null || target == null) return null;
        if (source.ManualTargets.TryGetValue(sourceTransform, out var manual) && manual != null)
        {
            method = "手動指定";
            return manual;
        }
        foreach (var ancestor in source.ManualTargets.OrderByDescending(pair => HumanoidMappingHelper.GetDepth(pair.Key)))
        {
            if (!sourceTransform.IsChildOf(ancestor.Key)) continue;
            var path = HumanoidMappingHelper.GetRelativePath(ancestor.Key, sourceTransform);
            var child = ancestor.Value.Find(path);
            if (child != null) { method = "手動指定配下"; return child; }
        }
        if (IsAmbiguousPath(sourceTransform, source, target)) return null;
        if (sourceTransform == source.Root.transform)
        {
            method = "ルート";
            return target.Root.transform;
        }

        // コピー元ルート配下以外(アバター本体など外部参照)は対応付けしない。
        // これを許すと MA MergeArmature の mergeTarget(アバターのArmature)が
        // 名前一致で衣装側Armatureに付け替えられてしまう。
        if (!HumanoidMappingHelper.IsDescendantOf(sourceTransform, source.Root.transform)) return null;

        // アーマチュア(Hipsの親)は作成・コピーせず、相手側アーマチュアへ対応付ける
        if (source.Armature != null && sourceTransform == source.Armature && target.Armature != null)
        {
            method = "アーマチュア";
            return target.Armature;
        }

        if (source.SkinnedMeshTargetBySource.TryGetValue(sourceTransform, out var pairedSkinnedMesh)
            && pairedSkinnedMesh != null)
        {
            method = "SkinnedMeshペア";
            return pairedSkinnedMesh;
        }

        foreach (var pair in source.SkinnedMeshTargetBySource)
        {
            if (pair.Key == null || pair.Value == null) continue;
            if (!HumanoidMappingHelper.IsDescendantOf(sourceTransform, pair.Key)) continue;

            var subPath = HumanoidMappingHelper.GetRelativePath(pair.Key, sourceTransform);
            if (string.IsNullOrEmpty(subPath))
            {
                method = "SkinnedMeshペア";
                return pair.Value;
            }
            var targetChild = pair.Value.Find(subPath);
            if (targetChild != null && have_matching_bone_roles(sourceTransform, targetChild, source, target))
            {
                method = "SkinnedMeshペア配下";
                return targetChild;
            }
        }

        var directKey = FindResolvedKey(source.ResolvedByKey, sourceTransform);
        if (!string.IsNullOrEmpty(directKey) && target.ResolvedByKey.TryGetValue(directKey, out var directTarget))
        {
            method = $"ボーン: {directKey}";
            return directTarget.Transform;
        }

        foreach (var pair in source.ResolvedByKey.OrderByDescending(p => p.Value.Depth))
        {
            var sourceBone = pair.Value.Transform;
            if (!HumanoidMappingHelper.IsDescendantOf(sourceTransform, sourceBone)) continue;
            if (!target.ResolvedByKey.TryGetValue(pair.Key, out var targetBone)) continue;

            var subPath = HumanoidMappingHelper.GetRelativePath(sourceBone, sourceTransform);
            if (string.IsNullOrEmpty(subPath))
            {
                method = $"ボーン: {pair.Key}";
                return targetBone.Transform;
            }
            var targetChild = targetBone.Transform.Find(subPath);
            if (targetChild != null && have_matching_bone_roles(sourceTransform, targetChild, source, target))
            {
                method = $"ボーン配下: {pair.Key}";
                return targetChild;
            }
        }

        // 補助オブジェクトは元の階層を保つ。同名の実ボーンには割り当てない。
        var relative_path = HumanoidMappingHelper.GetRelativePath(source.Root.transform, sourceTransform);
        var same_path = find_unique_relative_path(target.Root.transform, relative_path);
        if (same_path != null && have_matching_bone_roles(sourceTransform, same_path, source, target))
        {
            method = "階層パス一致";
            return same_path;
        }
        if (source.bone_hierarchy.Count > 0 && !is_bone_transform(sourceTransform, source)) return null;

        // リグ情報のない階層などでは、従来の一意な名前一致も使用する。
        if (target.NameMap.TryGetValue(sourceTransform.name, out var sameName) && sameName != null
            && have_matching_bone_roles(sourceTransform, sameName, source, target))
        {
            method = "名前一致";
            return sameName;
        }

        return null;
    }

    private static string FindResolvedKey(Dictionary<string, BoneCandidate> map, Transform transform)
    {
        foreach (var pair in map)
        {
            if (pair.Value.Transform == transform) return pair.Key;
        }
        return null;
    }

    private static void RemapSerializedObjectReferences(
        Component targetComponent,
        ScanResult source,
        ScanResult target,
        HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap,
        Dictionary<Component, Component> componentMap)
    {
        var serializedObject = new SerializedObject(targetComponent);
        var property = serializedObject.GetIterator();
        var enterChildren = true;
        while (property.Next(enterChildren))
        {
            enterChildren = true;
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_Script" || property.propertyPath == "m_GameObject") continue;
            var remapped = RemapObjectReference(property.objectReferenceValue, source, target, addedObjects, addedMap, componentMap);
            if (remapped != property.objectReferenceValue)
            {
                property.objectReferenceValue = remapped;
            }
        }
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static UnityEngine.Object RemapObjectReference(
        UnityEngine.Object reference,
        ScanResult source,
        ScanResult target,
        HashSet<GameObject> addedObjects,
        Dictionary<GameObject, GameObject> addedMap,
        Dictionary<Component, Component> componentMap)
    {
        if (reference == null) return null;
        if (reference is Component sourceComponent)
        {
            if (componentMap.TryGetValue(sourceComponent, out var mappedComponent)) return mappedComponent;
            var mappedTransform = ResolveTargetTransform(sourceComponent.transform, source, target, addedObjects, addedMap);
            if (mappedTransform == null) return reference;
            if (sourceComponent is Transform) return mappedTransform;
            var sameType = EditorComponentCopyHelper.FindCounterpart(sourceComponent, mappedTransform.gameObject);
            // 選択順で割り当てたコピー先を、除外した別コンポーネントの参照先として流用しない。
            if (sameType != null && componentMap.Values.Contains(sameType)) return reference;
            return sameType != null ? sameType : reference;
        }
        if (reference is GameObject sourceObject)
        {
            var mappedTransform = ResolveTargetTransform(sourceObject.transform, source, target, addedObjects, addedMap);
            return mappedTransform != null ? mappedTransform.gameObject : reference;
        }
        return reference;
    }

    private static GameObject ResolvePrefabRoot(GameObject selection, string label, List<string> warningsOut)
    {
        if (selection == null) return null;
        var root = PrefabUtility.GetNearestPrefabInstanceRoot(selection);
        if (root != null && root != selection)
        {
            warningsOut.Add($"{label}: Prefab内の子が選択されたため root '{root.name}' を使用します。");
            return root;
        }
        return selection;
    }

    private void UpdateUI()
    {
        var ready = sourceScan != null && targetScan != null && preview_stamp != null && !EditorApplication.isPlaying;
        execute_button?.SetEnabled(ready && count_selected(copyRows) > 0);
        common_copy_button?.SetEnabled(ready && count_selected(common_copy_rows) > 0);
        sourceListView?.Rebuild();
        targetListView?.Rebuild();
        unresolvedListView?.Rebuild();
        copyListView?.Rebuild();
        common_copy_list?.Rebuild();
        copy_empty_label?.EnableInClassList("d9_hidden", copyRows.Count > 0);
        common_empty_label?.EnableInClassList("d9_hidden", common_copy_rows.Count > 0);

        if (warningBox != null)
        {
            var text = string.Join("\n", warnings.Distinct());
            warning_scroll?.EnableInClassList("d9_hidden", string.IsNullOrWhiteSpace(text));
            warningBox.messageType = warnings.All(w => w.StartsWith("コピー完了:")) ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
            warningBox.text = text;
        }
    }
}
