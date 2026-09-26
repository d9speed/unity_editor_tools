using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace D9speed.PackageValidation
{
    public static class guide_board_checks
    {
        private const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly List<string> passed = new List<string>();
        private static string output_directory;
        private static string asset_directory;
        private static Type preview_type;
        private static Type settings_type;
        private static Type exporter_type;

        public static void run()
        {
            output_directory = Path.GetFullPath("Logs/guide_board");
            Directory.CreateDirectory(output_directory);
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro");
                AssetDatabase.importPackageCompleted += imported;
                AssetDatabase.importPackageFailed += import_failed;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }
            run_checks();
        }

        private static void imported(string name)
        {
            AssetDatabase.importPackageCompleted -= imported;
            AssetDatabase.importPackageFailed -= import_failed;
            EditorApplication.delayCall += run_checks;
        }

        private static void import_failed(string name, string error)
        {
            finish(new InvalidOperationException(error));
        }

        private static void run_checks()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "D9speed.EditorUtils.GuideBoard");
                settings_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_settings", true);
                preview_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_preview", true);
                exporter_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_exporter", true);
                asset_directory = "Assets/guide_board_check_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(asset_directory));
                var settings = Activator.CreateInstance(settings_type, true);
                set(settings, "font", TMP_Settings.defaultFontAsset);
                set(settings, "text", "SETUP GUIDE\nAttach the outfit here.");
                int scene_count = EditorSceneManager.previewSceneCount;
                var active_scene = SceneManager.GetActiveScene();
                bool was_dirty = active_scene.isDirty;
                var sentinel = new RenderTexture(8, 8, 0);
                sentinel.Create();
                var old_active = RenderTexture.active;
                RenderTexture.active = sentinel;
                try
                {
                    using (var preview = (IDisposable)Activator.CreateInstance(preview_type, true))
                    {
                        check(EditorSceneManager.previewSceneCount == scene_count + 1, "isolated_preview_scene");
                        invoke(preview, "render", settings);
                        check(RenderTexture.active == sentinel, "render_restores_active_texture");
                        check(!get<bool>(preview, "text_overflows"), "normal_text_fits");
                        var first_texture = get<Texture>(preview, "texture");
                        var bytes = (byte[])invoke(preview, "encode_png");
                        check(RenderTexture.active == sentinel, "png_restores_active_texture");
                        File.WriteAllBytes(Path.Combine(output_directory, "guide_preview_english.png"), bytes);
                        var image = new Texture2D(2, 2);
                        try
                        {
                            image.LoadImage(bytes);
                            check(image.width == 1024 && image.height == 512, "png_dimensions");
                            check(image.GetPixel(5, 5).r > 0.97f, "white_background");
                            int dark = image.GetPixels32().Count(p => p.r < 150 && p.g < 150 && p.b < 150);
                            check(dark > 500 && dark < 60000, "rendered_text_pixels");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(image); }

                        var prefab = save(asset_directory + "/guide_board.prefab", bytes);
                        var image_path = asset_directory + "/guide_board_image.png";
                        var material_path = asset_directory + "/guide_board_material.mat";
                        var original_guids = new[] { AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)), AssetDatabase.AssetPathToGUID(image_path), AssetDatabase.AssetPathToGUID(material_path) };
                        var original_image = File.ReadAllBytes(image_path);
                        check(prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab), "saved_prefab_asset");
                        check(prefab.CompareTag("EditorOnly"), "editor_only_tag");
                        check(prefab.GetComponent<Collider>() == null && prefab.GetComponents<Component>().Length == 3, "quad_without_collider_or_scripts");
                        check(prefab.transform.localScale == new Vector3(2f, 1f, 1f), "prefab_image_aspect_ratio");
                        var renderer = prefab.GetComponent<MeshRenderer>();
                        check(prefab.GetComponent<MeshFilter>().sharedMesh != null && renderer.sharedMaterial.shader.name == "Unlit/Texture", "persistent_quad_and_unlit_material");
                        check(AssetDatabase.GetAssetPath(renderer.sharedMaterial.mainTexture) == image_path, "persistent_texture_reference");
                        var dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true);
                        check(!dependencies.Any(p => p.EndsWith(".cs") || p.Contains("TextMesh Pro") || p.Contains("com.unity.textmeshpro") || p.Contains("editor_core")), "prefab_has_no_tmp_or_tool_dependency");
                        var importer = (TextureImporter)AssetImporter.GetAtPath(image_path);
                        check(importer.sRGBTexture && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.wrapMode == TextureWrapMode.Clamp, "texture_import_settings");
                        var second = save(asset_directory + "/guide_board.prefab", bytes);
                        check(AssetDatabase.GetAssetPath(second) != AssetDatabase.GetAssetPath(prefab), "duplicate_prefab_gets_unique_path");
                        check(original_image.SequenceEqual(File.ReadAllBytes(image_path)) && original_guids.SequenceEqual(new[] { AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)), AssetDatabase.AssetPathToGUID(image_path), AssetDatabase.AssetPathToGUID(material_path) }), "existing_assets_and_guids_preserved");
                        int before_rejection = Directory.GetFiles(asset_directory).Length;
                        expect_rejection("Assets/../outside.prefab", bytes);
                        expect_rejection(asset_directory + "/empty.prefab", Array.Empty<byte>());
                        check(Directory.GetFiles(asset_directory).Length == before_rejection, "invalid_exports_create_no_assets");

                        set(settings, "background_color", new Color(0.6f, 0.3f, 0.15f));
                        invoke(preview, "render", settings);
                        check(get<Texture>(preview, "texture") == first_texture, "same_size_texture_reused");
                        var color_image = new Texture2D(2, 2);
                        try
                        {
                            color_image.LoadImage((byte[])invoke(preview, "encode_png"));
                            var color = color_image.GetPixel(5, 5);
                            check(Mathf.Abs(color.r - 0.6f) < 0.03f && Mathf.Abs(color.g - 0.3f) < 0.03f, "background_color_accuracy");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(color_image); }
                        set(settings, "width", 64);
                        set(settings, "height", 64);
                        set(settings, "padding", 4);
                        set(settings, "text", "This is deliberately too long to fit inside the small image.");
                        invoke(preview, "render", settings);
                        check(get<bool>(preview, "text_overflows") && get<Texture>(preview, "texture").width == 64, "overflow_and_texture_resize");
                        check(get<int>(preview, "render_count") == 3, "only_requested_renders");
                        set(settings, "text", "日本語");
                        invoke(preview, "render", settings);
                        check(get<bool>(preview, "has_missing_characters"), "missing_japanese_glyphs_detected");
                    }
                }
                finally
                {
                    RenderTexture.active = old_active;
                    sentinel.Release();
                    UnityEngine.Object.DestroyImmediate(sentinel);
                }
                check(EditorSceneManager.previewSceneCount == scene_count, "preview_and_export_scenes_released");
                check(SceneManager.GetActiveScene() == active_scene && active_scene.isDirty == was_dirty, "active_scene_unchanged");
                var source_font = AssetDatabase.LoadAssetAtPath<Font>("Assets/test_font.ttc");
                check(source_font != null, "japanese_test_font_available");
                var japanese_font = TMP_FontAsset.CreateFontAsset(source_font);
                try
                {
                    set(settings, "font", japanese_font);
                    set(settings, "text", "衣装のセットアップ\n\nこの位置にギミックを配置します。\n設定が終わったら確認してください。");
                    set(settings, "width", 1024);
                    set(settings, "height", 512);
                    set(settings, "padding", 48);
                    set(settings, "background_color", Color.white);
                    using (var preview = (IDisposable)Activator.CreateInstance(preview_type, true))
                    {
                        invoke(preview, "render", settings);
                        check(!get<bool>(preview, "has_missing_characters") && !get<bool>(preview, "text_overflows"), "japanese_text_fits_and_renders");
                        var png = (byte[])invoke(preview, "encode_png");
                        File.WriteAllBytes(Path.Combine(output_directory, "guide_preview_japanese.png"), png);
                        save(asset_directory + "/japanese_board.prefab", png);
                    }
                }
                finally
                {
                    foreach (var atlas in japanese_font.atlasTextures) if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
                    UnityEngine.Object.DestroyImmediate(japanese_font.material);
                    UnityEngine.Object.DestroyImmediate(japanese_font);
                }

                var window_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_window", true);
                var window = (EditorWindow)ScriptableObject.CreateInstance(window_type);
                try
                {
                    window_type.GetField("settings", members).SetValue(window, settings);
                    set(settings, "font", TMP_Settings.defaultFontAsset);
                    set(settings, "text", "SETUP GUIDE");
                    invoke(window, "CreateGUI");
                    check(window.rootVisualElement.Q<Image>("preview_image") != null && window.rootVisualElement.Q<Button>("prefab_button") != null, "package_uxml_and_prefab_button_load");
                    check(window.rootVisualElement.ClassListContains("d9_ui_root"), "shared_editor_theme_applied");
                    invoke(window, "render_preview");
                    check(window.rootVisualElement.Q<Button>("prefab_button").enabledSelf, "valid_preview_enables_prefab_save");
                    set(settings, "font", null);
                    invoke(window, "render_preview");
                    check(!window.rootVisualElement.Q<Button>("prefab_button").enabledSelf && !window.rootVisualElement.Q<Button>("export_button").enabledSelf, "missing_font_disables_export");
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
                check(EditorSceneManager.previewSceneCount == scene_count, "window_resources_released");
                var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "D9speed.EditorUtils");
                var menu = core.GetType("D9speed_BaseEditorUtils.guide_board_menu", true).GetMethod("open_window", members).GetCustomAttribute<MenuItem>();
                check(menu.menuItem == "D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)", "exact_requested_menu_path");
                finish(null);
            }
            catch (Exception error) { finish(error); }
        }

        private static GameObject save(string path, byte[] png) => (GameObject)exporter_type.GetMethod("save_prefab", members).Invoke(null, new object[] { path, png });
        private static object invoke(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, members).Invoke(target, arguments);
        private static void set(object target, string name, object value) => target.GetType().GetField(name, members).SetValue(target, value);
        private static T get<T>(object target, string name) => (T)target.GetType().GetProperty(name, members).GetValue(target);

        private static void expect_rejection(string path, byte[] png)
        {
            try { save(path, png); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { return; }
            throw new InvalidOperationException("Expected rejected path or data: " + path);
        }

        private static void check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed.Add(description);
            Debug.Log("GUIDE_BOARD_CHECK: " + description);
        }

        private static void finish(Exception error)
        {
            string result = (error == null ? "PASS" : "FAIL") + "\nUnity " + Application.unityVersion + "\nColor space: " + QualitySettings.activeColorSpace + "\n" + string.Join("\n", passed);
            if (error != null) { result += "\n" + error; Debug.LogException(error); }
            File.WriteAllText(Path.Combine(output_directory, "validation_result.txt"), result);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
