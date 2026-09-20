using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEditor.Search;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils
{
public class TransformMirrorTool : EditorWindow
{
    [SerializeField] private List<UnityEngine.Object> targetObjects = new();
    private ListView listView;
    private Label target_count;
    private Label empty_state;
    private Button clear_button;
    private Button mirror_button;

    [SerializeField] private bool useCustomPivot = false;

    [SerializeField] private Vector3 customPivot = Vector3.zero;

    // 回転の切り替えトグル
    [SerializeField] private bool mirrorRotation = true;
    [SerializeField] private bool CopyblendshapesWeightValue = true;

    [MenuItem("D9speed/Transform Mirror Tool")]
    public static void ShowWindow()
    {
        GetWindow<TransformMirrorTool>("Transform Mirror");
    }

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

        var header = new VisualElement();
        header.AddToClassList("d9_header");
        header.Add(CreateLabel("Transform Mirror", "d9_title"));
        header.Add(CreateLabel("X軸を基準に、反転したコピーを作成します。", "d9_description"));
        content.Add(header);

        var targets = new VisualElement();
        targets.AddToClassList("d9_section");
        content.Add(targets);
        var target_header = new VisualElement();
        target_header.AddToClassList("d9_section_header");
        target_header.Add(CreateLabel("複製するオブジェクト", "d9_section_title"));
        target_count = CreateLabel(string.Empty, "d9_badge");
        target_count.name = "target_count";
        target_header.Add(target_count);
        targets.Add(target_header);
        targets.Add(CreateLabel("Hierarchy / Project からドラッグ＆ドロップ", "d9_description"));

        var list_container = new VisualElement();
        list_container.AddToClassList("d9_list_container");
        targets.Add(list_container);
        listView = new ListView(targetObjects, 28, () => CreateLabel(string.Empty, "d9_list_row"), (e, i) =>
        {
            (e as Label).text = targetObjects[i] != null ? targetObjects[i].name : "<null>";
        }) { name = "mirror_targets" };
        listView.AddToClassList("d9_target_list");
        listView.selectionType = SelectionType.Multiple;
        listView.reorderable = true;
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
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is GameObject)
                    targetObjects.Add(obj);
            }
            listView.RemoveFromClassList("d9_drop_active");
            listView.Rebuild();
            RefreshTargetState();
            evt.StopPropagation();
        });
        list_container.Add(listView);
        empty_state = CreateLabel("GameObject / Prefab をここへ追加", "d9_empty_state");
        empty_state.pickingMode = PickingMode.Ignore;
        list_container.Add(empty_state);

        var list_actions = new VisualElement();
        list_actions.AddToClassList("d9_list_actions");
        clear_button = new Button(() =>
        {
            targetObjects.Clear();
            listView.ClearSelection();
            listView.Rebuild();
            RefreshTargetState();
        }) { name = "clear_targets", text = "一覧をクリア", tooltip = "一覧から外します。シーンやアセットは削除しません。" };
        clear_button.AddToClassList("d9_button");
        list_actions.Add(clear_button);
        targets.Add(list_actions);

        var settings = new VisualElement();
        settings.AddToClassList("d9_section");
        settings.Add(CreateLabel("ミラー設定", "d9_section_title"));
        content.Add(settings);
        var pivotToggle = CreateToggle("基準点を指定する", useCustomPivot, "use_custom_pivot");
        settings.Add(pivotToggle);
        settings.Add(CreateLabel("通常はワールド原点 (0, 0, 0) を使います。", "d9_description"));

        var pivotField = new Vector3Field("基準点") { name = "custom_pivot", value = customPivot };
        pivotField.AddToClassList("d9_vector_field");
        pivotField.SetEnabled(useCustomPivot);
        pivotToggle.RegisterValueChangedCallback(evt =>
        {
            useCustomPivot = evt.newValue;
            pivotField.SetEnabled(useCustomPivot);
        });
        pivotField.RegisterValueChangedCallback(evt => customPivot = evt.newValue);
        settings.Add(pivotField);

        var rotToggle = CreateToggle("回転も反転する (Y / Z)", mirrorRotation, "mirror_rotation");
        rotToggle.RegisterValueChangedCallback(evt => mirrorRotation = evt.newValue);
        settings.Add(rotToggle);

        var blendshapeToggle = CreateToggle("BlendShapeのウェイトを引き継ぐ", CopyblendshapesWeightValue, "copy_blendshapes");
        blendshapeToggle.RegisterValueChangedCallback(evt => CopyblendshapesWeightValue = evt.newValue);
        settings.Add(blendshapeToggle);

        var footer = new VisualElement();
        footer.AddToClassList("d9_footer");
        mirror_button = new Button(() => InstantiateMirroredAll())
        {
            name = "create_mirror", text = "ミラーを作成"
        };
        mirror_button.AddToClassList("d9_button");
        mirror_button.AddToClassList("d9_button_primary");
        footer.Add(mirror_button);
        footer.Add(CreateLabel("シーンに複製して配置します。Undoで元に戻せます。", "d9_description"));
        root.Add(footer);
        RefreshTargetState();
    }

    private static Label CreateLabel(string text, string style_class)
    {
        var label = new Label(text);
        label.AddToClassList(style_class);
        return label;
    }

    private static Toggle CreateToggle(string text, bool value, string name)
    {
        var toggle = new Toggle { text = text, value = value, name = name };
        toggle.AddToClassList("d9_toggle");
        var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
        glyph.AddToClassList("d9_check_glyph");
        toggle.Q(className: Toggle.checkmarkUssClassName).Add(glyph);
        return toggle;
    }

    private void OnInspectorUpdate()
    {
        EditorUiTheme.RefreshTheme(rootVisualElement);
        RefreshTargetState();
    }

    private void RefreshTargetState()
    {
        if (mirror_button == null) return;
        var count = targetObjects.Count(obj => obj is GameObject);
        target_count.text = count + " 件";
        empty_state.EnableInClassList("d9_hidden", targetObjects.Count > 0);
        clear_button.SetEnabled(targetObjects.Count > 0);
        mirror_button.SetEnabled(count > 0);
    }

    // ============================================================
    // Main Processing
    // ============================================================
    void InstantiateMirroredAll()
    {
        Vector3 pivot = useCustomPivot ? customPivot : Vector3.zero;

        foreach (var obj in targetObjects)
        {
            if (obj is not GameObject go) continue;

            // プロジェクトウィンドウとプレハブウィンドウで名前が変更されている場合、ヒエラルキーの名前を使用する
            string sceneName = go.name;

            // Prefab asset?
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                InstantiatePrefabAsMirrored(go, sceneName, pivot);
                continue;
            }

            // GameObjectだった場合
            InstantiateSceneObjectMirrored(go, pivot);
        }

        UnityEngine.Debug.Log("Prefab展開 + ミラー配置 完了");
    }


    // ============================================================
    // 回転ミラー処理
    // ============================================================


    Quaternion MirrorRotation(Quaternion rot)
    {
        // X軸ミラー：Y,Z を反転
        return new Quaternion(rot.x, -rot.y, -rot.z, rot.w);
    }


    // ============================================================
    // Prefab をシーンに複製してミラー
    // ============================================================
    void InstantiatePrefabAsMirrored(GameObject prefabInstance,string instanceName, Vector3 pivot)
    {
        // ▼ シーンにある Prefab Instance の Transform を使用
        Transform src = prefabInstance.transform;

        Vector3 originalPos = src.position;
        Quaternion originalRot = src.rotation;

        // ▼ Prefab Asset を正しく取得（D&Dと同じ挙動に必要）
        var prefabAsset = EditorUtility.IsPersistent(prefabInstance) ? prefabInstance : PrefabUtility.GetCorrespondingObjectFromSource(prefabInstance);
        if (prefabAsset == null)
        {
            UnityEngine.Debug.LogError("Prefab Asset が取得できません。");
            return;
        }

        // ▼ 現在のシーンに Prefab Instance を正しく生成（D&Dと同等）
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        GameObject clone = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene);

        if (clone == null)
        {
            UnityEngine.Debug.LogError("InstantiatePrefab が null を返しました。");
            return;
        }

        Undo.RegisterCreatedObjectUndo(clone, "Instantiate Prefab Mirrored");

        clone.name = instanceName + "_Mirrored";

        Transform t = clone.transform;

        // ▼ シーン上の元の Transform をミラー化してセット
        Vector3 offset = originalPos - pivot;
        offset.x *= -1;
        t.position = pivot + offset;

        // 回転
        if (mirrorRotation)
        {
            t.rotation = MirrorRotation(originalRot);
        }
        else
        {
            t.rotation = originalRot;
        }

        // スケールもそのままコピー
        t.localScale = prefabInstance.transform.lossyScale;

        // ▼ ブレンドシェイプのウェイトをコピー
        if (CopyblendshapesWeightValue)
        {
            CopyBlendShapes(prefabInstance, clone);
        }
    }


// ============================================================

// BlendShape のウェイトをコピー ミラーしたときにブレンドシェイプの値をコピーするかどうか

    void CopyBlendShapes(GameObject source, GameObject destination)
    {
        var srcRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var dstRenderers = destination.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (srcRenderers.Length != dstRenderers.Length)
        {
            UnityEngine.Debug.LogWarning("SkinnedMesh の数が一致しません。階層構造が異なる可能性があります。");
        }

        int count = Mathf.Min(srcRenderers.Length, dstRenderers.Length);

        for (int r = 0; r < count; r++)
        {
            var src = srcRenderers[r];
            var dst = dstRenderers[r];

            Mesh mesh = src.sharedMesh;
            if (mesh == null) continue;

            int blendCount = mesh.blendShapeCount;

            for (int i = 0; i < blendCount; i++)
            {
                float w = src.GetBlendShapeWeight(i);
                int target_index = dst.sharedMesh == null ? -1 : dst.sharedMesh.GetBlendShapeIndex(mesh.GetBlendShapeName(i));
                if (target_index >= 0) dst.SetBlendShapeWeight(target_index, w);
            }
        }
    }

    // ============================================================
    // シーンオブジェクトを複製してミラー
    // ============================================================
    void InstantiateSceneObjectMirrored(GameObject original, Vector3 pivot)
    {
        GameObject clone = Instantiate(original);
        Undo.RegisterCreatedObjectUndo(clone, "Duplicate (Mirrored)");

        clone.name = original.name + "_Mirrored";

        Transform t = clone.transform;

        Vector3 offset = original.transform.position - pivot;
        offset.x *= -1;
        t.position = pivot + offset;
        t.localScale = original.transform.lossyScale;

        if (mirrorRotation)
        {
            t.rotation = MirrorRotation(original.transform.rotation);
        }
        else
        {
            t.rotation = original.transform.rotation;
        }
    }
}
}
