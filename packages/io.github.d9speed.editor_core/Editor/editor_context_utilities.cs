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
internal static class ShortCutExtension
{
    // ショートカット設定を追加
    [Shortcut("Custom/ShortCutEX/TransformReset", KeyCode.R, ShortcutModifiers.Alt)]
    private static void RunTransformReset()
    {
        foreach (var trans in Selection.transforms.Where(t => !EditorUtility.IsPersistent(t)))
        {
            // アンドゥの記録 Ctrl+Zで戻るように
            Undo.RecordObject(trans, "Undo TransformReset");
            TransformReset(trans);
            // 変更をエディタに反映
            EditorComponentCopyHelper.RecordPrefabChanges(trans);
        }
    }

    static void TransformReset(Transform trans)
    {
        trans.localPosition = Vector3.zero;
        trans.localEulerAngles = Vector3.zero;
        trans.localScale = Vector3.one;
    }

}


[InitializeOnLoad]
internal static class PrefabOverrideHierarchyIcon
{
    private const float IconSize = 16f;
    private const float IconMargin = 2f;
    private static readonly GUIContent OverrideIcon = EditorGUIUtility.IconContent("PrefabOverlayModified Icon");
    private static readonly GUIContent FallbackIcon = EditorGUIUtility.IconContent("Prefab Icon");

    static PrefabOverrideHierarchyIcon()
    {
#if UNITY_6000_5_OR_NEWER
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
        EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
    }

#if UNITY_6000_5_OR_NEWER
    private static void OnHierarchyGUI(EntityId instanceID, Rect selectionRect)
#else
    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
#endif
    {
#if UNITY_6000_3_OR_NEWER
        var go = EditorUtility.EntityIdToObject(instanceID) as GameObject;
#else
        var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
        if (!D9speedCommonEditorPrefs.ShowPrefabOverrideIcon || !HasPrefabUnitComponentOverride(go)) return;

        var iconRect = GetIconRect(selectionRect);
        var icon = OverrideIcon != null && OverrideIcon.image != null ? OverrideIcon : FallbackIcon;

        var color = GUI.color;
        GUI.color = new Color(1f, 0.82f, 0.25f, color.a);
        GUI.Label(iconRect, new GUIContent(icon.image, "Prefab instance has overrides"));
        GUI.color = color;
    }

    private static Rect GetIconRect(Rect selectionRect)
    {
        float x = Mathf.Max(IconMargin, selectionRect.xMin - IconSize - IconMargin);
        return new Rect(x, selectionRect.yMin, IconSize, selectionRect.height);
    }

    private static bool HasPrefabUnitComponentOverride(GameObject go)
    {
        if (go == null || !PrefabUtility.IsPartOfPrefabInstance(go))
            return false;

        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
        if (prefabRoot != go)
            return false;

        foreach (var objectOverride in PrefabUtility.GetObjectOverrides(prefabRoot, false))
        {
            if (objectOverride.instanceObject is not Component)
                continue;

            if (HasNonDefaultPropertyOverride(objectOverride.instanceObject))
                return true;
        }

        return false;
    }

    private static bool HasNonDefaultPropertyOverride(UnityEngine.Object target)
    {
        var modifications = PrefabUtility.GetPropertyModifications(target);
        if (modifications == null) return false;

        foreach (var modification in modifications)
        {
            if (!PrefabUtility.IsDefaultOverride(modification))
                return true;
        }

        return false;
    }
}

internal static class MainCameraLookAtObjectMenu
{
    [MenuItem("GameObject/メインカメラをこのオブジェクトに向ける", false, 0)]
    private static void LookAtSelectedObject(MenuCommand command)
    {
        GameObject target = GetTargetObject(command);
        if (target == null)
        {
            UnityEngine.Debug.LogWarning("オブジェクトが選択されていません。");
            return;
        }

        Camera mainCamera = GetMainCamera();
        if (mainCamera == null)
        {
            UnityEngine.Debug.LogWarning("MainCameraタグの付いたカメラがシーン上に見つかりません。");
            return;
        }

        Transform cameraTransform = mainCamera.transform;
        Vector3 targetPosition = GetObjectCenter(target);
        Vector3 direction = targetPosition - cameraTransform.position;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            UnityEngine.Debug.LogWarning("カメラと対象オブジェクトが同じ位置にあるため、向きを変更できません。");
            return;
        }

        Undo.RecordObject(cameraTransform, "Look Main Camera At Object");
        cameraTransform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        EditorComponentCopyHelper.RecordPrefabChanges(cameraTransform);
        EditorSceneManager.MarkSceneDirty(cameraTransform.gameObject.scene);

        UnityEngine.Debug.Log($"メインカメラを {target.name} に向けました。");
    }

    [MenuItem("GameObject/メインカメラをこのオブジェクトに正対させる(+Z)", false, 1)]
    private static void FaceSelectedObjectFromPositiveZ(MenuCommand command)
    {
        GameObject target = GetTargetObject(command);
        if (target == null)
        {
            UnityEngine.Debug.LogWarning("オブジェクトが選択されていません。");
            return;
        }

        Camera mainCamera = GetMainCamera();
        if (mainCamera == null)
        {
            UnityEngine.Debug.LogWarning("MainCameraタグの付いたカメラがシーン上に見つかりません。");
            return;
        }

        Transform cameraTransform = mainCamera.transform;
        Vector3 targetPosition = GetObjectCenter(target);
        float distance = Vector3.Distance(cameraTransform.position, targetPosition);

        if (distance <= Mathf.Epsilon)
            distance = GetFallbackCameraDistance(target);

        Vector3 cameraPosition = targetPosition + target.transform.forward * distance;
        Vector3 direction = targetPosition - cameraPosition;

        Undo.RecordObject(cameraTransform, "Face Main Camera To Object +Z");
        cameraTransform.position = cameraPosition;
        cameraTransform.rotation = Quaternion.LookRotation(direction, target.transform.up);
        EditorComponentCopyHelper.RecordPrefabChanges(cameraTransform);
        EditorSceneManager.MarkSceneDirty(cameraTransform.gameObject.scene);

        UnityEngine.Debug.Log($"メインカメラを {target.name} の +Z 方向から正対させました。");
    }

    [MenuItem("GameObject/メインカメラをこのオブジェクトに向ける", true)]
    private static bool ValidateLookAtSelectedObject()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem("GameObject/メインカメラをこのオブジェクトに正対させる(+Z)", true)]
    private static bool ValidateFaceSelectedObjectFromPositiveZ()
    {
        return Selection.activeGameObject != null;
    }

    private static GameObject GetTargetObject(MenuCommand command)
    {
        return command.context as GameObject ?? Selection.activeGameObject;
    }

    private static Camera GetMainCamera()
    {
        GameObject[] mainCameraObjects;

        try
        {
            mainCameraObjects = GameObject.FindGameObjectsWithTag("MainCamera");
        }
        catch (UnityException)
        {
            return null;
        }

        foreach (GameObject obj in mainCameraObjects)
        {
            if (obj.activeInHierarchy && obj.TryGetComponent(out Camera camera))
                return camera;
        }

        foreach (GameObject obj in mainCameraObjects)
        {
            if (obj.TryGetComponent(out Camera camera))
                return camera;
        }

        return null;
    }

    private static Vector3 GetObjectCenter(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (TryGetBoundsCenter(renderers, out Vector3 rendererCenter))
            return rendererCenter;

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        if (TryGetBoundsCenter(colliders, out Vector3 colliderCenter))
            return colliderCenter;

        return target.transform.position;
    }

    private static float GetFallbackCameraDistance(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (TryGetBoundsSize(renderers, out Vector3 rendererSize))
            return Mathf.Max(rendererSize.magnitude, 1f);

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        if (TryGetBoundsSize(colliders, out Vector3 colliderSize))
            return Mathf.Max(colliderSize.magnitude, 1f);

        return 1f;
    }

    private static bool TryGetBoundsCenter<T>(T[] components, out Vector3 center) where T : Component
    {
        if (!TryGetBounds(components, out Bounds bounds))
        {
            center = Vector3.zero;
            return false;
        }

        center = bounds.center;
        return true;
    }

    private static bool TryGetBoundsSize<T>(T[] components, out Vector3 size) where T : Component
    {
        if (!TryGetBounds(components, out Bounds bounds))
        {
            size = Vector3.zero;
            return false;
        }

        size = bounds.size;
        return true;
    }

    private static bool TryGetBounds<T>(T[] components, out Bounds bounds) where T : Component
    {
        bounds = new Bounds();
        bool hasBounds = false;

        foreach (T component in components)
        {
            Bounds componentBounds;
            if (component is Renderer renderer)
            {
                componentBounds = renderer.bounds;
            }
            else if (component is Collider collider)
            {
                componentBounds = collider.bounds;
            }
            else
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = componentBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(componentBounds);
            }
        }

        return hasBounds;
    }
}

internal static class CopyComponentNameMenu
{
    [MenuItem("CONTEXT/Component/コンポーネント名をコピー")]
    private static void CopyComponentName(MenuCommand command)
    {
        if (command.context is not Component component) return;

        string name = component.GetType().Name;
        EditorGUIUtility.systemCopyBuffer = name;
        UnityEngine.Debug.Log($"Copied: {name}");
    }
}


internal static class MultiComponentCopyPasteMenu
{
    private static readonly List<Component> copied_components = new();

    [MenuItem("CONTEXT/Component/ここから下のコンポーネントをコピー")]
    private static void CopyComponentsFromHere(MenuCommand command)
    {
        var clicked_component = command.context as Component;
        if (clicked_component == null) return;

        var components = GetComponents(clicked_component.gameObject);
        int start_index = components.IndexOf(clicked_component);
        if (start_index < 0) return;

        CopyComponents(clicked_component, start_index);
    }

    [MenuItem("CONTEXT/Component/コピーしたコンポーネントを新規貼り付け")]
    private static void PasteComponentsToComponentOwner(MenuCommand command)
    {
        var clicked_component = command.context as Component;
        if (clicked_component == null) return;

        PasteComponentsAsNew(clicked_component.gameObject);
    }

    [MenuItem("CONTEXT/Component/コピーしたコンポーネントを新規貼り付け", true)]
    private static bool ValidatePasteComponentsToComponentOwner(MenuCommand command)
    {
        return HasCopiedComponents() && command.context is Component component && !EditorUtility.IsPersistent(component);
    }

    [MenuItem("GameObject/コピーしたコンポーネントを新規貼り付け", false, 20)]
    private static void PasteComponentsToSelectedGameObjects()
    {
        foreach (var game_object in Selection.gameObjects)
        {
            PasteComponentsAsNew(game_object);
        }
    }

    [MenuItem("GameObject/コピーしたコンポーネントを新規貼り付け", true)]
    private static bool ValidatePasteComponentsToSelectedGameObjects()
    {
        return HasCopiedComponents() && Selection.gameObjects.Any(go => !EditorUtility.IsPersistent(go));
    }

    private static void CopyComponents(Component clicked_component, int start_index)
    {
        var components = GetComponents(clicked_component.gameObject);

        copied_components.Clear();
        copied_components.AddRange(components.Skip(start_index).Where(IsCopyableComponent));

        UnityEngine.Debug.Log($"{clicked_component.gameObject.name} から {copied_components.Count} 個のコンポーネントをコピーしました。");
    }

    private static List<Component> GetComponents(GameObject game_object)
    {
        return game_object
            .GetComponents<Component>()
            .Where(component => component != null)
            .ToList();
    }

    private static bool IsCopyableComponent(Component component)
    {
        return component != null && component is not Transform;
    }

    private static bool HasCopiedComponents()
    {
        copied_components.RemoveAll(component => component == null);
        return copied_components.Count > 0;
    }

    private static void PasteComponentsAsNew(GameObject target)
    {
        if (target == null || EditorUtility.IsPersistent(target)) return;

        Undo.RegisterFullObjectHierarchyUndo(target, "Paste Components As New");

        int pasted_count = 0;
        foreach (var source_component in copied_components)
        {
            if (source_component == null) continue;

            ComponentUtility.CopyComponent(source_component);
            if (ComponentUtility.PasteComponentAsNew(target))
                pasted_count++;
        }

        EditorComponentCopyHelper.RecordPrefabChanges(target);
        EditorSceneManager.MarkSceneDirty(target.scene);
        UnityEngine.Debug.Log($"{target.name} に {pasted_count} 個のコンポーネントを新規貼り付けしました。");
    }
}


internal static class SearchSelectedHierarchyPath
{
    internal static string BuildQuery(string path) => "h: path:\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [MenuItem("Tools/Search/Selected Hierarchy Path", true)]
    [MenuItem("GameObject/このヒエラルキー配下を検索", true)]
    private static bool CanSearch() => Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);

    [MenuItem("Tools/Search/Selected Hierarchy Path")]
    [MenuItem("GameObject/このヒエラルキー配下を検索", false, 0)]
    private static void SearchBySelectedHierarchyPath()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            UnityEngine.Debug.LogWarning("GameObject を選択してください。");
            return;
        }

        string path = HierarchyPathHelper.GetHierarchyPath(go.transform);
        string query = BuildQuery(path);
        SearchContext context = SearchService.CreateContext(query);

        SearchService.ShowWindow(context);
    }

}
}
