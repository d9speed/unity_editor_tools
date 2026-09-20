using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using D9speed_BaseEditorUtils;

// Newtonsoft Jsonはpackage.jsonでUnity公式パッケージへの依存を宣言します。



namespace D9speedBaseEditorUtil
{


public class PackageExporter : EditorWindow
{
    private const string LastProfilePathEditorPrefsKey = "D9speed.PackageExporter.LastProfilePath";
    private const string root_class = "package-exporter";
    private const string button_base_class = "package-exporter__button";
    private const string button_load_class = "package-exporter__button--load";
    private const string button_save_class = "package-exporter__button--save";
    private const string button_select_class = "package-exporter__button--select";
    private const string button_add_class = "package-exporter__button--add";
    private const string button_export_class = "package-exporter__button--export";
    private const string button_danger_class = "package-exporter__button--danger";
    private const int max_additional_name_strings = 5;
    private const int date_format_part_token = -1;
    private const int subfolder_name_part_token = -2;

    // シリアライズ可能にして、EditorWindow上で編集可能にする
    [SerializeField] private string exportPath = "";
    [SerializeField] private string outPath = "";
    [SerializeField] private string packageName = "";
    [SerializeField] private string dateFormat = "yyyy-MM-dd";
    [SerializeField] private List<string> additionalNameStrings = new List<string>();
    [SerializeField] private int dateFormatPosition = 0;
    [SerializeField] private int subfolderNamePosition = -1;
    [SerializeField] private string te64Path = "";
    [SerializeField] private bool openWithTE = false;
    [SerializeField] private bool overrideExclusionForSubfolders = false;
    [SerializeField] private Dictionary<string, bool> subFolderSelections = new Dictionary<string, bool>();
    [SerializeField] private bool excludeDirectoriesWithHyphen = true;
    [SerializeField] private bool includeMetaFiles = true;
    [SerializeField] private bool includeNestedPrefabs = true;
    [SerializeField] private bool includeOtherComponentsInLog = false;
    [SerializeField] private bool pendingNestedPrefabSelection = false;
    [SerializeField] private List<string> detectedNestedPrefabPaths = new List<string>();
    [SerializeField] private List<string> detectedExcludedImageWarnings = new List<string>();
    [SerializeField] private string currentProfilePath = "";
    private bool exportPrecheckScanScheduled = false;
    private Label currentProfilePathLabel;
    private Label packagePreviewLabel;
    private HelpBox packagePreviewWarningBox;
    private Label packagePreviewHintLabel;
    private VisualElement packageNamePartsContainer;
    private Button addPackageNameStringButton;
    private TextField subfolderNamePartField;
    private VisualElement subfolderContainer;
    private VisualElement exclusionKeywordsContainer;
    private VisualElement precheckContainer;
    private Button exportButton;
    private Button cancelNestedPrefabSelectionButton;

    [Serializable]
    private class ExportProfileJson
    {
        public string exportPath = "";
        public string outPath = "";
        public string packageName = "";
        public string dateFormat = "yyyy-MM-dd";
        public List<string> additionalNameStrings = new List<string>();
        public int dateFormatPosition = 0;
        public int subfolderNamePosition = -1;
        public string te64Path = "";
        public bool openWithTE = false;
        public bool overrideExclusionForSubfolders = false;
        public bool excludeDirectoriesWithHyphen = true;
        public bool includeMetaFiles = true;
        public bool includeNestedPrefabs = true;
        public bool includeOtherComponentsInLog = false;
        public List<string> exclusionKeywords = new List<string>();
        public List<SubFolderSelectionJson> subFolderSelections = new List<SubFolderSelectionJson>();
    }

    [Serializable]
    private class SubFolderSelectionJson
    {
        public string folderPath = "";
        public bool isSelected = true;
    }

    [Serializable]
    private class PackageComponentReportJson
    {
        public int schema_version = 2;
        public string generated_at = "";
        public string package_path = "";
        public string package_file = "";
        public string report_target = "";
        public bool include_meta_files = true;
        public bool include_other_components = false;
        public int asset_count = 0;
        public int prefab_count = 0;
        public int target_component_count = 0;
        public string summary = "";
        public List<string> asset_paths = new List<string>();
        public List<ComponentSummaryJson> component_summary = new List<ComponentSummaryJson>();
        public List<ComponentInstanceJson> component_instances = new List<ComponentInstanceJson>();
        public List<ComponentDetailJson> component_details = new List<ComponentDetailJson>();
        public ReferenceAuditJson reference_audit = new ReferenceAuditJson();
        public List<PrefabComponentReportJson> prefabs = new List<PrefabComponentReportJson>();
    }

    [Serializable]
    private class PrefabComponentReportJson
    {
        public string prefab_path = "";
        public string prefab_name = "";
        public bool has_target_components = false;
        public int target_component_count = 0;
        public List<ComponentSummaryJson> component_summary = new List<ComponentSummaryJson>();
        public List<ComponentDetailJson> component_details = new List<ComponentDetailJson>();
        public List<ReferenceAuditItemJson> reference_audit_items = new List<ReferenceAuditItemJson>();
        public List<string> blendshapes = new List<string>();
        public List<SkinnedMeshBoundsJson> skinned_mesh_renderer_bounds = new List<SkinnedMeshBoundsJson>();
        public List<GameObjectComponentReportJson> game_objects = new List<GameObjectComponentReportJson>();
    }

    [Serializable]
    private class GameObjectComponentReportJson
    {
        public string name = "";
        public string object_path = "";
        public string parent_path = "";
        public int depth = 0;
        public int sibling_index = 0;
        public int missing_component_count = 0;
        public List<ComponentInstanceJson> components = new List<ComponentInstanceJson>();
    }

    [Serializable]
    private class ComponentInstanceJson
    {
        public string prefab_path = "";
        public string object_path = "";
        public string parent_path = "";
        public int depth = 0;
        public int component_index = 0;
        public string type_name = "";
        public string type_full_name = "";
        public string type_namespace = "";
        public string assembly_name = "";
        public string category = "";
    }

    [Serializable]
    private class ComponentDetailJson
    {
        public string prefab_path = "";
        public string object_path = "";
        public int component_index = 0;
        public string type_name = "";
        public string type_full_name = "";
        public Dictionary<string, object> details = new Dictionary<string, object>();
    }

    [Serializable]
    private class ReferenceAuditJson
    {
        public int external_reference_count = 0;
        public int ignored_reference_count = 0;
        public int needs_check_count = 0;
        public int missing_reference_count = 0;
        public List<ReferenceAuditItemJson> items = new List<ReferenceAuditItemJson>();
    }

    [Serializable]
    private class ReferenceAuditItemJson
    {
        public string source_asset = "";
        public string source_object_path = "";
        public int component_index = 0;
        public string component_type = "";
        public string property_path = "";
        public string referenced_asset = "";
        public string referenced_type = "";
        public string referenced_object_name = "";
        public string status = "";
    }

    [Serializable]
    private class ComponentSummaryJson
    {
        public string type_name = "";
        public string type_full_name = "";
        public string category = "";
        public int count = 0;
    }

    private class ComponentSummaryTextRow
    {
        public string category = "";
        public string label = "";
        public int count = 0;
    }

    [Serializable]
    private class SkinnedMeshBoundsJson
    {
        public string object_path = "";
        public BoundsJson local_bounds = new BoundsJson();
        public BoundsJson world_bounds = new BoundsJson();
    }

    [Serializable]
    private class BoundsJson
    {
        public Vector3Json center = new Vector3Json();
        public Vector3Json size = new Vector3Json();
        public Vector3Json extents = new Vector3Json();
    }

    [Serializable]
    private class Vector3Json
    {
        public float x = 0f;
        public float y = 0f;
        public float z = 0f;
    }

    [MenuItem("D9speed/ExportBatch/ExportBatch", false, 100)]
    public static void ShowWindow()
    {
        GetWindow<PackageExporter>("PackageExporter");
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        LoadWindowStateFromSettings();
        currentProfilePath = EditorPrefs.GetString(LastProfilePathEditorPrefsKey, "");
        ResetNestedPrefabSelectionState();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
    }

    private void CreateGUI()
    {
        BuildUI();
        RefreshUI();
    }

    private void LoadWindowStateFromSettings()
    {
        var settings = PackageExporterSettings.instance;
        exportPath = settings.exportPath;
        outPath = settings.outPath;
        packageName = settings.packageName;
        dateFormat = settings.dateFormat;
        additionalNameStrings = new List<string>(settings.additionalNameStrings ?? new List<string>());
        dateFormatPosition = settings.dateFormatPosition;
        subfolderNamePosition = settings.subfolderNamePosition;
        NormalizePackageNameParts();
        te64Path = settings.te64Path;
        openWithTE = settings.openWithTE;
        overrideExclusionForSubfolders = settings.overrideExclusionForSubfolders;
        excludeDirectoriesWithHyphen = settings.excludeDirectoriesWithHyphen;
        includeMetaFiles = settings.includeMetaFiles;
        includeNestedPrefabs = settings.includeNestedPrefabs;
        includeOtherComponentsInLog = settings.includeOtherComponentsInLog;
    }

    private void SaveWindowStateToSettings(string undoName, bool recordUndo = true)
    {
        var settings = PackageExporterSettings.instance;
        if (recordUndo)
        {
            Undo.RecordObject(settings, undoName);
        }
        settings.exportPath = exportPath;
        settings.packageName = packageName;
        settings.outPath = outPath;
        settings.dateFormat = dateFormat;
        settings.additionalNameStrings = new List<string>(additionalNameStrings);
        settings.dateFormatPosition = dateFormatPosition;
        settings.subfolderNamePosition = subfolderNamePosition;
        settings.te64Path = te64Path;
        settings.openWithTE = openWithTE;
        settings.overrideExclusionForSubfolders = overrideExclusionForSubfolders;
        settings.excludeDirectoriesWithHyphen = excludeDirectoriesWithHyphen;
        settings.includeMetaFiles = includeMetaFiles;
        settings.includeNestedPrefabs = includeNestedPrefabs;
        settings.includeOtherComponentsInLog = includeOtherComponentsInLog;
        EditorUtility.SetDirty(settings);
        settings.SaveSettings(true);
    }

    private void BuildUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingTop = 8;
        root.style.paddingBottom = 8;
        root.AddToClassList(root_class);
        AddStyleSheet(root);
        D9speedEditorFontUtility.Apply(root);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        root.Add(scroll);

        scroll.Add(CreateTitle("Export Package Settings"));
        BuildProfileControls(scroll);
        BuildMainSettings(scroll);
        BuildSubfolderControls(scroll);
        BuildExclusionSettings(scroll);
        BuildNestedPrefabSelectionUI(scroll);
        BuildExportControls(scroll);
    }

    private void BuildProfileControls(VisualElement parent)
    {
        var box = CreateSection(parent, "エクスポートプロファイル");
        var row = CreateRow();
        row.Add(CreateButton("エクスポートプロファイルを読み込み", () =>
        {
            LoadExportProfileFromJson();
            RebuildUI();
        }, button_load_class));
        row.Add(CreateButton("現在のエクスポート設定内容をJsonに書き出し", () =>
        {
            SaveCurrentSettingsAsProfileJson();
            RefreshUI();
        }, button_save_class));
        box.Add(row);

        currentProfilePathLabel = new Label();
        currentProfilePathLabel.style.whiteSpace = WhiteSpace.Normal;
        box.Add(currentProfilePathLabel);
    }

    private void BuildMainSettings(VisualElement parent)
    {
        var box = CreateSection(parent, "Settings");
        box.Add(CreateTextField("Export Path", exportPath, value =>
        {
            exportPath = value;
            OnMainSettingChanged();
        }));
        box.Add(CreateTextField("Package Name", packageName, value =>
        {
            packageName = value;
            OnMainSettingChanged();
        }));

        box.Add(new Label("ファイル名の追加部分（Date Format＋Subfolder Name＋任意文字列 最大5個）")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginTop = 4
            }
        });
        packageNamePartsContainer = new VisualElement();
        box.Add(packageNamePartsContainer);
        addPackageNameStringButton = CreateButton("文字列を追加", AddPackageNameString, button_add_class);
        box.Add(addPackageNameStringButton);
        RefreshPackageNamePartsUI();

        box.Add(CreateTextField("TE64.exe Path", te64Path, value =>
        {
            te64Path = value;
            OnMainSettingChanged();
        }));
        box.Add(CreateToggle("TEで開く", openWithTE, value =>
        {
            openWithTE = value;
            OnMainSettingChanged();
        }));
        box.Add(CreateToggle("サブフォルダに除外キーワードを含むファイル(テクスチャとかマテリアル)があったときにオーバーライド", overrideExclusionForSubfolders, value =>
        {
            overrideExclusionForSubfolders = value;
            OnMainSettingChanged();
        }));
        box.Add(CreateTextField("Unityパッケージの出力フォルダ", outPath, value =>
        {
            outPath = value;
            OnMainSettingChanged();
        }));

        var output_button = CreateButton("Select Output Folder", () =>
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Folder", Directory.Exists(outPath) ? outPath : Application.dataPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                outPath = selectedPath;
                SaveWindowStateToSettings("PackageExporter Output Folder Changed");
                RebuildUI();
            }
        }, button_select_class);
        box.Add(output_button);

        packagePreviewWarningBox = new HelpBox("Date Format が無効です。例: yyyy-MM-dd", HelpBoxMessageType.Warning);
        packagePreviewLabel = new Label { style = { whiteSpace = WhiteSpace.Normal } };
        packagePreviewHintLabel = new Label("サブフォルダが複数の場合は \"***\" でプレビューします。");
        packagePreviewHintLabel.style.fontSize = 11;
        box.Add(new Label("Package Preview") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
        box.Add(packagePreviewWarningBox);
        box.Add(packagePreviewLabel);
        box.Add(packagePreviewHintLabel);
    }

    private void BuildSubfolderControls(VisualElement parent)
    {
        var box = CreateSection(parent, "エクスポートするサブフォルダ");
        box.Add(CreateButton("サブフォルダを取得", () =>
        {
            GetSubfolders();
            ScheduleExportPrecheckScan();
            RefreshUI();
        }, button_load_class));

        var row = CreateRow();
        row.Add(CreateButton("全部選択", () =>
        {
            SetAllSubfolderSelections(true);
            ScheduleExportPrecheckScan();
            RefreshUI();
        }, button_select_class));
        row.Add(CreateButton("選択解除", () =>
        {
            SetAllSubfolderSelections(false);
            ScheduleExportPrecheckScan();
            RefreshUI();
        }, button_select_class));
        box.Add(row);

        subfolderContainer = new VisualElement();
        box.Add(subfolderContainer);
    }

    private void BuildExclusionSettings(VisualElement parent)
    {
        var box = CreateSection(parent, "Exclusion Settings");
        exclusionKeywordsContainer = new VisualElement();
        box.Add(exclusionKeywordsContainer);
        box.Add(CreateButton("除外キーワードを追加", () =>
        {
            Undo.RecordObject(PackageExporterSettings.instance, "PackageExporter Exclusion Keyword Added");
            PackageExporterSettings.instance.exclusionKeywords.Add(string.Empty);
            SaveExclusionSettingsChanged();
            RefreshUI();
        }, button_add_class));

        box.Add(CreateToggle("除外: 名前に '-' を含むフォルダ", excludeDirectoriesWithHyphen, value =>
        {
            excludeDirectoriesWithHyphen = value;
            SaveExclusionSettingsChanged();
        }));
        box.Add(CreateToggle("除外: metaファイルをログに記録するかどうか", includeMetaFiles, value =>
        {
            includeMetaFiles = value;
            SaveExclusionSettingsChanged();
        }));
        box.Add(CreateToggle("その他のコンポーネントも記録する", includeOtherComponentsInLog, value =>
        {
            includeOtherComponentsInLog = value;
            SaveExclusionSettingsChanged();
        }));
    }

    private void BuildNestedPrefabSelectionUI(VisualElement parent)
    {
        precheckContainer = new VisualElement();
        parent.Add(precheckContainer);
    }

    private void BuildExportControls(VisualElement parent)
    {
        var box = CreateSection(parent, "Export");
        exportButton = CreateButton("Start Batch Export", () =>
        {
            StartBatchExport();
            RefreshUI();
        }, button_export_class);
        box.Add(exportButton);

        cancelNestedPrefabSelectionButton = CreateButton("ネストPrefab選択をキャンセル", () =>
        {
            ResetNestedPrefabSelectionState();
            RefreshUI();
        }, button_danger_class);
        box.Add(cancelNestedPrefabSelectionButton);
    }

    private void RebuildUI()
    {
        BuildUI();
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (currentProfilePathLabel != null)
        {
            currentProfilePathLabel.text = string.IsNullOrWhiteSpace(currentProfilePath)
                ? "現在のプロファイル: 未設定"
                : "現在のプロファイル: " + currentProfilePath;
        }

        RefreshPackageNamePreview();
        subfolderNamePartField?.SetValueWithoutNotify(GetSubfolderPreviewName());
        RefreshSubfolderCheckboxes();
        RefreshExclusionKeywordFields();
        RefreshNestedPrefabSelectionUI();

        if (exportButton != null)
        {
            exportButton.text = pendingNestedPrefabSelection
                ? "選択内容で Start Batch Export"
                : "Start Batch Export";
        }

        cancelNestedPrefabSelectionButton?.SetEnabled(pendingNestedPrefabSelection);
        if (cancelNestedPrefabSelectionButton != null)
        {
            cancelNestedPrefabSelectionButton.style.display = pendingNestedPrefabSelection ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void OnMainSettingChanged()
    {
        ResetNestedPrefabSelectionState();
        SaveWindowStateToSettings("PackageExporter Settings Changed");
        RefreshUI();
    }

    private void OnUndoRedoPerformed()
    {
        LoadWindowStateFromSettings();
        ResetNestedPrefabSelectionState();
        RebuildUI();
    }

    private void RefreshPackageNamePartsUI()
    {
        if (packageNamePartsContainer == null)
        {
            return;
        }

        NormalizePackageNameParts();
        subfolderNamePartField = null;
        packageNamePartsContainer.Clear();
        List<int> partOrder = BuildPackageNamePartOrder();
        for (int sequenceIndex = 0; sequenceIndex < partOrder.Count; sequenceIndex++)
        {
            int partToken = partOrder[sequenceIndex];
            if (partToken == date_format_part_token)
            {
                packageNamePartsContainer.Add(CreatePackageNamePartRow(
                    "Date Format",
                    dateFormat,
                    value =>
                    {
                        dateFormat = value;
                        OnMainSettingChanged();
                    },
                    sequenceIndex,
                    partOrder.Count,
                    null));
                continue;
            }

            if (partToken == subfolder_name_part_token)
            {
                VisualElement subfolderRow = CreatePackageNamePartRow(
                    "Subfolder Name（自動）",
                    GetSubfolderPreviewName(),
                    null,
                    sequenceIndex,
                    partOrder.Count,
                    null);
                subfolderNamePartField = subfolderRow.Q<TextField>();
                packageNamePartsContainer.Add(subfolderRow);
                continue;
            }

            int capturedCustomIndex = partToken;
            packageNamePartsContainer.Add(CreatePackageNamePartRow(
                $"文字列 {capturedCustomIndex + 1}",
                additionalNameStrings[capturedCustomIndex],
                value =>
                {
                    additionalNameStrings[capturedCustomIndex] = value;
                    OnMainSettingChanged();
                },
                sequenceIndex,
                partOrder.Count,
                () => RemovePackageNameString(capturedCustomIndex)));
        }

        addPackageNameStringButton?.SetEnabled(additionalNameStrings.Count < max_additional_name_strings);
    }

    private VisualElement CreatePackageNamePartRow(
        string label,
        string value,
        Action<string> onChange,
        int sequenceIndex,
        int totalPartCount,
        Action onRemove)
    {
        var row = CreateRow();
        var field = new TextField(label) { value = value ?? string.Empty };
        if (onChange == null)
        {
            field.SetEnabled(false);
        }
        else
        {
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue ?? string.Empty));
        }
        field.style.flexGrow = 1;
        row.Add(field);

        var moveUpButton = CreateButton("↑", () => MovePackageNamePart(sequenceIndex, -1));
        moveUpButton.style.flexGrow = 0;
        moveUpButton.style.width = 32;
        moveUpButton.SetEnabled(sequenceIndex > 0);
        row.Add(moveUpButton);

        var moveDownButton = CreateButton("↓", () => MovePackageNamePart(sequenceIndex, 1));
        moveDownButton.style.flexGrow = 0;
        moveDownButton.style.width = 32;
        moveDownButton.SetEnabled(sequenceIndex < totalPartCount - 1);
        row.Add(moveDownButton);

        if (onRemove != null)
        {
            var removeButton = CreateButton("削除", onRemove, button_danger_class);
            removeButton.style.flexGrow = 0;
            removeButton.style.width = 52;
            row.Add(removeButton);
        }

        return row;
    }

    private void AddPackageNameString()
    {
        if (additionalNameStrings.Count >= max_additional_name_strings)
        {
            return;
        }

        List<int> partOrder = BuildPackageNamePartOrder();
        int newCustomIndex = additionalNameStrings.Count;
        additionalNameStrings.Add(string.Empty);
        int insertIndex = partOrder.IndexOf(subfolder_name_part_token);
        partOrder.Insert(insertIndex >= 0 ? insertIndex : partOrder.Count, newCustomIndex);
        ApplyPackageNamePartOrder(partOrder);
        SaveWindowStateToSettings("PackageExporter Name String Added");
        RefreshPackageNamePartsUI();
        RefreshPackageNamePreview();
    }

    private void RemovePackageNameString(int customIndex)
    {
        if (customIndex < 0 || customIndex >= additionalNameStrings.Count)
        {
            return;
        }

        List<int> partOrder = BuildPackageNamePartOrder();
        partOrder.Remove(customIndex);
        ApplyPackageNamePartOrder(partOrder);
        SaveWindowStateToSettings("PackageExporter Name String Removed");
        RefreshPackageNamePartsUI();
        RefreshPackageNamePreview();
    }

    private void MovePackageNamePart(int sequenceIndex, int direction)
    {
        int targetIndex = sequenceIndex + direction;
        List<int> partOrder = BuildPackageNamePartOrder();
        if (sequenceIndex < 0 || sequenceIndex >= partOrder.Count || targetIndex < 0 || targetIndex >= partOrder.Count)
        {
            return;
        }

        (partOrder[sequenceIndex], partOrder[targetIndex]) = (partOrder[targetIndex], partOrder[sequenceIndex]);
        ApplyPackageNamePartOrder(partOrder);
        SaveWindowStateToSettings("PackageExporter Name Part Moved");
        RefreshPackageNamePartsUI();
        RefreshPackageNamePreview();
    }

    private List<int> BuildPackageNamePartOrder()
    {
        NormalizePackageNameParts();
        var partOrder = new List<int>();
        int customIndex = 0;
        int totalPartCount = additionalNameStrings.Count + 2;
        for (int sequenceIndex = 0; sequenceIndex < totalPartCount; sequenceIndex++)
        {
            if (sequenceIndex == dateFormatPosition)
            {
                partOrder.Add(date_format_part_token);
            }
            else if (sequenceIndex == subfolderNamePosition)
            {
                partOrder.Add(subfolder_name_part_token);
            }
            else
            {
                partOrder.Add(customIndex++);
            }
        }
        return partOrder;
    }

    private void ApplyPackageNamePartOrder(List<int> partOrder)
    {
        List<string> sourceStrings = additionalNameStrings;
        additionalNameStrings = partOrder
            .Where(token => token >= 0)
            .Select(token => sourceStrings[token])
            .ToList();
        dateFormatPosition = partOrder.IndexOf(date_format_part_token);
        subfolderNamePosition = partOrder.IndexOf(subfolder_name_part_token);
        NormalizePackageNameParts();
    }

    private void NormalizePackageNameParts()
    {
        additionalNameStrings ??= new List<string>();
        if (additionalNameStrings.Count > max_additional_name_strings)
        {
            additionalNameStrings.RemoveRange(max_additional_name_strings, additionalNameStrings.Count - max_additional_name_strings);
        }
        int totalPartCount = additionalNameStrings.Count + 2;
        dateFormatPosition = Mathf.Clamp(dateFormatPosition, 0, totalPartCount - 1);
        if (subfolderNamePosition < 0
            || subfolderNamePosition >= totalPartCount
            || subfolderNamePosition == dateFormatPosition)
        {
            subfolderNamePosition = totalPartCount - 1;
            if (subfolderNamePosition == dateFormatPosition)
            {
                subfolderNamePosition--;
            }
        }
    }

    private void SaveExclusionSettingsChanged(bool refreshUi = true)
    {
        Undo.RecordObject(PackageExporterSettings.instance, "PackageExporter Exclusion Settings Changed");
        ResetNestedPrefabSelectionState();
        SaveWindowStateToSettings("PackageExporter Exclusion Settings Changed", false);
        ScheduleExportPrecheckScan();
        if (refreshUi)
        {
            RefreshUI();
        }
    }

    private void SetAllSubfolderSelections(bool isSelected)
    {
        var keys = new List<string>(subFolderSelections.Keys);
        foreach (var key in keys)
        {
            subFolderSelections[key] = isSelected;
        }
    }

    private void RefreshSubfolderCheckboxes()
    {
        if (subfolderContainer == null)
        {
            return;
        }

        subfolderContainer.Clear();
        if (subFolderSelections.Count == 0)
        {
            subfolderContainer.Add(new Label("サブフォルダ未取得"));
            return;
        }

        foreach (var kvp in subFolderSelections.ToList())
        {
            string folderPath = kvp.Key;
            string folderName = Path.GetFileName(folderPath);
            var toggle = new Toggle(folderName) { value = kvp.Value };
            toggle.RegisterValueChangedCallback(evt =>
            {
                subFolderSelections[folderPath] = evt.newValue;
                ScheduleExportPrecheckScan();
                RefreshUI();
            });
            subfolderContainer.Add(toggle);
        }
    }

    private void RefreshExclusionKeywordFields()
    {
        if (exclusionKeywordsContainer == null)
        {
            return;
        }

        exclusionKeywordsContainer.Clear();
        var keywords = PackageExporterSettings.instance.exclusionKeywords;
        for (var i = 0; i < keywords.Count; i++)
        {
            int index = i;
            var row = CreateRow();
            var field = new TextField($"Keyword {index + 1}") { value = keywords[index] };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(PackageExporterSettings.instance, "PackageExporter Exclusion Keyword Changed");
                keywords[index] = evt.newValue ?? string.Empty;
                SaveExclusionSettingsChanged(false);
            });
            row.Add(field);

            var removeButton = new Button(() =>
            {
                Undo.RecordObject(PackageExporterSettings.instance, "PackageExporter Exclusion Keyword Removed");
                keywords.RemoveAt(index);
                SaveExclusionSettingsChanged();
                RefreshUI();
            }) { text = "削除" };
            removeButton.AddToClassList(button_base_class);
            removeButton.AddToClassList(button_danger_class);
            removeButton.style.width = 52;
            row.Add(removeButton);
            exclusionKeywordsContainer.Add(row);
        }
    }

    private void RefreshNestedPrefabSelectionUI()
    {
        if (precheckContainer == null)
        {
            return;
        }

        precheckContainer.Clear();
        bool hasPrecheck = pendingNestedPrefabSelection || detectedExcludedImageWarnings.Count > 0;
        precheckContainer.style.display = hasPrecheck ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasPrecheck)
        {
            return;
        }

        precheckContainer.Add(new Label("エクスポート前チェック")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginTop = 8,
                marginBottom = 3
            }
        });

        if (detectedNestedPrefabPaths.Count > 0)
        {
            precheckContainer.Add(new HelpBox("ネストPrefabを検知しました。チェックONで含める（現在の挙動）、OFFで含めない。", HelpBoxMessageType.Info));
            precheckContainer.Add(CreateToggle("ネストPrefabを含める", includeNestedPrefabs, value =>
            {
                Undo.RecordObject(PackageExporterSettings.instance, "PackageExporter Nested Prefab Option Changed");
                includeNestedPrefabs = value;
                SaveWindowStateToSettings("PackageExporter Nested Prefab Option Changed", false);
                RefreshUI();
            }));
        }

        if (detectedExcludedImageWarnings.Count > 0)
        {
            precheckContainer.Add(new HelpBox(
                "ModularAvatarMeshCutter のマスク画像が除外設定によりエクスポート対象外です。このまま出力すると VertexFilterByMaskComponent の参照が Missing になる可能性があります。",
                HelpBoxMessageType.Warning));
        }

        if (detectedNestedPrefabPaths.Count > 0)
        {
            precheckContainer.Add(new Label("ネストPrefab") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            foreach (string nestedPrefabPath in detectedNestedPrefabPaths)
            {
                precheckContainer.Add(new Label(nestedPrefabPath) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
            }
        }

        if (detectedExcludedImageWarnings.Count > 0)
        {
            precheckContainer.Add(new Label("除外されているマスク画像") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            foreach (string warning in detectedExcludedImageWarnings)
            {
                precheckContainer.Add(new Label(warning) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11 } });
            }
        }
    }

    private void RefreshPackageNamePreview()
    {
        if (packagePreviewLabel == null)
        {
            return;
        }

        string dateString = TryFormatDateForPreview(out bool dateValid);
        string folderPreview = GetSubfolderPreviewName();
        string packageFileName = BuildPackageFileName(folderPreview, dateString);

        packagePreviewWarningBox.style.display = dateValid ? DisplayStyle.None : DisplayStyle.Flex;
        packagePreviewLabel.text = $"出力例: {packageFileName}";
        packagePreviewHintLabel.style.display = folderPreview == "***" ? DisplayStyle.Flex : DisplayStyle.None;
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

        var box = new VisualElement
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
                marginBottom = 8
            }
        };
        parent.Add(box);
        return box;
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

    private static void AddStyleSheet(VisualElement root)
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageExporter).Assembly);
        if (package == null) return;
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(package.assetPath + "/Editor/PackageExporter.uss");
        if (styleSheet != null)
        {
            root.styleSheets.Add(styleSheet);
        }
    }

    private static Button CreateButton(string text, Action onClick, params string[] classNames)
    {
        var button = new Button(onClick)
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
        button.AddToClassList(button_base_class);
        if (classNames == null || classNames.Length == 0)
        {
            button.AddToClassList(button_select_class);
            return button;
        }

        foreach (var className in classNames)
        {
            if (!string.IsNullOrWhiteSpace(className))
            {
                button.AddToClassList(className);
            }
        }

        return button;
    }

    private static TextField CreateTextField(string label, string value, Action<string> onChange)
    {
        var field = new TextField(label) { value = value ?? string.Empty };
        field.RegisterValueChangedCallback(evt => onChange(evt.newValue ?? string.Empty));
        return field;
    }

    private static Toggle CreateToggle(string label, bool value, Action<bool> onChange)
    {
        var toggle = new Toggle(label) { value = value };
        toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
        return toggle;
    }

    private void LoadExportProfileFromJson()
    {
        string jsonPath = EditorUtility.OpenFilePanel("プロファイルJSONを読み込み", ResolveInitialProfileDirectory(), "json");
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return;
        }

        if (!TryReadProfileFromJson(jsonPath, out ExportProfileJson profile))
        {
            EditorUtility.DisplayDialog("読み込み失敗", "JSONプロファイルの読み込みに失敗しました。Consoleを確認してください。", "OK");
            return;
        }

        Undo.RecordObject(this, "Load Export Profile");
        Undo.RecordObject(PackageExporterSettings.instance, "Load Export Profile");
        ApplyProfile(profile);
        SaveWindowStateToSettings("Load Export Profile", false);
        SetCurrentProfilePath(jsonPath);
        RebuildUI();
        Debug.Log("Loaded export profile: " + jsonPath);
    }

    private void SaveCurrentSettingsAsProfileJson()
    {
        string initialDirectory = TryGetAbsoluteExportPath(out string exportAbsolutePath)
            ? exportAbsolutePath
            : ResolveInitialProfileDirectory();
        string defaultFileName = Path.GetFileNameWithoutExtension(BuildProfileFileName());
        string jsonPath = EditorUtility.SaveFilePanel("現在の設定をJSONとして保存", initialDirectory, defaultFileName, "json");
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return;
        }

        if (File.Exists(jsonPath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "上書き確認",
                "同名のJSONファイルが既に存在します。上書きしますか？\n" + jsonPath,
                "上書き",
                "キャンセル"
            );
            if (!overwrite)
            {
                return;
            }
        }

        if (!TryWriteProfileToJson(jsonPath))
        {
            EditorUtility.DisplayDialog("保存失敗", "JSONプロファイルの保存に失敗しました。Consoleを確認してください。", "OK");
            return;
        }

        SetCurrentProfilePath(jsonPath);
        AssetDatabase.Refresh();
        Debug.Log("Saved export profile: " + jsonPath);
    }

    private bool TryReadProfileFromJson(string jsonPath, out ExportProfileJson profile)
    {
        profile = CreateCurrentProfile();
        try
        {
            string json = File.ReadAllText(jsonPath);
            JsonConvert.PopulateObject(json, profile, GetJsonReadSettings());
            NormalizeProfile(profile);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to load profile json: " + ex.Message);
            return false;
        }
    }

    private bool TryWriteProfileToJson(string jsonPath)
    {
        try
        {
            var profile = CreateCurrentProfile();
            string json = JsonConvert.SerializeObject(profile, Formatting.Indented, GetJsonWriteSettings());
            File.WriteAllText(jsonPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to save profile json: " + ex.Message);
            return false;
        }
    }

    private void NormalizeProfile(ExportProfileJson profile)
    {
        if (profile == null)
        {
            return;
        }

        profile.exclusionKeywords ??= new List<string>();
        profile.subFolderSelections ??= new List<SubFolderSelectionJson>();
        profile.additionalNameStrings ??= new List<string>();
        if (profile.additionalNameStrings.Count > max_additional_name_strings)
        {
            profile.additionalNameStrings.RemoveRange(
                max_additional_name_strings,
                profile.additionalNameStrings.Count - max_additional_name_strings);
        }
        int totalPartCount = profile.additionalNameStrings.Count + 2;
        profile.dateFormatPosition = Mathf.Clamp(profile.dateFormatPosition, 0, totalPartCount - 1);
        if (profile.subfolderNamePosition < 0
            || profile.subfolderNamePosition >= totalPartCount
            || profile.subfolderNamePosition == profile.dateFormatPosition)
        {
            profile.subfolderNamePosition = totalPartCount - 1;
            if (profile.subfolderNamePosition == profile.dateFormatPosition)
            {
                profile.subfolderNamePosition--;
            }
        }
    }

    private JsonSerializerSettings GetJsonReadSettings()
    {
        return new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include
        };
    }

    private JsonSerializerSettings GetJsonWriteSettings()
    {
        return new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include
        };
    }

    private ExportProfileJson CreateCurrentProfile()
    {
        return new ExportProfileJson
        {
            exportPath = exportPath,
            outPath = outPath,
            packageName = packageName,
            dateFormat = dateFormat,
            additionalNameStrings = new List<string>(additionalNameStrings),
            dateFormatPosition = dateFormatPosition,
            subfolderNamePosition = subfolderNamePosition,
            te64Path = te64Path,
            openWithTE = openWithTE,
            overrideExclusionForSubfolders = overrideExclusionForSubfolders,
            excludeDirectoriesWithHyphen = excludeDirectoriesWithHyphen,
            includeMetaFiles = includeMetaFiles,
            includeNestedPrefabs = includeNestedPrefabs,
            includeOtherComponentsInLog = includeOtherComponentsInLog,
            exclusionKeywords = new List<string>(PackageExporterSettings.instance.exclusionKeywords),
            subFolderSelections = ConvertSubFolderSelectionsToJsonList(subFolderSelections)
        };
    }

    private void ApplyProfile(ExportProfileJson profile)
    {
        exportPath = profile.exportPath ?? "";
        outPath = profile.outPath ?? "";
        packageName = profile.packageName ?? "";
        dateFormat = profile.dateFormat ?? "yyyy-MM-dd";
        additionalNameStrings = new List<string>(profile.additionalNameStrings ?? new List<string>());
        dateFormatPosition = profile.dateFormatPosition;
        subfolderNamePosition = profile.subfolderNamePosition;
        NormalizePackageNameParts();
        te64Path = profile.te64Path ?? "";
        openWithTE = profile.openWithTE;
        overrideExclusionForSubfolders = profile.overrideExclusionForSubfolders;
        excludeDirectoriesWithHyphen = profile.excludeDirectoriesWithHyphen;
        includeMetaFiles = profile.includeMetaFiles;
        includeNestedPrefabs = profile.includeNestedPrefabs;
        includeOtherComponentsInLog = profile.includeOtherComponentsInLog;
        subFolderSelections = ConvertSubFolderSelectionsToDictionary(profile.subFolderSelections);
        PackageExporterSettings.instance.exclusionKeywords = new List<string>(profile.exclusionKeywords ?? new List<string>());
    }

    private List<SubFolderSelectionJson> ConvertSubFolderSelectionsToJsonList(Dictionary<string, bool> source)
    {
        var result = new List<SubFolderSelectionJson>();
        if (source == null)
        {
            return result;
        }

        foreach (var kvp in source.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new SubFolderSelectionJson
            {
                folderPath = kvp.Key,
                isSelected = kvp.Value
            });
        }
        return result;
    }

    private Dictionary<string, bool> ConvertSubFolderSelectionsToDictionary(List<SubFolderSelectionJson> source)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return result;
        }

        foreach (var entry in source)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.folderPath))
            {
                continue;
            }
            result[entry.folderPath] = entry.isSelected;
        }
        return result;
    }

    private string ResolveInitialProfileDirectory()
    {
        if (!string.IsNullOrWhiteSpace(currentProfilePath))
        {
            string currentDir = Path.GetDirectoryName(currentProfilePath);
            if (!string.IsNullOrWhiteSpace(currentDir) && Directory.Exists(currentDir))
            {
                return currentDir;
            }
        }

        if (TryGetAbsoluteExportPath(out string exportAbsolutePath))
        {
            return exportAbsolutePath;
        }

        if (!string.IsNullOrWhiteSpace(outPath) && Directory.Exists(outPath))
        {
            return outPath;
        }

        return Application.dataPath;
    }

    private bool TryGetAbsoluteExportPath(out string exportAbsolutePath)
    {
        exportAbsolutePath = string.Empty;

        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return false;
        }

        string normalizedExportPath = exportPath.Replace('\\', '/');
        if (!normalizedExportPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!AssetDatabase.IsValidFolder(normalizedExportPath))
        {
            return false;
        }

        string projectRootPath = Directory.GetParent(Application.dataPath).FullName;
        exportAbsolutePath = Path.GetFullPath(Path.Combine(projectRootPath, normalizedExportPath));
        return Directory.Exists(exportAbsolutePath);
    }

    private string BuildProfileFileName()
    {
        string safePackageName = SanitizeFileName(string.IsNullOrWhiteSpace(packageName) ? "PackageExporter" : packageName);
        string safeDateText = SanitizeFileName(GetDateTextForProfileFileName());
        return safePackageName + safeDateText + "_ExportSetting.json";
    }

    private string GetDateTextForProfileFileName()
    {
        try
        {
            return DateTime.Today.ToString(dateFormat);
        }
        catch
        {
            Debug.LogWarning("Date Format が無効なため、プロファイル保存時は yyyy-MM-dd を使用します。");
            return DateTime.Today.ToString("yyyy-MM-dd");
        }
    }

    private void SetCurrentProfilePath(string profilePath)
    {
        currentProfilePath = profilePath ?? "";
        EditorPrefs.SetString(LastProfilePathEditorPrefsKey, currentProfilePath);
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "PackageExporterProfile";
        }

        fileName = EditorFileNameHelper.SanitizeFileName(fileName);

        return fileName;
    }

    /// <summary>
    /// サブフォルダを取得し、チェックボックス表示用のデータを準備する
    /// </summary>
    private void GetSubfolders()
    {
        if (string.IsNullOrEmpty(exportPath) || !Directory.Exists(exportPath))
        {
            Debug.LogError("Export path does not exist: " + exportPath);
            return;
        }

        // ExportPath直下のサブフォルダを取得
        string[] allSubFolders = AssetDatabase.GetSubFolders(exportPath);
        
        // 新しいDictionaryを作成
        Dictionary<string, bool> newSelections = new Dictionary<string, bool>();
        
        foreach (string folder in allSubFolders)
        {
            string folderName = System.IO.Path.GetFileName(folder);
            
            // "-"で始まるフォルダは除外
            if (folderName.StartsWith("-"))
                continue;
                
            // 既存の選択状態を保持するか、新規の場合はデフォルトでtrueに設定
            bool isSelected = subFolderSelections.ContainsKey(folder) ? subFolderSelections[folder] : true;
            newSelections[folder] = isSelected;
        }
        
        // 新しいDictionaryで更新
        subFolderSelections = newSelections;
        
        Debug.Log($"Found {subFolderSelections.Count} valid subfolders in {exportPath}");
    }

    private void DetectNestedPrefabsForCurrentSelection()
    {
        List<string> selectedFolders = subFolderSelections
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        if (selectedFolders.Count == 0)
        {
            ResetNestedPrefabSelectionState();
            return;
        }

        Regex exclusionRegex = BuildExclusionRegex();
        UpdateExportPrecheck(selectedFolders, exclusionRegex);
    }
    
    /// <summary>
    /// サブフォルダのチェックボックスを表示する
    /// </summary>
    private void DrawSubfolderCheckboxes()
    {
        RefreshSubfolderCheckboxes();
    }

    /// <summary>
    /// 除外キーワードとディレクトリ除外条件の設定を表示
    /// </summary>
    private void DrawExclusionSettings()
    {
        RefreshExclusionKeywordFields();
    }

    private void DrawNestedPrefabSelectionUI()
    {
        RefreshNestedPrefabSelectionUI();
    }

    private void ResetNestedPrefabSelectionState()
    {
        pendingNestedPrefabSelection = false;
        detectedNestedPrefabPaths.Clear();
        detectedExcludedImageWarnings.Clear();
    }

    /// <summary>
    /// バッチエクスポート処理全体の開始。パスの検証や例外処理を実施。
    /// </summary>
    private void StartBatchExport()
    {
        try
        {
            if (!TryPrepareExportContext(out List<string> selectedFolders, out Regex exclusionRegex))
            {
                return;
            }

            CancelScheduledExportPrecheckScan();
            UpdateExportPrecheck(selectedFolders, exclusionRegex);
            ExecuteBatchExport(selectedFolders, exclusionRegex, detectedNestedPrefabPaths);
            ResetNestedPrefabSelectionState();
        }
        catch (Exception ex)
        {
            Debug.LogError("An error occurred during export: " + ex.Message + "\n" + ex.StackTrace);
            ResetNestedPrefabSelectionState();
        }
    }

    private bool TryPrepareExportContext(out List<string> selectedFolders, out Regex exclusionRegex)
    {
        selectedFolders = new List<string>();
        exclusionRegex = null;

        if (!Directory.Exists(exportPath))
        {
            Debug.LogError("Export path does not exist: " + exportPath);
            return false;
        }

        if (!Directory.Exists(outPath))
        {
            Debug.LogError("Output folder does not exist: " + outPath);
            return false;
        }

        if (subFolderSelections.Count == 0)
        {
            GetSubfolders();
            if (subFolderSelections.Count == 0)
            {
                Debug.LogError("No subfolders found in export path: " + exportPath);
                return false;
            }
        }

        selectedFolders = subFolderSelections
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        if (selectedFolders.Count == 0)
        {
            Debug.LogWarning("No folders selected for export.");
            return false;
        }

        exclusionRegex = BuildExclusionRegex();
        return true;
    }

    private void ExecuteBatchExport(List<string> selectedFolders, Regex exclusionRegex, List<string> nestedPrefabPaths)
    {
        string dateString = DateTime.Today.ToString(dateFormat);
        string lastPackageFullPath = null;
        var nestedPrefabSet = new HashSet<string>(nestedPrefabPaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (string folder in selectedFolders)
        {
            string exportedPackagePath = ProcessFolder(
                folder,
                dateString,
                exclusionRegex,
                overrideExclusionForSubfolders,
                includeNestedPrefabs,
                nestedPrefabSet);

            if (!string.IsNullOrEmpty(exportedPackagePath))
            {
                lastPackageFullPath = exportedPackagePath;
            }
        }

        if (openWithTE && !string.IsNullOrEmpty(lastPackageFullPath))
        {
            TryOpenWithTE(lastPackageFullPath);
        }

        Debug.Log($"Batch export completed successfully. Exported {selectedFolders.Count} folders.");
    }

    /// <summary>
    /// 指定されたフォルダ内のアセットと依存アセットを収集し、パッケージをエクスポートする
    /// </summary>
    /// <param name="folder">対象のフォルダパス</param>
    /// <param name="dateString">日付文字列</param>
    private string ProcessFolder(
        string folder,
        string dateString,
        Regex exclusionRegex,
        bool overrideExclusion,
        bool includeNestedPrefabDependencies,
        HashSet<string> detectedNestedPrefabSet)
    {
        HashSet<string> assetPathsToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visitedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            CollectAssetAndDependencies(
                assetPath,
                folder,
                exclusionRegex,
                overrideExclusion,
                includeNestedPrefabDependencies,
                detectedNestedPrefabSet,
                assetPathsToExport,
                visitedAssetPaths,
                false);
        }

        string folderName = System.IO.Path.GetFileName(folder);
        string packageFileName = BuildPackageFileName(folderName, dateString);
        string packageFullPath = System.IO.Path.Combine(outPath, packageFileName);

        string reportFileName = $"{System.IO.Path.GetFileNameWithoutExtension(packageFileName)}_components.json";
        string reportFilePath = System.IO.Path.Combine(outPath, reportFileName);

        // エクスポート処理
        ExportPackage(assetPathsToExport.ToArray(), packageFullPath);

        WriteExportedAssetsJsonReport(assetPathsToExport, reportFilePath, packageFullPath, includeMetaFiles, includeOtherComponentsInLog);
        return packageFullPath;
    }

    private string BuildPackageFileName(string folderName, string dateString)
    {
        NormalizePackageNameParts();
        var nameParts = new List<string>();
        AddPackageFileNamePart(nameParts, packageName);

        foreach (int partToken in BuildPackageNamePartOrder())
        {
            if (partToken == date_format_part_token)
            {
                AddPackageFileNamePart(nameParts, dateString);
            }
            else if (partToken == subfolder_name_part_token)
            {
                AddPackageFileNamePart(nameParts, folderName);
            }
            else
            {
                AddPackageFileNamePart(nameParts, additionalNameStrings[partToken]);
            }
        }

        return string.Join("_", nameParts) + ".unitypackage";
    }

    private static void AddPackageFileNamePart(List<string> nameParts, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string sanitizedValue = value.Trim();
        sanitizedValue = EditorFileNameHelper.SanitizeFileName(sanitizedValue);

        if (!string.IsNullOrWhiteSpace(sanitizedValue))
        {
            nameParts.Add(sanitizedValue);
        }
    }

    private void CollectAssetAndDependencies(
        string assetPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        bool includeNestedPrefabDependencies,
        HashSet<string> detectedNestedPrefabSet,
        HashSet<string> assetPathsToExport,
        HashSet<string> visitedAssetPaths,
        bool isDependency)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        if (ShouldSkipAssetPath(
                assetPath,
                folder,
                exclusionRegex,
                overrideExclusion,
                isDependency,
                includeNestedPrefabDependencies,
                detectedNestedPrefabSet))
        {
            return;
        }

        if (!visitedAssetPaths.Add(assetPath))
        {
            return;
        }

        assetPathsToExport.Add(assetPath);

        string[] dependencies = AssetDatabase.GetDependencies(assetPath, false);
        foreach (string dependencyPath in dependencies)
        {
            if (string.IsNullOrEmpty(dependencyPath)
                || dependencyPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CollectAssetAndDependencies(
                dependencyPath,
                folder,
                exclusionRegex,
                overrideExclusion,
                includeNestedPrefabDependencies,
                detectedNestedPrefabSet,
                assetPathsToExport,
                visitedAssetPaths,
                true);
        }
    }

    private bool ShouldSkipAssetPath(
        string assetPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        bool isDependency,
        bool includeNestedPrefabDependencies,
        HashSet<string> detectedNestedPrefabSet)
    {
        if (ShouldSkipByExclusion(assetPath, folder, exclusionRegex, overrideExclusion))
        {
            return true;
        }

        if (!isDependency)
        {
            return false;
        }

        if (excludeDirectoriesWithHyphen && assetPath.Contains("-"))
        {
            return true;
        }

        if (!includeNestedPrefabDependencies
            && detectedNestedPrefabSet != null
            && detectedNestedPrefabSet.Contains(assetPath))
        {
            return true;
        }

        return false;
    }

    private bool ShouldSkipByExclusion(string assetPath, string folder, Regex exclusionRegex, bool overrideExclusion)
    {
        if (exclusionRegex == null || string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        bool shouldApplyExclusion = !overrideExclusion
            || !assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        return shouldApplyExclusion && exclusionRegex.IsMatch(assetPath);
    }

    private List<string> DetectNestedPrefabDependencies(
        IEnumerable<string> folders,
        Regex exclusionRegex,
        bool overrideExclusion)
    {
        var detectedNestedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedPrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkipByExclusion(prefabPath, folder, exclusionRegex, overrideExclusion))
                {
                    continue;
                }

                CollectNestedPrefabDependenciesRecursive(
                    prefabPath,
                    folder,
                    exclusionRegex,
                    overrideExclusion,
                    detectedNestedPrefabs,
                    visitedPrefabPaths);
            }
        }

        return detectedNestedPrefabs
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> DetectExcludedMeshCutterImages(
        IEnumerable<string> folders,
        Regex exclusionRegex,
        bool overrideExclusion)
    {
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkipByExclusion(prefabPath, folder, exclusionRegex, overrideExclusion))
                {
                    continue;
                }

                CollectExcludedMeshCutterImages(prefabPath, folder, exclusionRegex, overrideExclusion, warnings);
            }
        }

        return warnings
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectExcludedMeshCutterImages(
        string prefabPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        HashSet<string> warnings)
    {
        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                return;
            }

            foreach (Component component in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "ModularAvatarMeshCutter")
                {
                    continue;
                }

                CollectExcludedMeshCutterMaskReferences(component, prefabPath, folder, exclusionRegex, overrideExclusion, warnings);
            }
        }
        catch (Exception)
        {
            // UI警告用の検査なので、検査失敗時はコンソールには出さない。
        }
        finally
        {
            if (prefabRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

    private void ScheduleExportPrecheckScan()
    {
        if (exportPrecheckScanScheduled)
        {
            return;
        }

        exportPrecheckScanScheduled = true;
        EditorApplication.delayCall += RunScheduledExportPrecheckScan;
    }

    private void CancelScheduledExportPrecheckScan()
    {
        if (!exportPrecheckScanScheduled)
        {
            return;
        }

        exportPrecheckScanScheduled = false;
        EditorApplication.delayCall -= RunScheduledExportPrecheckScan;
    }

    private void RunScheduledExportPrecheckScan()
    {
        exportPrecheckScanScheduled = false;
        if (this == null)
        {
            return;
        }

        DetectNestedPrefabsForCurrentSelection();
    }

    private void UpdateExportPrecheck(List<string> selectedFolders, Regex exclusionRegex)
    {
        if (selectedFolders == null || selectedFolders.Count == 0)
        {
            ResetNestedPrefabSelectionState();
            return;
        }

        List<string> nestedPrefabs = DetectNestedPrefabDependencies(
            selectedFolders,
            exclusionRegex,
            overrideExclusionForSubfolders);
        List<string> excludedImageWarnings = DetectExcludedMeshCutterImages(
            selectedFolders,
            exclusionRegex,
            overrideExclusionForSubfolders);

        detectedNestedPrefabPaths = nestedPrefabs;
        detectedExcludedImageWarnings = excludedImageWarnings;
        pendingNestedPrefabSelection = nestedPrefabs.Count > 0 || excludedImageWarnings.Count > 0;
        RefreshUI();
        if (nestedPrefabs.Count > 0)
        {
            Debug.Log($"Nested prefabs detected: {nestedPrefabs.Count}");
        }
    }

    private void CollectExcludedMeshCutterMaskReferences(
        Component meshCutter,
        string prefabPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        HashSet<string> warnings)
    {
        foreach (Component component in meshCutter.gameObject.GetComponents<Component>())
        {
            if (component == null || component.GetType().Name != "VertexFilterByMaskComponent")
            {
                continue;
            }

            CollectExcludedTextureReferences(component, prefabPath, folder, exclusionRegex, overrideExclusion, warnings);
        }
    }

    private void CollectExcludedTextureReferences(
        Component component,
        string prefabPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        HashSet<string> warnings)
    {
        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty property = serializedObject.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            if (!(property.objectReferenceValue is Texture2D))
            {
                continue;
            }

            string texturePath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
            if (!IsImageAssetPath(texturePath))
            {
                continue;
            }

            if (ShouldSkipAssetPath(texturePath, folder, exclusionRegex, overrideExclusion, true, true, null))
            {
                warnings.Add($"{prefabPath} -> {texturePath}");
            }
        }
    }

    private bool IsImageAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string extension = Path.GetExtension(assetPath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".psd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exr", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hdr", StringComparison.OrdinalIgnoreCase);
    }

    private void CollectNestedPrefabDependenciesRecursive(
        string prefabPath,
        string folder,
        Regex exclusionRegex,
        bool overrideExclusion,
        HashSet<string> detectedNestedPrefabs,
        HashSet<string> visitedPrefabPaths)
    {
        if (string.IsNullOrWhiteSpace(prefabPath)
            || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            || !visitedPrefabPaths.Add(prefabPath))
        {
            return;
        }

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                return;
            }

            foreach (Transform transform in prefabRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == prefabRoot.transform)
                {
                    continue;
                }

                GameObject nestedRoot = transform.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(nestedRoot))
                {
                    continue;
                }

                UnityEngine.Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(nestedRoot);
                string sourcePath = sourceObject != null
                    ? AssetDatabase.GetAssetPath(sourceObject)
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(sourcePath)
                    || !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || sourcePath.Equals(prefabPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ShouldSkipAssetPath(
                        sourcePath,
                        folder,
                        exclusionRegex,
                        overrideExclusion,
                        true,
                        true,
                        null))
                {
                    continue;
                }

                detectedNestedPrefabs.Add(sourcePath);

                CollectNestedPrefabDependenciesRecursive(
                    sourcePath,
                    folder,
                    exclusionRegex,
                    overrideExclusion,
                    detectedNestedPrefabs,
                    visitedPrefabPaths);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to inspect nested prefabs: {prefabPath} - {ex.Message}");
        }
        finally
        {
            if (prefabRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

    /// <summary>
    /// AssetDatabase.ExportPackageを用いて、パッケージをエクスポートする
    /// </summary>
    /// <param name="assetPaths">エクスポート対象のアセットパス一覧</param>
    /// <param name="packageFullPath">出力先のパッケージパス</param>
    private void ExportPackage(string[] assetPaths, string packageFullPath)
    {
        if (assetPaths.Length == 0)
        {
            Debug.LogWarning("No assets found to export for package: " + packageFullPath);
            return;
        }

        var options = openWithTE ? ExportPackageOptions.Default : ExportPackageOptions.Interactive;
        AssetDatabase.ExportPackage(assetPaths, packageFullPath, options);
        Debug.Log("Exported package: " + packageFullPath);
    }

    private void TryOpenWithTE(string packageFullPath)
    {
        if (string.IsNullOrWhiteSpace(te64Path))
        {
            Debug.LogWarning("TE64.exe path is not set. Skip opening Tablacus Explorer.");
            return;
        }

        if (!File.Exists(te64Path))
        {
            Debug.LogError("TE64.exe not found: " + te64Path);
            return;
        }

        string packageDirectory = Path.GetDirectoryName(packageFullPath);
        if (string.IsNullOrEmpty(packageDirectory))
        {
            Debug.LogError("Failed to resolve package directory from: " + packageFullPath);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = te64Path,
                Arguments = $"\"{packageDirectory}\"",
                UseShellExecute = false,
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to open Tablacus Explorer: " + ex.Message);
        }
    }

    private void DrawPackageNamePreview()
    {
        RefreshPackageNamePreview();
    }

    private string TryFormatDateForPreview(out bool valid)
    {
        try
        {
            valid = true;
            return DateTime.Today.ToString(dateFormat);
        }
        catch
        {
            valid = false;
            return "InvalidDateFormat";
        }
    }

    private string GetSubfolderPreviewName()
    {
        if (subFolderSelections == null || subFolderSelections.Count == 0)
            return "***";

        var selected = subFolderSelections.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        if (selected.Count != 1)
            return "***";

        return Path.GetFileName(selected[0]);
    }

    /// <summary>
    /// エクスポートしたパッケージごとにコンポーネント比較用JSONを出力する
    /// </summary>
    private void WriteExportedAssetsJsonReport(
        HashSet<string> assetPaths,
        string reportFilePath,
        string packagePath,
        bool metaFiles,
        bool includeOtherComponents)
    {
        try
        {
            var report = BuildPackageComponentReport(assetPaths, packagePath, metaFiles, includeOtherComponents);
            string json = JsonConvert.SerializeObject(report, Formatting.Indented, GetJsonWriteSettings());
            File.WriteAllText(reportFilePath, json);
            Debug.Log("Wrote package component report: " + reportFilePath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Error writing component json report: " + ex.Message);
        }
    }

    private PackageComponentReportJson BuildPackageComponentReport(
        HashSet<string> assetPaths,
        string packagePath,
        bool metaFiles,
        bool includeOtherComponents)
    {
        List<string> normalizedAssetPaths = assetPaths
            .Select(path => path.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> prefabPaths = normalizedAssetPaths
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var report = new PackageComponentReportJson
        {
            generated_at = DateTime.Now.ToString("o"),
            package_path = packagePath,
            package_file = System.IO.Path.GetFileName(packagePath),
            report_target = includeOtherComponents
                ? "Transform以外の全コンポーネント"
                : "VRCSDK / ModularAvatar / Unity標準主要コンポーネント",
            include_meta_files = metaFiles,
            include_other_components = includeOtherComponents,
            asset_count = CountExportedAssets(normalizedAssetPaths, metaFiles),
            prefab_count = prefabPaths.Count,
            asset_paths = normalizedAssetPaths
        };

        foreach (string prefabPath in prefabPaths)
        {
            var prefabReport = BuildPrefabComponentReport(prefabPath, includeOtherComponents, normalizedAssetPaths);
            report.prefabs.Add(prefabReport);
            report.component_instances.AddRange(prefabReport.game_objects.SelectMany(item => item.components));
            report.component_details.AddRange(prefabReport.component_details);
            report.reference_audit.items.AddRange(prefabReport.reference_audit_items);
        }

        report.target_component_count = report.component_instances.Count;
        report.component_summary = BuildComponentSummary(report.component_instances);
        report.summary = BuildComponentSummaryText(report.component_summary);
        PopulateReferenceAuditCounts(report.reference_audit);
        return report;
    }

    private int CountExportedAssets(List<string> normalizedAssetPaths, bool metaFiles)
    {
        int actualAssetCount = 0;
        foreach (string normalizedAsset in normalizedAssetPaths)
        {
            if (normalizedAsset.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (metaFiles)
                {
                    actualAssetCount++;
                }
                continue;
            }

            actualAssetCount++;
            if (metaFiles && File.Exists(normalizedAsset + ".meta"))
            {
                actualAssetCount++;
            }
        }

        return actualAssetCount;
    }

    private PrefabComponentReportJson BuildPrefabComponentReport(
        string prefabPath,
        bool includeOtherComponents,
        List<string> packageAssetPaths)
    {
        var prefabReport = new PrefabComponentReportJson
        {
            prefab_path = prefabPath,
            prefab_name = System.IO.Path.GetFileNameWithoutExtension(prefabPath),
            blendshapes = CollectPrefabBlendshapeNames(prefabPath),
            skinned_mesh_renderer_bounds = CollectSkinnedMeshBoundsJson(prefabPath)
        };

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            return prefabReport;
        }

        var packageAssetSet = new HashSet<string>(packageAssetPaths, StringComparer.OrdinalIgnoreCase);
        CollectGameObjectComponentReport(prefab, prefab.transform, prefabPath, includeOtherComponents, prefabReport, packageAssetSet);
        prefabReport.game_objects = prefabReport.game_objects
            .OrderBy(item => item.object_path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        prefabReport.target_component_count = prefabReport.game_objects.Sum(item => item.components.Count);
        prefabReport.has_target_components = prefabReport.target_component_count > 0;
        prefabReport.component_summary = BuildComponentSummary(prefabReport.game_objects.SelectMany(item => item.components));
        prefabReport.component_details = prefabReport.component_details
            .OrderBy(item => item.object_path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.component_index)
            .ToList();
        prefabReport.reference_audit_items = prefabReport.reference_audit_items
            .Where(item => item.status != "none")
            .OrderBy(item => item.status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.source_object_path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.property_path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return prefabReport;
    }

    private bool CollectGameObjectComponentReport(
        GameObject gameObject,
        Transform root,
        string prefabPath,
        bool includeOtherComponents,
        PrefabComponentReportJson prefabReport,
        HashSet<string> packageAssetSet)
    {
        var transform = gameObject.transform;
        string objectPath = GetTransformPath(transform, root);
        string parentPath = transform.parent != null && transform != root
            ? GetTransformPath(transform.parent, root)
            : "";

        var gameObjectReport = new GameObjectComponentReportJson
        {
            name = gameObject.name,
            object_path = objectPath,
            parent_path = parentPath,
            depth = GetTransformDepth(transform, root),
            sibling_index = transform.GetSiblingIndex()
        };

        Component[] components = gameObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
            {
                gameObjectReport.missing_component_count++;
                continue;
            }

            if (IsLogTargetComponent(component, includeOtherComponents))
            {
                gameObjectReport.components.Add(CreateComponentInstanceJson(component, prefabPath, objectPath, parentPath, gameObjectReport.depth, i));
                ComponentDetailJson detail = CreateComponentDetailJson(component, prefabPath, objectPath, i, root);
                if (detail.details.Count > 0)
                {
                    prefabReport.component_details.Add(detail);
                }
            }

            prefabReport.reference_audit_items.AddRange(CollectComponentReferenceAuditItems(component, prefabPath, objectPath, i, packageAssetSet));
        }

        bool childHasTargetComponents = false;
        foreach (Transform child in transform)
        {
            if (CollectGameObjectComponentReport(child.gameObject, root, prefabPath, includeOtherComponents, prefabReport, packageAssetSet))
            {
                childHasTargetComponents = true;
            }
        }

        bool hasTargetComponents = gameObjectReport.components.Count > 0;
        if (hasTargetComponents || childHasTargetComponents)
        {
            prefabReport.game_objects.Add(gameObjectReport);
            return true;
        }

        return false;
    }

    private ComponentInstanceJson CreateComponentInstanceJson(
        Component component,
        string prefabPath,
        string objectPath,
        string parentPath,
        int depth,
        int componentIndex)
    {
        Type type = component.GetType();
        return new ComponentInstanceJson
        {
            prefab_path = prefabPath,
            object_path = objectPath,
            parent_path = parentPath,
            depth = depth,
            component_index = componentIndex,
            type_name = type.Name,
            type_full_name = type.FullName ?? type.Name,
            type_namespace = type.Namespace ?? "",
            assembly_name = type.Assembly.GetName().Name ?? "",
            category = GetComponentCategory(component)
        };
    }

    private ComponentDetailJson CreateComponentDetailJson(
        Component component,
        string prefabPath,
        string objectPath,
        int componentIndex,
        Transform root)
    {
        Type type = component.GetType();
        var detail = new ComponentDetailJson
        {
            prefab_path = prefabPath,
            object_path = objectPath,
            component_index = componentIndex,
            type_name = type.Name,
            type_full_name = type.FullName ?? type.Name
        };

        if (type.Name == "VRCPhysBone")
        {
            AddVrcPhysBoneDetails(component, detail.details, root);
        }
        else if (type.Name == "ModularAvatarBlendshapeSync")
        {
            List<string> blendshapes = new List<string>();
            ExtractBlendshapeNames(component, blendshapes, new HashSet<object>());
            detail.details["blendshapes"] = blendshapes.Distinct().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        else if (IsModularAvatarShapeChanger(type))
        {
            detail.details["settings"] = CollectShapeChangerSettings(component, root);
        }

        return detail;
    }

    private void AddVrcPhysBoneDetails(Component component, Dictionary<string, object> details, Transform root)
    {
        details["root_transform"] = GetReferencedTransformPath(component, "rootTransform", root);

        List<string> ignoreTransforms = GetReferencedTransformListPaths(component, "ignoreTransforms", root);
        details["ignore_transform_count"] = ignoreTransforms.Count;
        details["ignore_transforms"] = ignoreTransforms;

        List<string> colliders = GetReferencedObjectListPaths(component, "colliders", root);
        if (colliders.Count == 0)
        {
            colliders = GetSerializedObjectReferencePaths(component, "colliders", root);
        }
        details["collider_count"] = colliders.Count;
        details["colliders"] = colliders;
    }

    private List<string> GetSerializedObjectReferencePaths(Component component, string propertyPathPrefix, Transform root)
    {
        List<string> result = new List<string>();
        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            if (!iterator.propertyPath.StartsWith(propertyPathPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (iterator.objectReferenceValue != null)
            {
                result.Add(FormatReferencedObject(iterator.objectReferenceValue, root));
            }
        }

        return result
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsModularAvatarShapeChanger(Type type)
    {
        string typeText = GetComponentTypeText(type);
        return typeText.IndexOf("ModularAvatar", StringComparison.OrdinalIgnoreCase) >= 0
            && typeText.IndexOf("ShapeChanger", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private List<Dictionary<string, string>> CollectShapeChangerSettings(Component component, Transform root)
    {
        List<Dictionary<string, string>> settings = new List<Dictionary<string, string>>();
        SerializedObject serializedObject = new SerializedObject(component);
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyPath == "m_Script")
            {
                continue;
            }

            if (!TryGetShapeChangerPropertyValue(iterator, root, out string value))
            {
                continue;
            }

            settings.Add(new Dictionary<string, string>
            {
                ["property_path"] = iterator.propertyPath,
                ["display_name"] = iterator.displayName,
                ["value"] = value
            });
        }

        return settings
            .OrderBy(item => item["property_path"], StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool TryGetShapeChangerPropertyValue(SerializedProperty property, Transform root, out string value)
    {
        value = "";
        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
                value = property.boolValue ? "true" : "false";
                return true;
            case SerializedPropertyType.Enum:
                value = property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString();
                return true;
            case SerializedPropertyType.String:
                if (string.IsNullOrEmpty(property.stringValue)) return false;
                value = property.stringValue;
                return true;
            case SerializedPropertyType.ObjectReference:
                if (property.objectReferenceValue == null) return false;
                value = FormatReferencedObject(property.objectReferenceValue, root);
                return true;
            default:
                return false;
        }
    }

    private string GetReferencedTransformPath(Component component, string memberName, Transform root)
    {
        object value = GetMemberValue(component, memberName);
        return value is Transform transform ? GetTransformPath(transform, root) : "";
    }

    private List<string> GetReferencedTransformListPaths(Component component, string memberName, Transform root)
    {
        return GetEnumerableMemberValues(component, memberName)
            .OfType<Transform>()
            .Select(transform => GetTransformPath(transform, root))
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> GetReferencedObjectListPaths(Component component, string memberName, Transform root)
    {
        return GetEnumerableMemberValues(component, memberName)
            .OfType<UnityEngine.Object>()
            .Where(obj => obj != null)
            .Select(obj => FormatReferencedObject(obj, root))
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<object> GetEnumerableMemberValues(object target, string memberName)
    {
        object value = GetMemberValue(target, memberName);
        if (value is IEnumerable enumerable && !(value is string))
        {
            foreach (object item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private object GetMemberValue(object target, string memberName)
    {
        if (target == null) return null;
        Type type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            try { return property.GetValue(target, null); } catch { return null; }
        }

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            try { return field.GetValue(target); } catch { return null; }
        }

        return null;
    }

    private string FormatReferencedObject(UnityEngine.Object obj, Transform root)
    {
        if (obj == null) return "";
        if (obj is Transform transform)
        {
            return GetTransformPath(transform, root);
        }

        if (obj is Component component)
        {
            return GetTransformPath(component.transform, root) + ":" + component.GetType().Name;
        }

        string assetPath = AssetDatabase.GetAssetPath(obj);
        return string.IsNullOrEmpty(assetPath) ? obj.name : assetPath;
    }

    private List<ComponentSummaryJson> BuildComponentSummary(IEnumerable<ComponentInstanceJson> components)
    {
        return components
            .GroupBy(component => new { component.type_name, component.type_full_name, component.category })
            .Select(group => new ComponentSummaryJson
            {
                type_name = group.Key.type_name,
                type_full_name = group.Key.type_full_name,
                category = group.Key.category,
                count = group.Count()
            })
            .OrderBy(item => item.category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.type_name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<ReferenceAuditItemJson> CollectComponentReferenceAuditItems(
        Component component,
        string prefabPath,
        string objectPath,
        int componentIndex,
        HashSet<string> packageAssetSet)
    {
        List<ReferenceAuditItemJson> items = new List<ReferenceAuditItemJson>();
        if (component == null || component is Transform)
        {
            return items;
        }

        SerializedObject serializedObject;
        try
        {
            serializedObject = new SerializedObject(component);
        }
        catch
        {
            return items;
        }

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            if (iterator.propertyPath == "m_Script")
            {
                continue;
            }

            UnityEngine.Object referencedObject = iterator.objectReferenceValue;
            string referencedAsset = referencedObject != null ? AssetDatabase.GetAssetPath(referencedObject) : "";
            string status = GetReferenceAuditStatus(iterator, referencedObject, referencedAsset, packageAssetSet);

            if (status == "included" || status == "scene_object" || status == "none" || status == "ignored_external")
            {
                continue;
            }

            items.Add(new ReferenceAuditItemJson
            {
                source_asset = prefabPath,
                source_object_path = objectPath,
                component_index = componentIndex,
                component_type = component.GetType().Name,
                property_path = iterator.propertyPath,
                referenced_asset = referencedAsset,
                referenced_type = referencedObject != null ? referencedObject.GetType().Name : "",
                referenced_object_name = referencedObject != null ? referencedObject.name : "",
                status = status
            });
        }

        return items;
    }

    private string GetReferenceAuditStatus(
        SerializedProperty property,
        UnityEngine.Object referencedObject,
        string referencedAsset,
        HashSet<string> packageAssetSet)
    {
        if (referencedObject == null)
        {
#if UNITY_6000_5_OR_NEWER
            return property.objectReferenceEntityIdValue != EntityId.None ? "missing" : "none";
#else
            return property.objectReferenceInstanceIDValue != 0 ? "missing" : "none";
#endif
        }

        if (string.IsNullOrEmpty(referencedAsset))
        {
            return "scene_object";
        }

        string normalizedAsset = referencedAsset.Replace('\\', '/');
        if (packageAssetSet.Contains(normalizedAsset))
        {
            return "included";
        }

        string referencedMeta = normalizedAsset + ".meta";
        if (packageAssetSet.Contains(referencedMeta))
        {
            return "included";
        }

        if (IsIgnoredExternalReference(referencedObject, normalizedAsset))
        {
            return "ignored_external";
        }

        if (normalizedAsset.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return "external_prefab";
        }

        return "needs_check";
    }

    private bool IsIgnoredExternalReference(UnityEngine.Object referencedObject, string referencedAsset)
    {
        if (referencedObject is Material || referencedObject is Texture || referencedObject is Texture2D || referencedObject is Shader || referencedObject is MonoScript)
        {
            return true;
        }

        string extension = Path.GetExtension(referencedAsset);
        return extension.Equals(".mat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".psd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exr", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hdr", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".shader", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private void PopulateReferenceAuditCounts(ReferenceAuditJson audit)
    {
        audit.items = audit.items
            .OrderBy(item => item.status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.source_asset, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.source_object_path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.property_path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        audit.external_reference_count = audit.items.Count(item => item.status == "external_prefab" || item.status == "needs_check");
        audit.ignored_reference_count = 0;
        audit.needs_check_count = audit.items.Count(item => item.status == "external_prefab" || item.status == "needs_check");
        audit.missing_reference_count = audit.items.Count(item => item.status == "missing");
    }

    private string BuildComponentSummaryText(IEnumerable<ComponentSummaryJson> componentSummary)
    {
        var rows = componentSummary
            .Select(item => new ComponentSummaryTextRow
            {
                category = item.category,
                label = GetSummaryComponentLabel(item),
                count = item.count
            })
            .GroupBy(item => new { item.category, item.label })
            .Select(group => new ComponentSummaryTextRow
            {
                category = group.Key.category,
                label = group.Key.label,
                count = group.Sum(item => item.count)
            })
            .ToList();

        StringBuilder builder = new StringBuilder();
        AppendSummaryGroup(builder, rows, "vrc_sdk", "VRC");
        AppendSummaryGroup(builder, rows, "modular_avatar", "MA");
        AppendSummaryGroup(builder, rows, "unity_standard", "Unity");
        AppendSummaryGroup(builder, rows, "other", "Other");
        return builder.ToString().TrimEnd();
    }

    private void AppendSummaryGroup(StringBuilder builder, IEnumerable<ComponentSummaryTextRow> rows, string category, string title)
    {
        var groupRows = rows
            .Where(item => item.category == category)
            .OrderBy(item => item.label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groupRows.Count == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(title + ":");
        foreach (var row in groupRows)
        {
            builder.AppendLine("+" + row.label + ":" + row.count);
        }
    }

    private string GetSummaryComponentLabel(ComponentSummaryJson summary)
    {
        string typeName = summary.type_name ?? "";
        if (summary.category == "modular_avatar" && typeName.StartsWith("ModularAvatar", StringComparison.Ordinal))
        {
            return typeName.Substring("ModularAvatar".Length);
        }

        if (summary.category == "vrc_sdk" && typeName.EndsWith("Constraint", StringComparison.Ordinal))
        {
            return "VRCConstraint";
        }

        return typeName;
    }
    
    /// <summary>
    /// Prefab内の対象コンポーネントをカウントする
    /// </summary>
    /// <param name="prefabPath">Prefabのアセットパス</param>
    /// <param name="componentCounts">コンポーネント名と数のDictionary</param>
    private void CountPrefabComponents(string prefabPath, Dictionary<string, int> componentCounts, bool includeOtherComponents)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            return;
        }

        // Prefab内の全GameObjectを再帰的に処理
        CountGameObjectComponents(prefab, componentCounts, includeOtherComponents);
    }

    // Prefab 内の ModularAvatarBlendshapeSync から設定済み Blendshape 名を取得
    private List<string> CollectPrefabBlendshapeNames(string prefabPath)
    {
        HashSet<string> result = new HashSet<string>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return result.ToList();

        foreach (Component comp in prefab.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;
            if (comp.GetType().Name == "ModularAvatarBlendshapeSync")
            {
                List<string> temp = new List<string>();
                ExtractBlendshapeNames(comp, temp, new HashSet<object>());
                foreach (var n in temp) result.Add(n);
            }
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
    
    /// <summary>
    /// GameObjectとその子オブジェクトの対象コンポーネントを再帰的にカウントする
    /// </summary>
    /// <param name="gameObject">対象のGameObject</param>
    /// <param name="componentCounts">コンポーネント名と数のDictionary</param>
    private void CountGameObjectComponents(GameObject gameObject, Dictionary<string, int> componentCounts, bool includeOtherComponents)
    {
        Component[] components = gameObject.GetComponents<Component>();
        
        foreach (Component component in components)
        {
            if (component == null) continue;
            
            string componentTypeName = component.GetType().Name;
            
            if (IsLogTargetComponent(component, includeOtherComponents))
            {
                if (componentCounts.ContainsKey(componentTypeName))
                {
                    componentCounts[componentTypeName]++;
                }
                else
                {
                    componentCounts[componentTypeName] = 1;
                }
            }

            // MergeAnimatorをチェック
            if (componentTypeName == "ModularAvatarMergeAnimator")
                {
                    if (HasMissingObjectReference(component))
                    {
                        Debug.LogWarning($"警告: ModularAvatarMergeAnimator に null または None のプロパティがあります。");
                        return;
                    }
            }
        }

        // 子オブジェクトを再帰的に処理
        foreach (Transform child in gameObject.transform)
        {
            CountGameObjectComponents(child.gameObject, componentCounts, includeOtherComponents);
        }
    }

    private struct SkinnedMeshBoundsLog
    {
        public string objectPath;
        public Vector3 localCenter;
        public Vector3 localSize;
        public Vector3 localExtents;
        public Vector3 worldCenter;
        public Vector3 worldSize;
        public Vector3 worldExtents;
    }

    private List<SkinnedMeshBoundsLog> CollectSkinnedMeshBounds(string prefabPath)
    {
        List<SkinnedMeshBoundsLog> result = new List<SkinnedMeshBoundsLog>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return result;

        foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Bounds localBounds = renderer.localBounds;
            Bounds worldBounds = renderer.bounds;
            result.Add(new SkinnedMeshBoundsLog
            {
                objectPath = GetTransformPath(renderer.transform, prefab.transform),
                localCenter = localBounds.center,
                localSize = localBounds.size,
                localExtents = localBounds.extents,
                worldCenter = worldBounds.center,
                worldSize = worldBounds.size,
                worldExtents = worldBounds.extents
            });
        }

        return result;
    }

    private List<SkinnedMeshBoundsJson> CollectSkinnedMeshBoundsJson(string prefabPath)
    {
        return CollectSkinnedMeshBounds(prefabPath)
            .Select(boundsLog => new SkinnedMeshBoundsJson
            {
                object_path = boundsLog.objectPath,
                local_bounds = CreateBoundsJson(boundsLog.localCenter, boundsLog.localSize, boundsLog.localExtents),
                world_bounds = CreateBoundsJson(boundsLog.worldCenter, boundsLog.worldSize, boundsLog.worldExtents)
            })
            .OrderBy(item => item.object_path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private BoundsJson CreateBoundsJson(Vector3 center, Vector3 size, Vector3 extents)
    {
        return new BoundsJson
        {
            center = CreateVector3Json(center),
            size = CreateVector3Json(size),
            extents = CreateVector3Json(extents)
        };
    }

    private Vector3Json CreateVector3Json(Vector3 value)
    {
        return new Vector3Json
        {
            x = value.x,
            y = value.y,
            z = value.z
        };
    }

    private string GetTransformPath(Transform target, Transform root)
    {
        if (target == null) return "";
        if (target == root) return target.name;

        List<string> names = new List<string>();
        Transform current = target;
        while (current != null)
        {
            names.Add(current.name);
            if (current == root) break;
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private string FormatVector3(Vector3 value)
    {
        return $"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}";
    }

    private int GetTransformDepth(Transform target, Transform root)
    {
        int depth = 0;
        Transform current = target;
        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }

        return depth;
    }

    private string GetComponentCategory(Component component)
    {
        if (IsVrcSdkComponent(component))
        {
            return "vrc_sdk";
        }

        if (IsModularAvatarComponent(component))
        {
            return "modular_avatar";
        }

        if (IsUnityStandardSummaryComponent(component))
        {
            return "unity_standard";
        }

        return "other";
    }

    private bool IsLogTargetComponent(Component component, bool includeOtherComponents)
    {
        if (component == null || component is Transform)
        {
            return false;
        }

        if (IsVrcSdkComponent(component) || IsModularAvatarComponent(component) || IsUnityStandardSummaryComponent(component))
        {
            return true;
        }

        return includeOtherComponents;
    }

    private bool IsVrcSdkComponent(Component component)
    {
        Type type = component.GetType();
        string typeText = GetComponentTypeText(type);
        return type.Name.StartsWith("VRC", StringComparison.Ordinal)
            || typeText.IndexOf("VRC.SDK", StringComparison.OrdinalIgnoreCase) >= 0
            || typeText.IndexOf("VRCSDK", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsModularAvatarComponent(Component component)
    {
        Type type = component.GetType();
        string typeText = GetComponentTypeText(type);
        return type.Name.StartsWith("ModularAvatar", StringComparison.Ordinal)
            || typeText.IndexOf("ModularAvatar", StringComparison.OrdinalIgnoreCase) >= 0
            || typeText.IndexOf("nadena.dev.modular-avatar", StringComparison.OrdinalIgnoreCase) >= 0
            || typeText.IndexOf("nadena.dev.modular_avatar", StringComparison.OrdinalIgnoreCase) >= 0
            || typeText.IndexOf("modular_avatar", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string GetComponentTypeText(Type type)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(type.FullName ?? type.Name);
        builder.Append('|');
        builder.Append(type.Namespace ?? "");
        builder.Append('|');
        builder.Append(type.Assembly.GetName().Name ?? "");
        return builder.ToString();
    }

    private bool IsUnityStandardSummaryComponent(Component component)
    {
        Type type = component.GetType();
        return component is Collider
            || component is Collider2D
            || component is Rigidbody
            || component is Rigidbody2D
            || component is Joint
            || component is Joint2D
            || component is Cloth
            || component is SkinnedMeshRenderer
            || component is MeshRenderer
            || component is MeshFilter
            || component is Animator
            || component is Animation
            || type.Name.EndsWith("Constraint", StringComparison.Ordinal);
    }

    // ModularAvatarBlendshapeSync の設定ブレンドシェイプをログに記録
    private void LogBlendshapeNames(Component component, StreamWriter writer, string indent)
    {
        List<string> names = new List<string>();
        ExtractBlendshapeNames(component, names, new HashSet<object>());
        if (names.Count > 0)
        {
            writer.WriteLine($"{indent}- ModularAvatarBlendshapeSync:");
            foreach (string n in names)
            {
                writer.WriteLine($"{indent}  - {n}");
            }
        }
    }

    // オブジェクトから再帰的にblendshape名を抽出する
    private void ExtractBlendshapeNames(object obj, List<string> names, HashSet<object> visited)
    {
        if (obj == null || visited.Contains(obj)) return;
        visited.Add(obj);

        if (obj is string str)
        {
            if (!string.IsNullOrEmpty(str)) names.Add(str);
            return;
        }

        Type t = obj.GetType();

        if (typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string))
        {
            foreach (var item in (IEnumerable)obj)
            {
                ExtractBlendshapeNames(item, names, visited);
            }
            return;
        }

        foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            object value;
            try { value = field.GetValue(obj); } catch { continue; }

            if (field.FieldType == typeof(string) && field.Name.ToLower().Contains("blendshape"))
            {
                if (value is string s && !string.IsNullOrEmpty(s)) names.Add(s);
            }
            else if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string))
            {
                ExtractBlendshapeNames(value, names, visited);
            }
        }

        foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            object value;
            try { value = prop.GetValue(obj, null); } catch { continue; }

            if (prop.PropertyType == typeof(string) && prop.Name.ToLower().Contains("blendshape"))
            {
                if (value is string s && !string.IsNullOrEmpty(s)) names.Add(s);
            }
            else if (!prop.PropertyType.IsPrimitive && prop.PropertyType != typeof(string))
            {
                ExtractBlendshapeNames(value, names, visited);
            }
        }
    }

    /// <summary>
    /// Missing ObjectReference を総当たりで検出
    /// </summary>
    bool HasMissingObjectReference(Component comp)
    {
    #if UNITY_EDITOR
        var so = new SerializedObject(comp);
        var prop = so.GetIterator();
        bool enterChildren = true;

        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            // ObjectReferenceがnullまたはNoneの場合
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            if (prop.objectReferenceValue == null)
                return true;
        }
        
    #endif
        return false;
    }


    /// <summary>
    /// GameObjectとその子オブジェクトのコンポーネント情報を再帰的にログに記録する（Markdown形式）
    /// 対象コンポーネントを持つGameObjectのみを記録する
    /// </summary>
    /// <param name="gameObject">対象のGameObject</param>
    /// <param name="writer">ログファイルのStreamWriter</param>
    /// <param name="indent">インデント（階層表示用）</param>
    /// <returns>対象コンポーネントを持つGameObjectが見つかった場合はtrue</returns>
    private bool LogGameObjectComponents(GameObject gameObject, StreamWriter writer, string indent)
    {
        // このGameObjectのコンポーネントを取得
        Component[] components = gameObject.GetComponents<Component>();
        string componentIndent = indent + "- ";
        string propertyIndent = indent + "  - ";
        
        bool hasTargetComponents = false;
        
        // このGameObjectが対象コンポーネントを持っているか確認
        // VRC関連ととりあえずモジュラーアバター関連のコンポーネントを対象にする


        foreach (Component component in components)
        {
            if (component == null) continue;
            
            string componentTypeName = component.GetType().Name;
            
            if (componentTypeName == "VRCPhysBone" || 
                componentTypeName == "VRCPhysBoneCollider" || 
                componentTypeName.Contains("VRC")|| 
                componentTypeName.Contains("ModularAvatar"))
            {
                hasTargetComponents = true;
                break;
            }
        }
        
        // 子オブジェクトに対象コンポーネントがあるか再帰的に確認
        bool childrenHaveTargetComponents = false;
        List<Transform> childrenWithTargetComponents = new List<Transform>();
        
        foreach (Transform child in gameObject.transform)
        {
            if (LogGameObjectComponents(child.gameObject, null, indent + "  "))
            {
                childrenHaveTargetComponents = true;
                childrenWithTargetComponents.Add(child);
            }
        }
        
        // このGameObjectまたは子オブジェクトに対象コンポーネントがある場合のみログに記録
        if (hasTargetComponents || childrenHaveTargetComponents)
        {
            // 実際にログに書き込む場合（writerがnullでない場合）
            if (writer != null)
            {
                writer.WriteLine($"{indent}#### GameObject: {gameObject.name}");
                
                // このGameObjectのコンポーネントを記録
                if (hasTargetComponents)
                {
                    foreach (Component component in components)
                    {
                        if (component == null)
                        {
                            writer.WriteLine($"{componentIndent}[Missing Component]");
                            writer.WriteLine();
                            continue;
                        }
                        
                        string componentTypeName = component.GetType().Name;
                        
                        if (componentTypeName == "VRCPhysBone" || 
                            componentTypeName == "VRCPhysBoneCollider" || 
                            componentTypeName.Contains("VRC")|| 
                            componentTypeName.Contains("ModularAvatar"))
                        {
                            writer.WriteLine($"{componentIndent}**Component**: {componentTypeName}");
                            LogComponentProperties(component, writer, propertyIndent);
                            if (componentTypeName == "ModularAvatarBlendshapeSync")
                            {
                                LogBlendshapeNames(component, writer, propertyIndent);
                            }
                            writer.WriteLine();
                        }
                        
                    }
                }
                else
                {
                    // 自身には対象コンポーネントがないが、子に対象コンポーネントがある場合
                    writer.WriteLine($"{componentIndent}*[このGameObjectには対象コンポーネントなし、子オブジェクトに存在]*");
                }
                
                // 対象コンポーネントを持つ子オブジェクトを再帰的に処理
                foreach (Transform child in childrenWithTargetComponents)
                {
                    LogGameObjectComponents(child.gameObject, writer, indent + "  ");
                }
            }
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Prefabのコンポーネント情報をログに記録する（Markdown形式）
    /// 対象コンポーネントを持つGameObjectのみを記録する
    /// </summary>
    /// <param name="prefabPath">Prefabのアセットパス</param>
    /// <param name="writer">ログファイルのStreamWriter</param>
    private void LogPrefabComponents(string prefabPath, StreamWriter writer)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            return;
        }
        
        writer.WriteLine($"### Prefab: {prefabPath}");
        writer.WriteLine();
        
        // Prefab内の対象コンポーネントを持つGameObjectのみを再帰的に処理
        bool hasPrefabTargetComponents = LogGameObjectComponents(prefab, writer, "");
        
        // 対象コンポーネントが見つからなかった場合はその旨を記録
        if (!hasPrefabTargetComponents)
        {
            writer.WriteLine("*[このPrefabには対象コンポーネントが見つかりませんでした]*");
        }
        
        writer.WriteLine();
    }
    
    // 再帰的にコンポーネント値をダンプするヘルパー　配列の入れ子とかにも対応できそう

    private static void DumpValue(object obj, TextWriter writer, string indent)
    {
        if (obj == null)                { writer.WriteLine($"{indent}null"); return; }
        if (obj is string s)            { writer.WriteLine($"{indent}\"{s}\""); return; }

        if (obj is System.Collections.IDictionary dict)
        {
            if (dict.Count == 0)
            {
                writer.WriteLine($"{indent}(empty dictionary)");
                return;
            }

            foreach (System.Collections.DictionaryEntry de in dict)
            {
                // キー表示
                writer.WriteLine($"{indent}[Key] ");
                DumpValue(de.Key,   writer, indent + "  ");

                // 値表示
                writer.WriteLine($"{indent}[Value]");
                DumpValue(de.Value, writer, indent + "  ");
            }
            return;
        }


        // IEnumerable (配列・List<T> など)
        if (obj is System.Collections.IEnumerable enumerable)
        {
            int idx = 0;
            foreach (var item in enumerable)
            {
                
                DumpValue(item, writer, indent + "  ");
                idx++;
            }
            if (idx == 0) writer.WriteLine($"{indent}(empty)");
            return;
        }

        // それ以外の通常型
        writer.WriteLine($"{indent}{obj.GetType().Name}: {obj}");
        foreach (PropertyInfo property in obj.GetType().GetProperties())
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    object value = property.GetValue(obj, null);
                    writer.WriteLine($"{indent}  {property.Name}: {value}");
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"{indent}  {property.Name}: *[取得エラー]* - {ex.Message}");
                }
            }
        }

    }

    /// <summary>
    /// コンポーネントのプロパティ値をログに記録する（Markdown形式）
    /// </summary>
    /// <param name="component">対象のコンポーネント</param>
    /// <param name="writer">ログファイルのStreamWriter</param>
    /// <param name="indent">インデント（階層表示用）</param>
    private void LogComponentProperties(Component component, StreamWriter writer, string indent)
    {
        Type type = component.GetType();
        
        // パブリックフィールドを取得
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            try
            {
                object value = field.GetValue(component);
                string valueStr = value != null ? value.ToString() : "null";
                writer.WriteLine($"{indent}`{field.Name}`: {valueStr}");
                DumpValue(value, writer, indent + "  ");
            }
            catch (Exception)
            {
                writer.WriteLine($"{indent}`{field.Name}`: *[取得エラー]*");
            }
        }


        // パブリックプロパティを取得（get アクセサーがあるもののみ）
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (PropertyInfo property in properties)
        {
            // getアクセサーがあり、インデクサーでなく、パラメータが不要なプロパティのみ処理
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    object value = property.GetValue(component, null);
                    string valueStr = value != null ? value.ToString() : "null";
                    
                    // 一般的な基本型またはUnityの基本型のみ記録
                    if (IsSimpleType(property.PropertyType))
                    {
                        writer.WriteLine($"{indent}`{property.Name}`: {valueStr}");
                    }
                }
                catch (Exception)
                {
                    // エラーが発生した場合は記録しない（多くの場合、実行時のみアクセス可能なプロパティ）
                }
            }
        }
    }

    /// <summary>
    /// 除外キーワードの正規表現を構築
    /// </summary>
    private Regex BuildExclusionRegex()
    {
        var keywords = PackageExporterSettings.instance.exclusionKeywords;
        string pattern = string.Join("|", keywords.Select(Regex.Escape));
        return new Regex(pattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// フォルダ内に除外キーワードを含むアセットが存在するか
    /// </summary>
    private bool FolderContainsExclusionKeyword(string folder, Regex exclusionRegex)
    {
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (exclusionRegex.IsMatch(assetPath))
                return true;
        }
        return false;
    }
    


    /// <summary>
    /// 指定された型が単純型（基本型またはUnityの基本型）かどうかを判定
    /// </summary>
    /// <param name="type">チェックする型</param>
    /// <returns>単純型の場合はtrue</returns>
    private bool IsSimpleType(Type type)
    {
        return type.IsPrimitive 
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Vector2)
            || type == typeof(Vector3)
            || type == typeof(Vector4)
            || type == typeof(Quaternion)
            || type == typeof(Color)
            || type == typeof(Rect)
            || type == typeof(Bounds)
            || type == typeof(Matrix4x4)
            || type.IsEnum;
    }
}
}
