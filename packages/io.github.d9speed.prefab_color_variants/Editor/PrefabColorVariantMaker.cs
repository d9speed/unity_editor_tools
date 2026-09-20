using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine.UIElements;

public class PrefabColorVariantMaker : EditorWindow
{
    private const int OriginalMaterialColumnWidth = 220;
    private const int ReplacementMaterialColumnWidth = 180;

    // 複数の元プレハブを指定するリスト
    [SerializeField]
    private List<GameObject> originalPrefabs = new List<GameObject>();
    // 生成するバリアント列
    [SerializeField]
    private List<VariantColumn> variantColumns = new List<VariantColumn>()
    {
        new VariantColumn()
    };
    // ユニークマテリアルの置換設定
    [SerializeField]
    private List<MaterialMapping> materialMappings = new List<MaterialMapping>();

    private VisualElement prefabListRoot;
    private VisualElement variantListRoot;
    private VisualElement materialMappingRoot;

    // マッピング用クラス
    [System.Serializable]
    public class MaterialMapping
    {
        public Material original;
        public List<Material> replacements = new List<Material>();
    }

    [System.Serializable]
    public class VariantColumn
    {
        public string suffix = "Color";
        public DefaultAsset materialFolder;
    }

    [MenuItem("D9speed/Tools/PrefabColorVariantMaker")]
    public static void ShowWindow()
    {
        GetWindow<PrefabColorVariantMaker>("PrefabColorVariantMaker");
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += RefreshAllSections;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= RefreshAllSections;
    }

    private void CreateGUI()
    {
        CreateUI(rootVisualElement);
    }

    public void CreateUI(VisualElement root)
    {
        EnsureVariantColumns();
        EnsureReplacementSlots();

        root.Clear();
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingTop = 8;
        root.style.paddingBottom = 8;
        D9speedEditorFontUtility.Apply(root);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        root.Add(scroll);

        AddHeader(scroll, "Prefab Color Variant Maker");
        AddPrefabSection(scroll);
        AddVariantSection(scroll);
        AddMaterialSection(scroll);

        var createButton = new Button(() =>
        {
            CreateVariants();
            RefreshAllSections();
        })
        { text = "プレハブバリアントを作成" };
        createButton.style.marginTop = 10;
        scroll.Add(createButton);
    }

    private void AddHeader(VisualElement parent, string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 6;
        parent.Add(label);
    }

    private void AddPrefabSection(VisualElement parent)
    {
        AddHeader(parent, "元プレハブの指定");
        parent.Add(new HelpBox("複数のプレハブをここにドラッグ＆ドロップできます", HelpBoxMessageType.Info));

        var dropArea = new Label("ここにプレハブをドラッグ＆ドロップ");
        dropArea.style.height = 50;
        dropArea.style.unityTextAlign = TextAnchor.MiddleCenter;
        dropArea.style.borderBottomWidth = 1;
        dropArea.style.borderLeftWidth = 1;
        dropArea.style.borderRightWidth = 1;
        dropArea.style.borderTopWidth = 1;
        dropArea.style.marginBottom = 8;
        RegisterPrefabDropArea(dropArea);
        parent.Add(dropArea);

        AddHeader(parent, "登録済みプレハブ一覧");
        prefabListRoot = new VisualElement();
        parent.Add(prefabListRoot);

        var addButton = new Button(() =>
        {
            RecordWindowState("Add Prefab Slot");
            originalPrefabs.Add(null);
            RefreshPrefabList();
        })
        { text = "プレハブを追加" };
        parent.Add(addButton);

        RefreshPrefabList();
    }

    private void AddVariantSection(VisualElement parent)
    {
        AddHeader(parent, "バリアント設定");
        parent.Add(new HelpBox("列ごとにプレハブ名の後置詞と置換マテリアルを設定します。フォルダー指定後に自動割り当てを押すと、元マテリアル名から近い名前のマテリアルを探します。", HelpBoxMessageType.Info));

        variantListRoot = new VisualElement();
        parent.Add(variantListRoot);

        var row = CreateRow();
        row.style.marginBottom = 8;
        row.Add(new Button(() =>
        {
            AddVariantColumn();
            RefreshVariantList();
            RefreshMaterialMappings();
        })
        { text = "バリアント列を追加" });
        row.Add(new Button(() =>
        {
            AssignAllReplacementsFromFolders();
            RefreshMaterialMappings();
        })
        { text = "全列を自動割り当て" });
        parent.Add(row);

        var scanButton = new Button(() =>
        {
            ScanUniqueMaterials();
            RefreshMaterialMappings();
        })
        { text = "ユニークマテリアルを抽出" };
        parent.Add(scanButton);

        RefreshVariantList();
    }

    private void AddMaterialSection(VisualElement parent)
    {
        materialMappingRoot = new VisualElement();
        materialMappingRoot.style.marginTop = 10;
        parent.Add(materialMappingRoot);
        RefreshMaterialMappings();
    }

    private void RegisterPrefabDropArea(VisualElement dropArea)
    {
        dropArea.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        });
        dropArea.RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();
            AddDraggedPrefabs(DragAndDrop.objectReferences);
            evt.StopPropagation();
        });
    }

    private void AddDraggedPrefabs(IEnumerable<Object> draggedObjects)
    {
        bool changed = false;
        foreach (var draggedObject in draggedObjects)
        {
            var prefab = draggedObject as GameObject;
            if (prefab == null) continue;
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            if (!assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (originalPrefabs.Contains(prefab)) continue;

            if (!changed)
            {
                RecordWindowState("Add Dragged Prefabs");
                changed = true;
            }
            originalPrefabs.Add(prefab);
        }

        if (changed)
        {
            RefreshPrefabList();
        }
    }

    private void RefreshAllSections()
    {
        EnsureVariantColumns();
        EnsureReplacementSlots();
        RefreshPrefabList();
        RefreshVariantList();
        RefreshMaterialMappings();
    }

    private void RefreshPrefabList()
    {
        if (prefabListRoot == null) return;
        prefabListRoot.Clear();

        for (int i = 0; i < originalPrefabs.Count; i++)
        {
            int index = i;
            var row = CreateRow();
            var prefabField = new ObjectField("Prefab " + (index + 1))
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = originalPrefabs[index],
                style = { flexGrow = 1 }
            };
            prefabField.style.flexShrink = 1;
            prefabField.RegisterValueChangedCallback(evt =>
            {
                RecordWindowState("Change Prefab");
                originalPrefabs[index] = evt.newValue as GameObject;
                RefreshPrefabList();
            });

            var removeButton = new Button(() =>
            {
                RecordWindowState("Remove Prefab");
                originalPrefabs.RemoveAt(index);
                RefreshPrefabList();
            })
            { text = "削除" };
            removeButton.style.width = 60;
            removeButton.style.flexShrink = 0;

            row.Add(prefabField);
            row.Add(removeButton);
            prefabListRoot.Add(row);

            if (originalPrefabs[index] == null) continue;
            string assetPath = AssetDatabase.GetAssetPath(originalPrefabs[index]);
            if (assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                prefabListRoot.Add(new HelpBox("FBXファイルが指定されています。プレハブを指定してください。", HelpBoxMessageType.Warning));
            }
        }
    }

    private void RefreshVariantList()
    {
        if (variantListRoot == null) return;
        EnsureVariantColumns();
        variantListRoot.Clear();

        for (int i = 0; i < variantColumns.Count; i++)
        {
            int index = i;
            var columnRoot = new VisualElement();
            columnRoot.style.marginBottom = 8;

            var suffixField = new TextField("後置詞 " + (index + 1))
            {
                value = variantColumns[index].suffix,
                style = { minWidth = 180, flexGrow = 1, flexShrink = 1 }
            };
            suffixField.RegisterValueChangedCallback(evt =>
            {
                RecordWindowState("Change Variant Suffix");
                variantColumns[index].suffix = evt.newValue;
                RefreshMaterialMappings();
            });

            var folderField = new ObjectField("素材フォルダー " + (index + 1))
            {
                objectType = typeof(DefaultAsset),
                allowSceneObjects = false,
                value = variantColumns[index].materialFolder,
                style = { minWidth = 240, flexGrow = 1, flexShrink = 1 }
            };
            folderField.tooltip = "この列の置換マテリアルを探すフォルダーを指定します。";
            folderField.RegisterValueChangedCallback(evt =>
            {
                RecordWindowState("Change Material Folder");
                variantColumns[index].materialFolder = evt.newValue as DefaultAsset;
            });

            var assignButton = new Button(() =>
            {
                AssignReplacementsFromFolder(index);
                RefreshMaterialMappings();
            })
            { text = "自動割り当て" };
            assignButton.style.width = 90;
            assignButton.style.flexShrink = 0;

            var removeButton = new Button(() =>
            {
                RemoveVariantColumn(index);
                RefreshVariantList();
                RefreshMaterialMappings();
            })
            { text = "列削除" };
            removeButton.style.width = 60;
            removeButton.style.flexShrink = 0;
            removeButton.SetEnabled(variantColumns.Count > 1);

            var fieldRow = CreateRow();
            fieldRow.Add(suffixField);
            fieldRow.Add(folderField);

            var buttonRow = CreateRow();
            buttonRow.Add(assignButton);
            buttonRow.Add(removeButton);

            columnRoot.Add(fieldRow);
            columnRoot.Add(buttonRow);
            variantListRoot.Add(columnRoot);
        }
    }

    private void RefreshMaterialMappings()
    {
        if (materialMappingRoot == null) return;
        EnsureReplacementSlots();
        materialMappingRoot.Clear();

        if (materialMappings.Count == 0)
        {
            return;
        }

        AddHeader(materialMappingRoot, "マテリアル置換設定　Noneの場合、元のマテリアルを保持");

        var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        scroll.style.minHeight = 120;
        scroll.style.maxHeight = 420;
        materialMappingRoot.Add(scroll);

        scroll.Add(CreateMaterialHeaderRow());

        for (int i = 0; i < materialMappings.Count; i++)
        {
            int mappingIndex = i;
            var row = CreateRow();

            var originalField = new ObjectField
            {
                objectType = typeof(Material),
                allowSceneObjects = false,
                value = materialMappings[mappingIndex].original
            };
            originalField.style.width = OriginalMaterialColumnWidth;
            originalField.RegisterValueChangedCallback(evt =>
            {
                RecordWindowState("Change Original Material");
                materialMappings[mappingIndex].original = evt.newValue as Material;
            });
            row.Add(originalField);

            for (int variantIndex = 0; variantIndex < variantColumns.Count; variantIndex++)
            {
                int replacementIndex = variantIndex;
                var replacementField = new ObjectField
                {
                    objectType = typeof(Material),
                    allowSceneObjects = false,
                    value = materialMappings[mappingIndex].replacements[replacementIndex]
                };
                replacementField.style.width = ReplacementMaterialColumnWidth;
                replacementField.RegisterValueChangedCallback(evt =>
                {
                    RecordWindowState("Change Replacement Material");
                    EnsureReplacementSlots(materialMappings[mappingIndex]);
                    materialMappings[mappingIndex].replacements[replacementIndex] = evt.newValue as Material;
                });
                row.Add(replacementField);
            }

            scroll.Add(row);
        }
    }

    private VisualElement CreateMaterialHeaderRow()
    {
        var row = CreateRow();
        var originalLabel = CreateColumnLabel("元マテリアル", OriginalMaterialColumnWidth);
        row.Add(originalLabel);

        for (int variantIndex = 0; variantIndex < variantColumns.Count; variantIndex++)
        {
            string label = string.IsNullOrEmpty(variantColumns[variantIndex].suffix)
                ? "置換 " + (variantIndex + 1)
                : variantColumns[variantIndex].suffix;
            row.Add(CreateColumnLabel(label, ReplacementMaterialColumnWidth));
        }

        return row;
    }

    private Label CreateColumnLabel(string text, int width)
    {
        var label = new Label(text);
        label.style.width = width;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private VisualElement CreateRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;
        return row;
    }

    private void RecordWindowState(string undoName)
    {
        Undo.RecordObject(this, undoName);
    }

    // 指定された全プレハブ内のSkinnedMeshRenderer／MeshRendererからユニークなマテリアルを抽出
    private void ScanUniqueMaterials()
    {
        Undo.RecordObject(this, "Scan Unique Materials");
        materialMappings.Clear();
        HashSet<Material> uniqueMaterials = new HashSet<Material>();

        foreach (var prefab in originalPrefabs)
        {
            if (prefab == null) continue;
            // プレハブ内のSkinnedMeshRendererとMeshRendererを取得
            var skinRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in skinRenderers)
            {
                if (renderer.sharedMaterials != null)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                            uniqueMaterials.Add(mat);
                    }
                }
            }
            foreach (var renderer in meshRenderers)
            {
                if (renderer.sharedMaterials != null)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                            uniqueMaterials.Add(mat);
                    }
                }
            }
        }

        foreach (var mat in uniqueMaterials)
        {
            MaterialMapping mapping = new MaterialMapping();
            mapping.original = mat;
            EnsureReplacementSlots(mapping);
            materialMappings.Add(mapping);
        }
        EditorUtility.DisplayDialog("完了", "ユニークなマテリアル " + uniqueMaterials.Count + " 件を抽出しました。", "OK");
    }

    private void AddVariantColumn()
    {
        Undo.RecordObject(this, "Add Variant Column");
        variantColumns.Add(new VariantColumn());
        EnsureReplacementSlots();
    }

    private void RemoveVariantColumn(int index)
    {
        if (index < 0 || index >= variantColumns.Count || variantColumns.Count <= 1) return;

        Undo.RecordObject(this, "Remove Variant Column");
        variantColumns.RemoveAt(index);

        foreach (var mapping in materialMappings)
        {
            if (mapping.replacements != null && index < mapping.replacements.Count)
            {
                mapping.replacements.RemoveAt(index);
            }
        }

        EnsureReplacementSlots();
    }

    private void EnsureVariantColumns()
    {
        if (variantColumns == null)
        {
            variantColumns = new List<VariantColumn>();
        }

        if (variantColumns.Count == 0)
        {
            variantColumns.Add(new VariantColumn());
        }

        for (int i = 0; i < variantColumns.Count; i++)
        {
            if (variantColumns[i] == null)
            {
                variantColumns[i] = new VariantColumn();
            }
        }
    }

    private void EnsureReplacementSlots()
    {
        foreach (var mapping in materialMappings)
        {
            EnsureReplacementSlots(mapping);
        }
    }

    private void EnsureReplacementSlots(MaterialMapping mapping)
    {
        if (mapping.replacements == null)
        {
            mapping.replacements = new List<Material>();
        }

        while (mapping.replacements.Count < variantColumns.Count)
        {
            mapping.replacements.Add(null);
        }

        while (mapping.replacements.Count > variantColumns.Count)
        {
            mapping.replacements.RemoveAt(mapping.replacements.Count - 1);
        }
    }

    private void AssignAllReplacementsFromFolders()
    {
        for (int i = 0; i < variantColumns.Count; i++)
        {
            AssignReplacementsFromFolder(i, false);
        }

        EditorUtility.DisplayDialog("完了", "フォルダー指定済みの列にマテリアルを自動割り当てしました。", "OK");
    }

    private void AssignReplacementsFromFolder(int variantIndex, bool showDialog = true)
    {
        if (variantIndex < 0 || variantIndex >= variantColumns.Count) return;

        string folderPath = GetFolderPath(variantColumns[variantIndex].materialFolder);
        if (string.IsNullOrEmpty(folderPath))
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog("エラー", "マテリアルが格納されているフォルダーを指定してください。", "OK");
            }
            return;
        }

        Material[] folderMaterials = LoadMaterialsInFolder(folderPath);
        if (folderMaterials.Length == 0)
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog("エラー", "指定フォルダー内にマテリアルが見つかりませんでした。", "OK");
            }
            return;
        }

        Undo.RecordObject(this, "Assign Replacement Materials");
        int assignedCount = 0;
        string suffix = variantColumns[variantIndex].suffix;

        foreach (var mapping in materialMappings)
        {
            if (mapping.original == null) continue;
            Material replacement = FindBestReplacementMaterial(mapping.original.name, suffix, folderMaterials);
            if (replacement == null) continue;

            EnsureReplacementSlots(mapping);
            mapping.replacements[variantIndex] = replacement;
            assignedCount++;
        }

        if (showDialog)
        {
            EditorUtility.DisplayDialog("完了", assignedCount + " 件のマテリアルを自動割り当てしました。", "OK");
        }
    }

    private string GetFolderPath(DefaultAsset folder)
    {
        if (folder == null) return null;

        string path = AssetDatabase.GetAssetPath(folder);
        if (AssetDatabase.IsValidFolder(path))
        {
            return path;
        }

        return null;
    }

    private Material[] LoadMaterialsInFolder(string folderPath)
    {
        return AssetDatabase.FindAssets("t:Material", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<Material>(path))
            .Where(material => material != null)
            .OrderBy(material => material.name)
            .ToArray();
    }

    private Material FindBestReplacementMaterial(string originalName, string suffix, Material[] folderMaterials)
    {
        List<string> candidates = BuildMaterialNameCandidates(originalName, suffix);

        foreach (string candidate in candidates)
        {
            Material exactMatch = folderMaterials.FirstOrDefault(material => string.Equals(material.name, candidate, System.StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch;
            }
        }

        string normalizedOriginal = NormalizeMaterialName(originalName);
        string normalizedSuffix = NormalizeMaterialName(suffix);
        List<string> normalizedCandidates = candidates.Select(NormalizeMaterialName).ToList();

        foreach (var material in folderMaterials)
        {
            string normalizedMaterialName = NormalizeMaterialName(material.name);
            if (normalizedCandidates.Contains(normalizedMaterialName))
            {
                return material;
            }
        }

        foreach (var material in folderMaterials)
        {
            string normalizedMaterialName = NormalizeMaterialName(material.name);
            bool containsOriginal = normalizedMaterialName.Contains(normalizedOriginal);
            bool containsSuffix = string.IsNullOrEmpty(normalizedSuffix) || normalizedMaterialName.Contains(normalizedSuffix);

            if (containsOriginal && containsSuffix)
            {
                return material;
            }
        }

        return null;
    }

    private List<string> BuildMaterialNameCandidates(string originalName, string suffix)
    {
        List<string> candidates = new List<string>();
        if (string.IsNullOrEmpty(originalName))
        {
            return candidates;
        }

        if (!string.IsNullOrEmpty(suffix))
        {
            candidates.Add(originalName + suffix);
            candidates.Add(originalName + "_" + suffix);
            candidates.Add(originalName + "-" + suffix);
            candidates.Add(originalName + " " + suffix);
        }

        candidates.Add(originalName);
        return candidates.Distinct().ToList();
    }

    private string NormalizeMaterialName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        char[] chars = name
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray();
        return new string(chars);
    }

    // 各元プレハブからバリアントを生成し、マテリアルの置換を適用して保存
    private void CreateVariants()
    {
        if (originalPrefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "元プレハブが指定されていません。", "OK");
            return;
        }

        List<int> validVariantIndexes = Enumerable.Range(0, variantColumns.Count)
            .Where(index => variantColumns[index] != null && !string.IsNullOrEmpty(variantColumns[index].suffix))
            .ToList();

        if (validVariantIndexes.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "バリアントの後置詞が空です。", "OK");
            return;
        }

        int targetPrefabCount = originalPrefabs.Count(prefab => prefab != null);
        int createCount = targetPrefabCount * validVariantIndexes.Count;
        if (createCount == 0)
        {
            EditorUtility.DisplayDialog("エラー", "有効な元プレハブが指定されていません。", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("作成確認", createCount + " 件のプレハブバリアントを作成します。続行しますか？", "Yes", "No"))
        {
            return;
        }

        foreach (var prefab in originalPrefabs)
        {
            if (prefab == null) continue;
            // プレハブのパスを取得
            string originalPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogWarning("AssetDatabase上に存在しないプレハブです: " + prefab.name);
                continue;
            }

            foreach (int variantIndex in validVariantIndexes)
            {
                CreateVariant(prefab, originalPath, variantIndex);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完了", "プレハブバリアントの作成が完了しました。", "OK");
    }

    private void CreateVariant(GameObject prefab, string originalPath, int variantIndex)
    {
        // 新規パス生成：元ファイル名に後置詞を付与
        string directory = System.IO.Path.GetDirectoryName(originalPath);
        string fileName = System.IO.Path.GetFileNameWithoutExtension(originalPath);
        string newName = fileName + variantColumns[variantIndex].suffix;
        string newPath = System.IO.Path.Combine(directory, newName + ".prefab").Replace("\\", "/");

        // 上書き確認（既に同名ファイルがある場合）
        if (AssetDatabase.LoadAssetAtPath<GameObject>(newPath) != null)
        {
            if (!EditorUtility.DisplayDialog("上書き確認", "プレハブバリアント " + newName + " は既に存在します。上書きしますか？", "Yes", "No"))
            {
                return;
            }
        }

        // インスタンス生成し、マテリアルの置換を実施
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null)
        {
            Debug.LogError("プレハブのインスタンス生成に失敗しました: " + prefab.name);
            return;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Prefab Color Variant");

        try
        {
            ReplaceMaterials(instance, variantIndex);

            // プレハブバリアントとして保存
            PrefabUtility.SaveAsPrefabAsset(instance, newPath);
            Debug.Log("作成済み: " + newName);
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private void ReplaceMaterials(GameObject instance, int variantIndex)
    {
        Dictionary<Material, Material> replacementMap = BuildReplacementMap(variantIndex);

        foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            ReplaceRendererMaterials(renderer, replacementMap);
        }

        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            ReplaceRendererMaterials(renderer, replacementMap);
        }
    }

    private Dictionary<Material, Material> BuildReplacementMap(int variantIndex)
    {
        Dictionary<Material, Material> replacementMap = new Dictionary<Material, Material>();

        foreach (var mapping in materialMappings)
        {
            if (mapping.original == null
                || mapping.replacements == null
                || variantIndex >= mapping.replacements.Count
                || mapping.replacements[variantIndex] == null)
            {
                continue;
            }

            replacementMap[mapping.original] = mapping.replacements[variantIndex];
        }

        return replacementMap;
    }

    private void ReplaceRendererMaterials(Renderer renderer, Dictionary<Material, Material> replacementMap)
    {
        Material[] mats = renderer.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            Material replacement;
            if (mats[i] != null && replacementMap.TryGetValue(mats[i], out replacement))
            {
                mats[i] = replacement;
                changed = true;
            }
        }

        if (changed)
        {
            Undo.RecordObject(renderer, "Replace Materials");
            renderer.sharedMaterials = mats;
        }
    }
}
