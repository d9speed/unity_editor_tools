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

                    var prefab = save(asset_directory + "/guide_board.prefab", png);
                    var image_path = asset_directory + "/guide_board_image.png";
                    var material_path = asset_directory + "/guide_board_material.mat";
                    var original_guids = new[]
                    {
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)),
                        AssetDatabase.AssetPathToGUID(image_path),
                        AssetDatabase.AssetPathToGUID(material_path)
                    };
                    check(prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab) && prefab.CompareTag("EditorOnly"), "editor_only_prefab_saved");
                    check(prefab.GetComponent<Collider>() == null && prefab.GetComponents<Component>().Length == 3, "quad_without_runtime_components");
                    check(prefab.transform.localScale == new Vector3(2f, 1f, 1f), "prefab_preserves_image_aspect");
                    var renderer = prefab.GetComponent<MeshRenderer>();
                    check(renderer.sharedMaterial.shader.name == "Unlit/Texture" && AssetDatabase.GetAssetPath(renderer.sharedMaterial.mainTexture) == image_path,
                        "prefab_references_persistent_png_and_material");
                    var dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true);
                    check(!dependencies.Any(path => path.EndsWith(".cs") || path.EndsWith(".dll") || path.Contains("TextMesh Pro")),
                        "prefab_has_no_tool_or_font_dependency");
                    var importer = (TextureImporter)AssetImporter.GetAtPath(image_path);
                    check(importer.sRGBTexture && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed,
                        "png_import_settings");
                    var second = save(asset_directory + "/guide_board.prefab", png);
                    check(AssetDatabase.GetAssetPath(second) != AssetDatabase.GetAssetPath(prefab) &&
                        original_guids.SequenceEqual(new[]
                        {
                            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)),
                            AssetDatabase.AssetPathToGUID(image_path),
                            AssetDatabase.AssetPathToGUID(material_path)
                        }), "same_name_does_not_replace_assets");
                    expect_rejection("Assets/../outside.prefab", png);
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
                        window.rootVisualElement.Q<Button>("prefab_button") != null,
                        "unity_ui_has_text_preview_and_save");
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
                    check(window.rootVisualElement.Q<Button>("prefab_button").enabledSelf, "preview_enables_prefab_save");
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
                check(EditorSceneManager.previewSceneCount == preview_scenes, "preview_resources_released");

                var menu = core.GetType("D9speed_BaseEditorUtils.guide_board_menu", true)
                    .GetMethod("open_window", members).GetCustomAttribute<MenuItem>();
                check(menu.menuItem == "D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)", "requested_menu_path_unchanged");
                finish(null);
            }
            catch (Exception exception) { finish(exception); }
        }

        private static GameObject save(string path, byte[] png) =>
            (GameObject)exporter_type.GetMethod("save_prefab", members).Invoke(null, new object[] { path, png });

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
