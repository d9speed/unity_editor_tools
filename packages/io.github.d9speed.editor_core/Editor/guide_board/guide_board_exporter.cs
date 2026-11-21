using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    internal static class guide_board_exporter
    {
        public static Sprite save_sprite(string requested_path, byte[] png)
        {
            if (string.IsNullOrEmpty(requested_path)) throw new ArgumentException("保存先を指定してください。");
            var requested_asset_path = requested_path.Replace('\\', '/');
            var project_directory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var full_path = Path.GetFullPath(Path.Combine(project_directory, requested_asset_path));
            var assets_directory = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!requested_asset_path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !full_path.StartsWith(assets_directory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(full_path), ".png", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(requested_asset_path).Replace('\\', '/')))
                throw new ArgumentException("Assets 内のフォルダーに .png として保存してください。");
            if (png == null || png.Length == 0) throw new ArgumentException("画像がありません。");

            var asset_path = AssetDatabase.GenerateUniqueAssetPath(requested_asset_path);
            ensure_new_path(asset_path);
            bool created_file = false;
            try
            {
                using (var stream = new FileStream(asset_path, FileMode.CreateNew, FileAccess.Write))
                {
                    created_file = true;
                    stream.Write(png, 0, png.Length);
                }
                AssetDatabase.ImportAsset(asset_path, ImportAssetOptions.ForceSynchronousImport);
                configure_texture(asset_path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(asset_path);
                if (sprite == null) throw new InvalidOperationException("Sprite を読み込めませんでした。");
                return sprite;
            }
            catch
            {
                if (created_file && !AssetDatabase.DeleteAsset(asset_path) && File.Exists(asset_path)) File.Delete(asset_path);
                throw;
            }
        }

        private static void ensure_new_path(string path)
        {
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new IOException("保存先に既存のアセットがあります: " + path);
        }

        private static void configure_texture(string asset_path)
        {
            var importer = AssetImporter.GetAtPath(asset_path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("PNG のインポート設定を取得できませんでした。");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var sprite_settings = new TextureImporterSettings();
            importer.ReadTextureSettings(sprite_settings);
            sprite_settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(sprite_settings);
            importer.spritePixelsPerUnit = 512f;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }
}
