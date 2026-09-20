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
    private List<UnityEngine.Object> targetObjects = new();
    private ListView listView;

    private bool useCustomPivot = false;

    private Vector3 customPivot = Vector3.zero;

    // 回転の切り替えトグル
    private bool mirrorRotation = true;
    private bool CopyblendshapesWeightValue = true;

    [MenuItem("D9speed/Transform Mirror Tool")]
    public static void ShowWindow()
    {
        GetWindow<TransformMirrorTool>("Transform Mirror");
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = 10;
        root.style.paddingRight = 10;
        root.style.paddingTop = 10;
        D9speedEditorFontUtility.Apply(root);

        root.Add(new Label("Drag & Drop GameObjects or Prefabs"));

        listView = new ListView(targetObjects, 20, () => new Label(), (e, i) =>
        {
            (e as Label).text = targetObjects[i] != null ? targetObjects[i].name : "<null>";
        });
        listView.style.height = 200;
        listView.selectionType = SelectionType.Multiple;
        listView.reorderable = true;

        listView.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        });

        listView.RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is GameObject)
                    targetObjects.Add(obj);
            }
            listView.Rebuild();
        });

        root.Add(listView);

        var pivotToggle = new Toggle("基準点をカスタムにする (デフォルト：ワールド原点)");
        pivotToggle.RegisterValueChangedCallback(evt => useCustomPivot = evt.newValue);
        root.Add(pivotToggle);

        var pivotField = new Vector3Field("カスタム基準点");
        pivotField.value = customPivot;
        pivotField.RegisterValueChangedCallback(evt => customPivot = evt.newValue);
        root.Add(pivotField);

        var rotToggle = new Toggle("回転もミラーする(YZ)") { value = mirrorRotation };
        rotToggle.RegisterValueChangedCallback(evt => mirrorRotation = evt.newValue);
        root.Add(rotToggle);

        var blendshapeToggle = new Toggle("ブレンドシェイプのウェイトもコピーする") { value = CopyblendshapesWeightValue };
        blendshapeToggle.RegisterValueChangedCallback(evt => CopyblendshapesWeightValue = evt.newValue);
        root.Add(blendshapeToggle);

        var button = new Button(() => InstantiateMirroredAll())
        {
            text = "Prefab → シーン複製 & ミラー配置"
        };
        button.style.marginTop = 10;
        button.style.height = 32;
        root.Add(button);
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
