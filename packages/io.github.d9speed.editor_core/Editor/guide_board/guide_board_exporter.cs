using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    internal static class guide_board_exporter
    {
        public static GameObject save_prefab(string requested_path, byte[] png)
        {
            if (string.IsNullOrEmpty(requested_path)) throw new ArgumentException("保存先を指定してください。");
            var prefab_path = requested_path.Replace('\\', '/');
            var project_directory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var full_path = Path.GetFullPath(Path.Combine(project_directory, prefab_path));
            var assets_directory = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!prefab_path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !full_path.StartsWith(assets_directory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(full_path), ".prefab", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(prefab_path).Replace('\\', '/')))
                throw new ArgumentException("Assets 内のフォルダーに .prefab として保存してください。");
            if (png == null || png.Length == 0) throw new ArgumentException("画像がありません。");
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("Unlit/Texture が見つかりません。");

            prefab_path = AssetDatabase.GenerateUniqueAssetPath(prefab_path);
            var stem = prefab_path.Substring(0, prefab_path.Length - ".prefab".Length);
            var image_path = AssetDatabase.GenerateUniqueAssetPath(stem + "_image.png");
            var material_path = AssetDatabase.GenerateUniqueAssetPath(stem + "_material.mat");
            var created_paths = new List<string>();
            var temporary_scene = default(Scene);
            GameObject board = null;
            Material material = null;
            try
            {
                ensure_new_path(image_path);
                using (var stream = new FileStream(image_path, FileMode.CreateNew, FileAccess.Write))
                {
                    created_paths.Add(image_path);
                    stream.Write(png, 0, png.Length);
                }
                AssetDatabase.ImportAsset(image_path, ImportAssetOptions.ForceSynchronousImport);
                configure_texture(image_path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(image_path);
                if (texture == null) throw new InvalidOperationException("PNG を読み込めませんでした。");

                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(material_path), mainTexture = texture };
                ensure_new_path(material_path);
                created_paths.Add(material_path);
                AssetDatabase.CreateAsset(material, material_path);

                temporary_scene = EditorSceneManager.NewPreviewScene();
                board = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(board, temporary_scene);
                board.name = Path.GetFileNameWithoutExtension(prefab_path);
                board.tag = "EditorOnly";
                board.transform.localScale = new Vector3((float)texture.width / texture.height, 1f, 1f);
                UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
                var renderer = board.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                ensure_new_path(prefab_path);
                created_paths.Add(prefab_path);
                var prefab = PrefabUtility.SaveAsPrefabAsset(board, prefab_path, out bool success);
                if (!success || prefab == null) throw new InvalidOperationException("プレハブを保存できませんでした。");
                return prefab;
            }
            catch
            {
                for (int i = created_paths.Count - 1; i >= 0; i--)
                {
                    var path = created_paths[i];
                    if (!AssetDatabase.DeleteAsset(path) && File.Exists(path)) File.Delete(path);
                }
                throw;
            }
            finally
            {
                if (board != null) UnityEngine.Object.DestroyImmediate(board);
                if (temporary_scene.IsValid()) EditorSceneManager.ClosePreviewScene(temporary_scene);
                if (material != null && !EditorUtility.IsPersistent(material)) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void ensure_new_path(string path)
        {
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new IOException("保存先に既存のアセットがあります: " + path);
        }

        public static void configure_texture(string asset_path)
        {
            var importer = AssetImporter.GetAtPath(asset_path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("PNG のインポート設定を取得できませんでした。");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }
}
