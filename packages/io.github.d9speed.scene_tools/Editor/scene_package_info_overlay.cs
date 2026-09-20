#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Overlays;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed.ScenePackageInfo
{
    [Overlay(typeof(SceneView), "d9speed_scene_package_info", "Package / Prefab Info", true,
        defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom,
        defaultLayout = Layout.Panel)]
    public sealed class scene_package_info_overlay : Overlay
    {
        private VisualElement panel;
        private Button versions_button;
        private Button avatar_utils_button;
        private Label mesh_label;
        private string version_text = "";
        private bool versions_dirty = true;
        private double next_update;

        public override VisualElement CreatePanelContent()
        {
            panel = new VisualElement();
            panel.style.minWidth = 240;
            versions_button = new Button(copy_info) { tooltip = "クリックでバージョン情報・メッシュ集計をまとめてコピー" };
            versions_button.style.unityTextAlign = TextAnchor.MiddleLeft;
            versions_button.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(versions_button);
            mesh_label = new Label { tooltip = "クリックで全情報をコピー。非アクティブ・全LODを含む。三角形換算。同一マテリアルは1個、未設定は除外。" };
            mesh_label.style.whiteSpace = WhiteSpace.Normal;
            mesh_label.style.marginTop = 6;
            mesh_label.style.paddingTop = 6;
            mesh_label.style.borderTopWidth = 1;
            mesh_label.style.borderTopColor = new Color(.5f, .5f, .5f, .5f);
            panel.Add(mesh_label);
            avatar_utils_button = new Button(open_avatar_utils)
            {
                text = "lilAvatarUtils を開く",
                tooltip = "選択プレハブを対象に開く。複数選択時はアクティブな選択を使用。"
            };
            avatar_utils_button.style.marginTop = 8;
            avatar_utils_button.style.display = DisplayStyle.None;
            panel.Add(avatar_utils_button);
            panel.RegisterCallback<AttachToPanelEvent>(attach);
            panel.RegisterCallback<DetachFromPanelEvent>(detach);
            panel.RegisterCallback<MouseUpEvent>(evt =>
            {
                var target = evt.target as VisualElement;
                if (evt.button == 0 && !versions_button.Contains(target) && !avatar_utils_button.Contains(target)) copy_info();
            });
            return panel;
        }

        private void attach(AttachToPanelEvent evt)
        {
            EditorApplication.update += update;
            EditorApplication.projectChanged += invalidate_versions;
            Selection.selectionChanged += refresh_mesh;
            Undo.undoRedoPerformed += refresh_mesh;
            versions_dirty = true;
            next_update = 0;
            update();
        }

        private void detach(DetachFromPanelEvent evt)
        {
            EditorApplication.update -= update;
            EditorApplication.projectChanged -= invalidate_versions;
            Selection.selectionChanged -= refresh_mesh;
            Undo.undoRedoPerformed -= refresh_mesh;
        }

        private void invalidate_versions() { versions_dirty = true; }

        private void update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < next_update) return;
            next_update = EditorApplication.timeSinceStartup + 1;
            if (versions_dirty)
            {
                version_text = package_versions.read();
                versions_button.text = version_text;
                versions_button.style.display = string.IsNullOrEmpty(version_text) ? DisplayStyle.None : DisplayStyle.Flex;
                avatar_utils_button.style.display = find_avatar_utils_type() != null
                    ? DisplayStyle.Flex : DisplayStyle.None;
                versions_dirty = false;
            }
            refresh_mesh();
        }

        private void copy_info()
        {
            refresh_mesh();
            string text = string.Join("\n\n", new[] { version_text, mesh_label.text }
                .Where(value => !string.IsNullOrEmpty(value)));
            if (string.IsNullOrEmpty(text)) return;
            EditorGUIUtility.systemCopyBuffer = text;
            (containerWindow as SceneView)?.ShowNotification(new GUIContent("表示情報をコピーしました"));
        }

        private static Type find_avatar_utils_type()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("jp.lilxyzw.avatarutils.AvatarUtils", false))
                .FirstOrDefault(type => type != null);
        }

        private void open_avatar_utils()
        {
            var target = get_prefab_root(Selection.activeGameObject);
            if (!EditorApplication.ExecuteMenuItem("Tools/lilAvatarUtils"))
            {
                (containerWindow as SceneView)?.ShowNotification(new GUIContent("lilAvatarUtils を起動できませんでした"));
                return;
            }
            if (target == null) return;
            // 任意パッケージへのコンパイル依存を避け、対象設定だけを連携する。
            var type = find_avatar_utils_type();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var target_field = type?.GetField("gameObject", flags);
            var analyzed_field = type?.GetField("isAnalyzed", flags);
            if (type == null || !typeof(EditorWindow).IsAssignableFrom(type) ||
                target_field?.FieldType != typeof(GameObject) || analyzed_field?.FieldType != typeof(bool))
            {
                (containerWindow as SceneView)?.ShowNotification(new GUIContent("このバージョンでは対象を手動で指定してください"));
                return;
            }
            var window = EditorWindow.GetWindow(type);
            target_field.SetValue(window, target);
            // 既に開いている場合も次の OnGUI で新しい対象を解析する。
            analyzed_field.SetValue(window, false);
            window.Repaint();
        }

        private static GameObject get_prefab_root(GameObject selected)
        {
            if (selected == null) return null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && (selected == stage.prefabContentsRoot ||
                selected.transform.IsChildOf(stage.prefabContentsRoot.transform))) return stage.prefabContentsRoot;
            if (PrefabUtility.IsPartOfPrefabAsset(selected)) return selected.transform.root.gameObject;
            return PrefabUtility.IsPartOfPrefabInstance(selected)
                ? PrefabUtility.GetOutermostPrefabInstanceRoot(selected) : null;
        }

        private void refresh_mesh()
        {
            if (mesh_label == null) return;
            var roots = new HashSet<GameObject>();
            foreach (var selected in Selection.gameObjects)
            {
                var root = get_prefab_root(selected);
                if (root != null) roots.Add(root);
            }
            mesh_label.style.display = roots.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (roots.Count == 0) { mesh_label.text = ""; return; }
            int skinned_count = 0, mesh_count = 0, missing_count = 0;
            long polygons = 0;
            var renderers = new HashSet<Renderer>();
            var materials = new HashSet<Material>();
            foreach (var root in roots)
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderers.Add(renderer);
            foreach (var renderer in renderers)
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skinned) { skinned_count++; mesh = skinned.sharedMesh; }
                else if (renderer is MeshRenderer)
                {
                    mesh_count++;
                    var filter = renderer.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                else continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null) materials.Add(material);
                if (mesh == null) { missing_count++; continue; }
                polygons += count_triangles(mesh);
            }
            string title = roots.Count == 1 ? roots.First().name : $"プレハブ {roots.Count} 個";
            mesh_label.text = $"{title}\nSkinned Mesh Renderer: {skinned_count:N0}\nMesh Renderer: {mesh_count:N0}" +
                $"\nRenderer 合計: {skinned_count + mesh_count:N0}\n総ポリゴン数 (三角形): {polygons:N0}" +
                $"\nユニークマテリアル数: {materials.Count:N0}" +
                (missing_count > 0 ? $"\nMesh 未設定: {missing_count:N0}" : "");
        }

        // インデックス配列を読み出さないため Read/Write 無効の Mesh にも対応。
        internal static long count_triangles(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                long indices = mesh.GetIndexCount(i);
                switch (mesh.GetTopology(i))
                {
                    case MeshTopology.Triangles: count += indices / 3; break;
                    case MeshTopology.Quads: count += indices / 4 * 2; break;
                }
            }
            return count;
        }
    }

    internal static class package_versions
    {
        [Serializable]
        private sealed class manifest { public string name = ""; public string version = ""; }
        private static readonly string[] package_ids = {
            "com.vrchat.base", "com.vrchat.avatars", "com.vrchat.worlds", "jp.lilxyzw.liltoon",
            "com.poiyomi.toon", "com.poiyomi.pro", "nadena.dev.modular-avatar"
        };
        private static readonly string[] labels = {
            "VRC SDK (Base)", "VRC SDK (Avatars)", "VRC SDK (Worlds)", "lilToon",
            "Poiyomi", "Poiyomi Pro", "MA"
        };

        internal static string read()
        {
            var found = new Dictionary<string, string>();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                if (package_ids.Contains(package.name)) found[package.name] = package.version;
            // unitypackage で Assets 以下へ導入した場合も package.json を検出。
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !path.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var data = JsonUtility.FromJson<manifest>(File.ReadAllText(path));
                    if (data != null && package_ids.Contains(data.name) && !found.ContainsKey(data.name) &&
                        !string.IsNullOrWhiteSpace(data.version)) found[data.name] = data.version;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { }
            }
            if (!found.ContainsKey("jp.lilxyzw.liltoon"))
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("lilToon.lilConstants", false);
                    var field = type?.GetField("currentVersionName", BindingFlags.Public | BindingFlags.Static);
                    if (field == null) continue;
                    var version = field.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(version)) found["jp.lilxyzw.liltoon"] = version;
                    break;
                }
            }
            if (!found.ContainsKey("com.vrchat.base"))
            {
                const string legacy_version = "Assets/VRCSDK/version.txt";
                if (File.Exists(legacy_version))
                {
                    try { found["com.vrchat.base"] = File.ReadAllText(legacy_version).Trim(); }
                    catch (IOException) { }
                }
            }
            if (!found.ContainsKey("com.poiyomi.toon") && !found.ContainsKey("com.poiyomi.pro"))
            {
                // 最適化済みのアバター同梱 Shader をインストール済みと誤認しない。
                foreach (var shader_name in new[] { ".poiyomi/Poiyomi Toon", ".poiyomi/Poiyomi Pro" })
                {
                    var shader = Shader.Find(shader_name);
                    if (shader == null) continue;
                    int index = shader.FindPropertyIndex("shader_master_label");
                    if (index < 0) continue;
                    var match = Regex.Match(shader.GetPropertyDescription(index), @"Poiyomi\s+(\d+\.\d+\.\d+(?:[-+][\w.-]+)?)");
                    if (match.Success) found[shader_name.EndsWith("Pro", StringComparison.Ordinal) ?
                        "com.poiyomi.pro" : "com.poiyomi.toon"] = match.Groups[1].Value;
                }
            }
            var lines = new List<string>();
            for (int i = 0; i < package_ids.Length; i++)
                if (found.TryGetValue(package_ids[i], out var version) && !string.IsNullOrWhiteSpace(version))
                    lines.Add(labels[i] + ": " + version);
            return string.Join("\n", lines);
        }
    }
}
#endif
