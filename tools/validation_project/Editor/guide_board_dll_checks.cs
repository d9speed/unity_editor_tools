using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace D9speed.PackageValidation
{
    public static class guide_board_dll_checks
    {
        private const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly List<string> passed = new List<string>();
        private static string asset_directory;
        private static Type preview_type;
        private static Type settings_type;
        private static Type exporter_type;

        public static void run()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                check(!assemblies.Any(a => a.GetName().Name == "Unity.TextMeshPro"), "tmp_assembly_not_loaded");
                var core = assemblies.Single(a => a.GetName().Name == "D9speed.EditorUtils");
                var assembly = assemblies.Single(a => a.GetName().Name == "D9speed.EditorUtils.GuideBoard");
                check(!assembly.GetReferencedAssemblies().Any(a => a.Name.Contains("TextMeshPro") || a.Name == "Unity.ugui"), "guide_board_has_no_tmp_or_ugui_reference");
                settings_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_settings", true);
                preview_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_preview", true);
                exporter_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_exporter", true);
                asset_directory = "Assets/guide_board_dll_check_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(asset_directory));

                var settings = Activator.CreateInstance(settings_type, true);
                settings_type.GetField("text", members).SetValue(settings,
                    "衣装のセットアップ\n\nこの位置にギミックを配置します。\n設定が終わったら確認してください。");
                var scene = SceneManager.GetActiveScene();
                var was_dirty = scene.isDirty;
                int preview_scenes = EditorSceneManager.previewSceneCount;
                var installed_fonts = (string[])preview_type.GetMethod("get_font_families", members).Invoke(null, null);
                check(installed_fonts.Length > 0, "installed_windows_fonts_are_listed");
                using (var preview = (IDisposable)Activator.CreateInstance(preview_type, true))
                {
                    invoke(preview, "render", settings);
                    check(get<bool>(preview, "text_overflows") == false, "japanese_text_fits");
                    check(get<int>(preview, "render_count") == 1, "one_render_per_request");
                    var png = (byte[])invoke(preview, "encode_png");
                    var image = new Texture2D(2, 2);
                    try
                    {
                        check(image.LoadImage(png) && image.width == 1024 && image.height == 512, "dll_returns_expected_png");
                        check(image.GetPixel(5, 5).r > 0.97f, "white_background");
                        int dark = image.GetPixels32().Count(p => p.r < 150 && p.g < 150 && p.b < 150);
                        check(dark > 500 && dark < 60000, "japanese_text_pixels");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                    File.WriteAllBytes("Logs/guide_board_dll_japanese.png", png);

                    var sprite = save(asset_directory + "/guide_board.png", png);
                    var image_path = AssetDatabase.GetAssetPath(sprite);
                    var original_guid = AssetDatabase.AssetPathToGUID(image_path);
                    check(sprite != null && image_path == asset_directory + "/guide_board.png", "sprite_png_saved");
                    check(sprite.rect.width == 1024 && sprite.rect.height == 512 &&
                        Mathf.Abs(sprite.bounds.size.x - 2f) < 0.0001f &&
                        Mathf.Abs(sprite.bounds.size.y - 1f) < 0.0001f,
                        "sprite_preserves_image_dimensions_and_world_size");
                    var importer = (TextureImporter)AssetImporter.GetAtPath(image_path);
                    var sprite_settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(sprite_settings);
                    check(importer.textureType == TextureImporterType.Sprite &&
                        importer.spriteImportMode == SpriteImportMode.Single &&
                        sprite_settings.spriteMeshType == SpriteMeshType.FullRect &&
                        importer.spritePixelsPerUnit == 512f &&
                        importer.sRGBTexture && !importer.mipmapEnabled &&
                        importer.textureCompression == TextureImporterCompression.Uncompressed,
                        "sprite_import_settings");
                    var second = save(asset_directory + "/guide_board.png", png);
                    check(AssetDatabase.GetAssetPath(second) != image_path &&
                        AssetDatabase.AssetPathToGUID(image_path) == original_guid &&
                        AssetDatabase.FindAssets("t:Material", new[] { asset_directory }).Length == 0 &&
                        AssetDatabase.FindAssets("t:Prefab", new[] { asset_directory }).Length == 0,
                        "same_name_is_unique_and_only_sprites_are_saved");
                    expect_rejection("Assets/../outside.png", png);
                    expect_rejection(asset_directory + "/guide_board.prefab", png);
                    check(EditorSceneManager.previewSceneCount == preview_scenes && SceneManager.GetActiveScene() == scene && scene.isDirty == was_dirty,
                        "user_scene_is_unchanged");

                    settings_type.GetField("text", members).SetValue(settings, "FONT CHOICE ABC 123");
                    settings_type.GetField("font_name", members).SetValue(settings, installed_fonts[0]);
                    invoke(preview, "render", settings);
                    check(get<int>(preview, "render_count") == 2 && ((byte[])invoke(preview, "encode_png")).Length > 0,
                        "selected_font_renders_png");
                    settings_type.GetField("font_name", members).SetValue(settings, "missing_font_for_guide_board_check");
                    try { invoke(preview, "render", settings); throw new InvalidOperationException("Missing font was accepted."); }
                    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
                    settings_type.GetField("font_name", members).SetValue(settings, string.Empty);
                    check(get<int>(preview, "render_count") == 2, "missing_font_is_rejected");

                    settings_type.GetField("text", members).SetValue(settings, new string('長', 3000));
                    invoke(preview, "render", settings);
                    check(get<bool>(preview, "text_overflows"), "long_text_overflow_detected");
                    check(get<int>(preview, "render_count") == 3, "preview_updates_after_text_change");
                }

                var window_type = assembly.GetType("D9speed_BaseEditorUtils.GuideBoard.guide_board_window", true);
                var window = (EditorWindow)ScriptableObject.CreateInstance(window_type);
                try
                {
                    invoke(window, "CreateGUI");
                    check(window.rootVisualElement.Q<TextField>("text_field") != null &&
                        window.rootVisualElement.Q<Image>("preview_image") != null &&
                        window.rootVisualElement.Q<Button>("export_button") != null &&
                        window.rootVisualElement.Q<Button>("prefab_button") == null,
                        "unity_ui_has_sprite_save_only");
                    var font_field = window.rootVisualElement.Q<DropdownField>("font_field");
                    check(font_field != null && font_field.choices.Count > 1 &&
                        window.rootVisualElement.Q<VisualElement>("width_field") == null,
                        "unity_ui_lists_fonts_without_layout_controls");
                    invoke(window, "select_font", installed_fonts[0]);
                    var window_settings = window_type.GetField("settings", members).GetValue(window);
                    check((string)settings_type.GetField("font_name", members).GetValue(window_settings) == installed_fonts[0],
                        "font_selection_updates_settings");
                    invoke(window, "select_font", font_field.choices[0]);
                    check((string)settings_type.GetField("font_name", members).GetValue(window_settings) == string.Empty,
                        "automatic_font_selection_is_preserved");
                    check(window.rootVisualElement.ClassListContains("d9_ui_root"), "shared_theme_applied");
                    invoke(window, "render_preview");
                    check(window.rootVisualElement.Q<Button>("export_button").enabledSelf, "preview_enables_sprite_save");
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
                check(EditorSceneManager.previewSceneCount == preview_scenes, "preview_resources_released");

                var menu = core.GetType("D9speed_BaseEditorUtils.guide_board_menu", true)
                    .GetMethod("open_window", members).GetCustomAttribute<MenuItem>();
                check(menu.menuItem == "D9speed/Tools/説明画像をSpriteで作成", "sprite_menu_path");
                finish(null);
            }
            catch (Exception exception) { finish(exception); }
        }

        private static Sprite save(string path, byte[] png) =>
            (Sprite)exporter_type.GetMethod("save_sprite", members).Invoke(null, new object[] { path, png });

        private static object invoke(object target, string method, params object[] values) =>
            target.GetType().GetMethod(method, members).Invoke(target, values);

        private static T get<T>(object target, string name) =>
            (T)target.GetType().GetProperty(name, members).GetValue(target);

        private static void expect_rejection(string path, byte[] png)
        {
            try { save(path, png); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { return; }
            throw new InvalidOperationException("Invalid output path was accepted.");
        }

        private static void check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed.Add(description);
            Debug.Log("GUIDE_BOARD_DLL_CHECK: " + description);
        }

        private static void finish(Exception exception)
        {
            Directory.CreateDirectory("Logs");
            var report = (exception == null ? "PASS" : "FAIL") + "\nUnity " + Application.unityVersion +
                "\n" + string.Join("\n", passed);
            if (exception != null) { report += "\n" + exception; Debug.LogException(exception); }
            File.WriteAllText("Logs/guide_board_dll_result.txt", report);
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
