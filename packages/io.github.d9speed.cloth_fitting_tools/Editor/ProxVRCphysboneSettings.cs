using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

public class ProxVRCphysboneSettings : EditorWindow
{
    private Transform actualChainRoot;
    private bool includeActualRoot = true;
    private readonly List<Transform> proxyTargets = new List<Transform>();

    private float nearDistance = 0f;
    private float farDistance = 0.08f;
    private float nearWeight = 1f;
    private float farWeight = 0.15f;
    private int sourceBlendCount = 3;
    private float sourceFalloffPower = 2f;
    private ConstraintKind constraintKind = ConstraintKind.Rotation;
    private bool replaceExistingSources = true;
    private bool keepCurrentOffset = true;
    private bool lockAfterSetup = true;
    private bool affectsPositionX = true;
    private bool affectsPositionY = true;
    private bool affectsPositionZ = true;
    private bool affectsRotationX = true;
    private bool affectsRotationY = true;
    private bool affectsRotationZ = true;
    private Label actualBoneCountLabel;
    private Label proxyStatusLabel;
    private VisualElement affectsPositionContainer;
    private ScrollView proxyTargetScroll;
    private Button applyButton;

    private enum ConstraintKind
    {
        Rotation,
        Parent
    }

    private class PreviewEntry
    {
        public Transform Proxy;
        public readonly List<SourceWeight> Sources = new List<SourceWeight>();
        public string Message;
        public bool CanApply;
        public bool HasExistingConstraint;
    }

    private class SourceWeight
    {
        public Transform Actual;
        public float Distance;
        public float Weight;
    }

    [MenuItem("D9speed/Tools/ProxVRCphysboneSettings")]
    public static void ShowWindow()
    {
        var window = GetWindow<ProxVRCphysboneSettings>();
        window.titleContent = new GUIContent("ProxVRCphysboneSettings");
        window.minSize = new Vector2(760, 520);
        window.Show();
    }

    private void CreateGUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingTop = 8;
        root.style.paddingBottom = 8;
        D9speedEditorFontUtility.Apply(root);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        root.Add(scroll);

        scroll.Add(CreateTitle("ProxVRCphysboneSettings"));
        scroll.Add(new HelpBox(
            "VRCPhysBone で実際に揺れる Actual チェーンを1本指定し、複数の Proxy Target を最近傍の Actual ボーンへ VRC Constraint で同期します。",
            HelpBoxMessageType.Info));

        DrawActualChain(scroll);
        DrawOptions(scroll);
        DrawButtons(scroll);
        DrawProxyDropArea(scroll);
        DrawProxyTargets(scroll);
        RefreshDynamicUi();
    }

    private void DrawActualChain(VisualElement parent)
    {
        var box = CreateSection(parent, "Actual Chain");
        var actual_chain_root_field = CreateObjectField<Transform>("Actual Chain Root", actualChainRoot, true);
        actual_chain_root_field.RegisterValueChangedCallback(evt =>
        {
            actualChainRoot = evt.newValue as Transform;
            RefreshDynamicUi();
        });
        box.Add(actual_chain_root_field);

        var include_actual_root_toggle = new Toggle("Actual Chain Root 自身も候補に含める") { value = includeActualRoot };
        include_actual_root_toggle.RegisterValueChangedCallback(evt =>
        {
            includeActualRoot = evt.newValue;
            RefreshDynamicUi();
        });
        box.Add(include_actual_root_toggle);

        actualBoneCountLabel = new Label();
        box.Add(actualBoneCountLabel);
    }

    private void DrawOptions(VisualElement parent)
    {
        var box = CreateSection(parent, "Distance Weight");
        box.Add(CreateFloatField("Near Distance", nearDistance, value =>
        {
            nearDistance = Mathf.Max(0f, value);
            farDistance = Mathf.Max(nearDistance, farDistance);
            RefreshDynamicUi();
        }));
        box.Add(CreateFloatField("Far Distance", farDistance, value =>
        {
            farDistance = Mathf.Max(nearDistance, value);
            RefreshDynamicUi();
        }));
        box.Add(CreateSlider("Near Weight", nearWeight, 0f, 1f, value =>
        {
            nearWeight = value;
            RefreshDynamicUi();
        }));
        box.Add(CreateSlider("Far Weight", farWeight, 0f, 1f, value =>
        {
            farWeight = value;
            RefreshDynamicUi();
        }));
        box.Add(CreateIntSlider("Source Blend Count", sourceBlendCount, 1, 4, value =>
        {
            sourceBlendCount = value;
            RefreshDynamicUi();
        }));
        box.Add(CreateSlider("Source Falloff Power", sourceFalloffPower, 0.25f, 8f, value =>
        {
            sourceFalloffPower = value;
            RefreshDynamicUi();
        }));

        var constraint_kind_field = new EnumField("Constraint Type", constraintKind);
        constraint_kind_field.RegisterValueChangedCallback(evt =>
        {
            constraintKind = (ConstraintKind)evt.newValue;
            RefreshDynamicUi();
        });
        box.Add(constraint_kind_field);

        box.Add(CreateToggle("既存の Sources を置き換える", replaceExistingSources, value => replaceExistingSources = value));
        box.Add(CreateToggle("現在の見た目を維持する Offset を焼き込む", keepCurrentOffset, value => keepCurrentOffset = value));
        box.Add(CreateToggle("最後に Locked も有効化する (IsActive は常に True)", lockAfterSetup, value => lockAfterSetup = value));

        affectsPositionContainer = CreateAxisToggleGroup(
            "Affects Position",
            affectsPositionX,
            affectsPositionY,
            affectsPositionZ,
            value => affectsPositionX = value,
            value => affectsPositionY = value,
            value => affectsPositionZ = value);
        box.Add(affectsPositionContainer);

        box.Add(CreateAxisToggleGroup(
            "Affects Rotation",
            affectsRotationX,
            affectsRotationY,
            affectsRotationZ,
            value => affectsRotationX = value,
            value => affectsRotationY = value,
            value => affectsRotationZ = value));
    }

    private void DrawButtons(VisualElement parent)
    {
        var row = CreateRow();
        row.Add(CreateButton("Proxy Target を追加", () =>
        {
            proxyTargets.Add(null);
            RefreshDynamicUi();
        }));

        row.Add(CreateButton("選択中TransformをProxy Targetに追加", () =>
        {
            AddSelectionToProxyTargets();
            RefreshDynamicUi();
        }));

        applyButton = CreateButton($"{GetConstraintLabel()} を適用", () =>
        {
            ApplyWithDialog();
            RefreshDynamicUi();
        });
        row.Add(applyButton);
        parent.Add(row);
    }

    private void DrawProxyTargets(VisualElement parent)
    {
        proxyStatusLabel = new Label();
        proxyStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        proxyStatusLabel.style.marginTop = 6;
        parent.Add(proxyStatusLabel);

        proxyTargetScroll = new ScrollView(ScrollViewMode.Vertical);
        proxyTargetScroll.style.height = 320;
        parent.Add(proxyTargetScroll);
    }

    private void DrawProxyDropArea(VisualElement parent)
    {
        var dropArea = new Label("Hierarchy から Proxy Target を複数ドラッグ&ドロップ");
        dropArea.style.height = 46;
        dropArea.style.unityTextAlign = TextAnchor.MiddleCenter;
        dropArea.style.borderTopWidth = 1;
        dropArea.style.borderBottomWidth = 1;
        dropArea.style.borderLeftWidth = 1;
        dropArea.style.borderRightWidth = 1;
        dropArea.style.marginTop = 6;
        dropArea.style.marginBottom = 6;
        parent.Add(dropArea);

        dropArea.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            var transforms = GetDraggedTransforms();
            DragAndDrop.visualMode = transforms.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            evt.StopPropagation();
        });

        dropArea.RegisterCallback<DragPerformEvent>(evt =>
        {
            var transforms = GetDraggedTransforms();
            DragAndDrop.visualMode = transforms.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (transforms.Count > 0)
            {
                DragAndDrop.AcceptDrag();
                AddProxyTargets(transforms);
                RefreshDynamicUi();
            }
            evt.StopPropagation();
        });
    }

    private void RefreshDynamicUi()
    {
        if (actualBoneCountLabel != null)
        {
            var count = actualChainRoot == null ? 0 : GetActualBones().Count;
            actualBoneCountLabel.text = $"Actual Bone Candidates: {count}";
        }

        affectsPositionContainer?.SetEnabled(constraintKind == ConstraintKind.Parent);
        if (applyButton != null)
        {
            applyButton.text = $"{GetConstraintLabel()} を適用";
            applyButton.SetEnabled(GetApplicableCount() > 0);
        }

        RefreshProxyTargets();
    }

    private void RefreshProxyTargets()
    {
        if (proxyTargetScroll == null)
        {
            return;
        }

        var previews = BuildPreviewEntries();
        proxyStatusLabel.text = $"Proxy Targets: {proxyTargets.Count} / 適用可能: {GetApplicableCount()}";
        proxyTargetScroll.Clear();

        for (var i = 0; i < proxyTargets.Count; i++)
        {
            var preview = i < previews.Count ? previews[i] : null;
            proxyTargetScroll.Add(CreateProxyTargetElement(i, preview));
        }
    }

    private VisualElement CreateProxyTargetElement(int index, PreviewEntry preview)
    {
        var box = CreateBox();
        var row = CreateRow();
        row.Add(new Label($"Proxy Target {index + 1}") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
        row.Add(new Button(() =>
        {
            proxyTargets.RemoveAt(index);
            RefreshDynamicUi();
        }) { text = "削除" });
        box.Add(row);

        var proxy_field = CreateObjectField<Transform>("Proxy Target", proxyTargets[index], true);
        proxy_field.RegisterValueChangedCallback(evt =>
        {
            proxyTargets[index] = evt.newValue as Transform;
            RefreshDynamicUi();
        });
        box.Add(proxy_field);

        var message = preview != null ? preview.Message : "Proxy Target を指定してください。";
        var type = preview != null && preview.CanApply ? HelpBoxMessageType.None : HelpBoxMessageType.Warning;
        box.Add(new HelpBox(message, type));
        return box;
    }

    private static Label CreateTitle(string text)
    {
        return new Label(text)
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginBottom = 4
            }
        };
    }

    private static VisualElement CreateSection(VisualElement parent, string title)
    {
        parent.Add(new Label(title)
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginTop = 8,
                marginBottom = 3
            }
        });

        var box = CreateBox();
        parent.Add(box);
        return box;
    }

    private static VisualElement CreateBox()
    {
        return new VisualElement
        {
            style =
            {
                borderTopWidth = 1,
                borderBottomWidth = 1,
                borderLeftWidth = 1,
                borderRightWidth = 1,
                paddingLeft = 6,
                paddingRight = 6,
                paddingTop = 6,
                paddingBottom = 6,
                marginBottom = 6
            }
        };
    }

    private static VisualElement CreateRow()
    {
        return new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                marginTop = 2,
                marginBottom = 2
            }
        };
    }

    private static Button CreateButton(string text, Action on_click)
    {
        return new Button(on_click)
        {
            text = text,
            style =
            {
                flexGrow = 1,
                marginLeft = 2,
                marginRight = 2,
                height = 28
            }
        };
    }

    private static ObjectField CreateObjectField<T>(string label, UnityEngine.Object value, bool allow_scene_objects)
        where T : UnityEngine.Object
    {
        return new ObjectField(label)
        {
            objectType = typeof(T),
            allowSceneObjects = allow_scene_objects,
            value = value
        };
    }

    private static FloatField CreateFloatField(string label, float value, Action<float> on_change)
    {
        var field = new FloatField(label) { value = value };
        field.RegisterValueChangedCallback(evt => on_change(evt.newValue));
        return field;
    }

    private static Slider CreateSlider(string label, float value, float low_value, float high_value, Action<float> on_change)
    {
        var slider = new Slider(label, low_value, high_value)
        {
            value = value,
            showInputField = true
        };
        slider.RegisterValueChangedCallback(evt => on_change(evt.newValue));
        return slider;
    }

    private static SliderInt CreateIntSlider(string label, int value, int low_value, int high_value, Action<int> on_change)
    {
        var slider = new SliderInt(label, low_value, high_value)
        {
            value = value,
            showInputField = true
        };
        slider.RegisterValueChangedCallback(evt => on_change(evt.newValue));
        return slider;
    }

    private static Toggle CreateToggle(string label, bool value, Action<bool> on_change)
    {
        var toggle = new Toggle(label) { value = value };
        toggle.RegisterValueChangedCallback(evt => on_change(evt.newValue));
        return toggle;
    }

    private static VisualElement CreateAxisToggleGroup(
        string title,
        bool x_value,
        bool y_value,
        bool z_value,
        Action<bool> on_x_change,
        Action<bool> on_y_change,
        Action<bool> on_z_change)
    {
        var container = new VisualElement { style = { marginTop = 4 } };
        container.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        var row = CreateRow();
        row.Add(CreateAxisToggle("X", x_value, on_x_change));
        row.Add(CreateAxisToggle("Y", y_value, on_y_change));
        row.Add(CreateAxisToggle("Z", z_value, on_z_change));
        container.Add(row);
        return container;
    }

    private static Toggle CreateAxisToggle(string label, bool value, Action<bool> on_change)
    {
        var toggle = CreateToggle(label, value, on_change);
        toggle.style.width = 48;
        return toggle;
    }

    private List<PreviewEntry> BuildPreviewEntries()
    {
        var actualBones = GetActualBones();
        var entries = new List<PreviewEntry>();
        foreach (var proxy in proxyTargets)
        {
            entries.Add(BuildPreviewEntry(proxy, actualBones));
        }

        return entries;
    }

    private PreviewEntry BuildPreviewEntry(Transform proxy, List<Transform> actualBones)
    {
        var entry = new PreviewEntry { Proxy = proxy };
        if (actualChainRoot == null)
        {
            entry.Message = "Actual Chain Root が未指定です。";
            return entry;
        }

        if (actualBones.Count == 0)
        {
            entry.Message = "Actual チェーン内に候補ボーンがありません。";
            return entry;
        }

        if (proxy == null)
        {
            entry.Message = "Proxy Target が未指定です。";
            return entry;
        }

        entry.Sources.AddRange(BuildSourceWeights(proxy, actualBones));
        if (entry.Sources.Count == 0)
        {
            entry.Message = "Sourceにできる Actual ボーンが見つかりません。";
            return entry;
        }

        entry.HasExistingConstraint = HasExistingConstraint(proxy);
        entry.CanApply = true;

        var existing = entry.HasExistingConstraint ? "既存あり" : "新規追加";
        entry.Message = $"Sources: {FormatSourceWeights(entry.Sources)} / {existing}";
        return entry;
    }

    private List<Transform> GetActualBones()
    {
        var bones = new List<Transform>();
        if (actualChainRoot == null)
        {
            return bones;
        }

        if (includeActualRoot)
        {
            bones.Add(actualChainRoot);
        }

        foreach (Transform child in actualChainRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child != actualChainRoot)
            {
                bones.Add(child);
            }
        }

        return bones;
    }

    private List<SourceWeight> BuildSourceWeights(Transform proxy, List<Transform> actualBones)
    {
        var candidates = new List<SourceWeight>();

        foreach (var actual in actualBones)
        {
            if (actual == null || actual == proxy)
            {
                continue;
            }

            var distance = Vector3.Distance(actual.position, proxy.position);
            var baseWeight = CalculateWeight(distance);
            if (baseWeight <= 0f)
            {
                continue;
            }

            candidates.Add(new SourceWeight
            {
                Actual = actual,
                Distance = distance,
                Weight = Mathf.Pow(baseWeight, sourceFalloffPower)
            });
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (candidates.Count > sourceBlendCount)
        {
            candidates.RemoveRange(sourceBlendCount, candidates.Count - sourceBlendCount);
        }

        NormalizeSourceWeights(candidates);
        return candidates;
    }

    private static void NormalizeSourceWeights(List<SourceWeight> sources)
    {
        var total = 0f;
        foreach (var source in sources)
        {
            total += source.Weight;
        }

        if (total <= 0f)
        {
            var equalWeight = sources.Count > 0 ? 1f / sources.Count : 0f;
            foreach (var source in sources)
            {
                source.Weight = equalWeight;
            }
            return;
        }

        foreach (var source in sources)
        {
            source.Weight = Mathf.Clamp01(source.Weight / total);
        }
    }

    private static string FormatSourceWeights(List<SourceWeight> sources)
    {
        var parts = new List<string>();
        foreach (var source in sources)
        {
            parts.Add($"{source.Actual.name}:{source.Weight:F3}({source.Distance:F4})");
        }

        return string.Join(", ", parts);
    }

    private void AddSelectionToProxyTargets()
    {
        AddProxyTargets(Selection.GetTransforms(SelectionMode.Unfiltered));
    }

    private void AddProxyTargets(IEnumerable<Transform> transforms)
    {
        foreach (var selected in transforms)
        {
            if (selected != null && !proxyTargets.Contains(selected))
            {
                proxyTargets.Add(selected);
            }
        }
    }

    private static List<Transform> GetDraggedTransforms()
    {
        var transforms = new List<Transform>();
        foreach (var draggedObject in DragAndDrop.objectReferences)
        {
            var transform = GetTransformFromObject(draggedObject);
            if (transform != null && !transforms.Contains(transform))
            {
                transforms.Add(transform);
            }
        }

        return transforms;
    }

    private static Transform GetTransformFromObject(UnityEngine.Object source)
    {
        if (source is Transform transform)
        {
            return transform;
        }

        if (source is GameObject gameObject)
        {
            return gameObject.transform;
        }

        return null;
    }

    private int GetApplicableCount()
    {
        var count = 0;
        foreach (var entry in BuildPreviewEntries())
        {
            if (entry.CanApply)
            {
                count++;
            }
        }
        return count;
    }

    private float CalculateWeight(float distance)
    {
        if (Mathf.Approximately(nearDistance, farDistance))
        {
            return Mathf.Clamp01(nearWeight);
        }

        var t = Mathf.InverseLerp(nearDistance, farDistance, distance);
        return Mathf.Clamp01(Mathf.Lerp(nearWeight, farWeight, t));
    }

    private void ApplyWithDialog()
    {
        var count = GetApplicableCount();
        if (count == 0)
        {
            EditorUtility.DisplayDialog("ProxVRCphysboneSettings", "適用可能な Proxy Target がありません。", "OK");
            return;
        }

        var message = $"{count} 件の Proxy Target に {GetConstraintLabel()} を追加/更新します。\nUndo で戻せます。";
        if (!EditorUtility.DisplayDialog($"{GetConstraintLabel()} を適用", message, "適用", "キャンセル"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName($"Setup Prox VRC PhysBone {GetConstraintLabel()}");

        foreach (var entry in BuildPreviewEntries())
        {
            if (!entry.CanApply)
            {
                continue;
            }

            SetupConstraint(entry);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[ProxVRCphysboneSettings] {count} 件の {GetConstraintLabel()} を適用しました。");
    }

    private void SetupConstraint(PreviewEntry entry)
    {
        if (constraintKind == ConstraintKind.Parent)
        {
            SetupParentConstraint(entry);
        }
        else
        {
            SetupRotationConstraint(entry);
        }
    }

    private void SetupRotationConstraint(PreviewEntry entry)
    {
        var proxy = entry.Proxy;
        var originalLocalPosition = proxy.localPosition;
        var originalLocalRotation = proxy.localRotation;
        var originalLocalScale = proxy.localScale;

        Undo.RecordObject(proxy, "Keep Proxy Transform");

        var constraint = proxy.GetComponent<VRCRotationConstraint>();
        if (constraint == null)
        {
            constraint = Undo.AddComponent<VRCRotationConstraint>(proxy.gameObject);
        }
        else
        {
            Undo.RecordObject(constraint, "Update VRC Rotation Constraint");
        }

        constraint.IsActive = false;
        constraint.Locked = false;

        if (replaceExistingSources)
        {
            constraint.Sources.Clear();
        }

        foreach (var source in entry.Sources)
        {
            constraint.Sources.Add(new VRCConstraintSource(source.Actual, source.Weight));
        }
        constraint.AffectsRotationX = affectsRotationX;
        constraint.AffectsRotationY = affectsRotationY;
        constraint.AffectsRotationZ = affectsRotationZ;
        constraint.RotationAtRest = proxy.localEulerAngles;

        if (keepCurrentOffset)
        {
            TryRebakeOffsets(constraint);
        }

        proxy.localPosition = originalLocalPosition;
        proxy.localRotation = originalLocalRotation;
        proxy.localScale = originalLocalScale;

        if (lockAfterSetup)
        {
            constraint.Locked = true;
        }

        constraint.IsActive = true;

        EditorUtility.SetDirty(constraint);
        EditorUtility.SetDirty(proxy);
        EditorUtility.SetDirty(proxy.gameObject);
    }

    private void SetupParentConstraint(PreviewEntry entry)
    {
        var proxy = entry.Proxy;
        var originalLocalPosition = proxy.localPosition;
        var originalLocalRotation = proxy.localRotation;
        var originalLocalScale = proxy.localScale;

        Undo.RecordObject(proxy, "Keep Proxy Transform");

        var constraint = proxy.GetComponent<VRCParentConstraint>();
        if (constraint == null)
        {
            constraint = Undo.AddComponent<VRCParentConstraint>(proxy.gameObject);
        }
        else
        {
            Undo.RecordObject(constraint, "Update VRC Parent Constraint");
        }

        constraint.IsActive = false;
        constraint.Locked = false;

        if (replaceExistingSources)
        {
            constraint.Sources.Clear();
        }

        foreach (var source in entry.Sources)
        {
            constraint.Sources.Add(new VRCConstraintSource(source.Actual, source.Weight));
        }

        constraint.AffectsPositionX = affectsPositionX;
        constraint.AffectsPositionY = affectsPositionY;
        constraint.AffectsPositionZ = affectsPositionZ;
        constraint.AffectsRotationX = affectsRotationX;
        constraint.AffectsRotationY = affectsRotationY;
        constraint.AffectsRotationZ = affectsRotationZ;
        constraint.PositionAtRest = proxy.localPosition;
        constraint.RotationAtRest = proxy.localEulerAngles;

        if (keepCurrentOffset)
        {
            TryRebakeOffsets(constraint);
        }

        proxy.localPosition = originalLocalPosition;
        proxy.localRotation = originalLocalRotation;
        proxy.localScale = originalLocalScale;

        if (lockAfterSetup)
        {
            constraint.Locked = true;
        }

        constraint.IsActive = true;

        EditorUtility.SetDirty(constraint);
        EditorUtility.SetDirty(proxy);
        EditorUtility.SetDirty(proxy.gameObject);
    }

    private bool HasExistingConstraint(Transform proxy)
    {
        if (constraintKind == ConstraintKind.Parent)
        {
            return proxy.GetComponent<VRCParentConstraint>() != null;
        }

        return proxy.GetComponent<VRCRotationConstraint>() != null;
    }

    private string GetConstraintLabel()
    {
        return constraintKind == ConstraintKind.Parent ? "VRCParentConstraint" : "VRCRotationConstraint";
    }

    private static void TryRebakeOffsets(Component constraint)
    {
        var method = constraint.GetType().GetMethod("RebakeOffsets", Type.EmptyTypes);
        if (method != null)
        {
            method.Invoke(constraint, null);
        }
    }
}
