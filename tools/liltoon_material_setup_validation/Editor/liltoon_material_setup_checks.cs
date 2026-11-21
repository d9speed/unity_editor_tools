using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using D9speed_BaseEditorUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed.PackageValidation
{
    public static class liltoon_material_setup_checks
    {
        private static readonly List<string> passed = new List<string>();
        private static string fixture;
        private static string materials;
        private static string textures;

        public static void review()
        {
            EditorApplication.delayCall += () =>
            {
                string root = File.ReadAllText("Logs/liltoon_setup_fixture.txt");
                if (!EditorApplication.ExecuteMenuItem(LilToonMaterialSetup.menu_path)) throw new InvalidOperationException("Menu not registered");
                var window = EditorWindow.GetWindow<LilToonMaterialSetupWindow>();
                window.position = new Rect(80, 60, 720, 840);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(LilToonMaterialSetupWindow).GetField("material_folder", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<DefaultAsset>(root + "/materials"));
                typeof(LilToonMaterialSetupWindow).GetField("texture_folder", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<DefaultAsset>(root + "/textures"));
                window.CreateGUI();
                typeof(LilToonMaterialSetupWindow).GetMethod("build_preview", flags).Invoke(window, null);
                window.Show(); window.Focus();
                window.rootVisualElement.schedule.Execute(() =>
                {
                    var button = window.rootVisualElement.Q<Button>("apply_button");
                    File.WriteAllText("Logs/ui_review_ready.txt", "window=" + window.position + "\napply=" + button.worldBound);
                }).StartingIn(1500);
            };
        }

        public static void run()
        {
            try
            {
                fixture = "Assets/setup_checks_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
                materials = fixture + "/materials"; textures = fixture + "/textures";
                AssetDatabase.CreateFolder(fixture, "materials"); AssetDatabase.CreateFolder(fixture, "textures");
                check(Shader.Find("lilToon") != null, "lilToon 2.4.1 is available");
                check(!LilToonMaterialSetup.is_assets_folder("Packages"), "Packages folders rejected");
                var material = new Material(Shader.Find("Standard")) { name = "jacket" };
                string material_path = materials + "/jacket.mat";
                AssetDatabase.CreateAsset(material, material_path);
                var untouched = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(untouched, materials + "/untouched.mat");
                var template = new Material(Shader.Find("lilToon"));
                template.SetFloat("_BumpScale", 0.42f);
                template.renderQueue = 2460;
                AssetDatabase.CreateAsset(template, materials + "/template.mat");
                foreach (var map in LilToonMaterialSetup.maps) write_texture(textures + "/jacket_" + map.suffix + ".png");
                var matcap = AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "/jacket_BaseColor.png");
                template.SetTexture("_MatCapTex", matcap); template.SetTexture("_MatCap2ndTex", matcap);
                AssetDatabase.SaveAssets();
                string material_guid = AssetDatabase.AssetPathToGUID(material_path);
                string original_material = Convert.ToBase64String(File.ReadAllBytes(material_path));
                var original_metas = LilToonMaterialSetup.maps.ToDictionary(m => m.suffix,
                    m => Convert.ToBase64String(File.ReadAllBytes(textures + "/jacket_" + m.suffix + ".png.meta")));
                string rough_path = textures + "/jacket_Roughness.png";
                string original_roughness = Convert.ToBase64String(File.ReadAllBytes(rough_path));
                var go = new GameObject("reference_probe");
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                string prefab_path = fixture + "/reference_probe.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefab_path);
                UnityEngine.Object.DestroyImmediate(go);
                var options = new LilToonSetupOptions { material_folder = materials, texture_folder = textures, template = template };
                var plan = LilToonMaterialSetup.scan(options);
                check(plan.errors.Count == 0 && plan.rows.Count == 2, "Template excluded; standalone .mat files found");
                var jacket = plan.rows.Single(r => r.path == material_path);
                check(jacket.can_apply && jacket.bindings.Count == 10, "All screenshot suffixes and NormalMap2 matched");
                check(!plan.rows.Single(r => r.path.EndsWith("untouched.mat")).can_apply, "Unmatched material skipped");
                check(original_material == Convert.ToBase64String(File.ReadAllBytes(material_path)), "Preview does not edit material");
                check(original_metas.All(p => p.Value == Convert.ToBase64String(File.ReadAllBytes(textures + "/jacket_" + p.Key + ".png.meta"))), "Preview does not edit importers");
                string backup = LilToonMaterialSetup.apply(plan);
                check(material.shader.name == "lilToon", "Existing material converted to lilToon");
                check(material_guid == AssetDatabase.AssetPathToGUID(material_path), "Material GUID preserved");
                check(AssetDatabase.LoadAssetAtPath<GameObject>(prefab_path).GetComponent<MeshRenderer>().sharedMaterial == material, "Prefab reference preserved");
                check(material.renderQueue == 2460 && Mathf.Approximately(material.GetFloat("_BumpScale"), 0.42f), "Template queue and intensity preserved");
                foreach (var map in LilToonMaterialSetup.maps)
                {
                    string path = textures + "/jacket_" + map.suffix + ".png";
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    check(importer.sRGBTexture == map.srgb, map.suffix + " sRGB=" + map.srgb);
                    check(importer.textureType == (map.normal ? TextureImporterType.NormalMap : TextureImporterType.Default), map.suffix + " texture type");
                    if (map.suffix != "Roughness") check(AssetDatabase.GetAssetPath(material.GetTexture(map.property)) == path, map.suffix + " assigned");
                    if (!string.IsNullOrEmpty(map.feature)) check(material.GetFloat(map.feature) == 1, map.suffix + " feature enabled");
                }
                check(material.GetFloat("_UseShadow") == 1 && material.GetFloat("_UseMain2ndTex") == 0, "Default AO uses shadow AO without consuming main color 2nd");
                string generated = AssetDatabase.GetAssetPath(material.GetTexture("_SmoothnessTex"));
                check(generated.StartsWith(textures + "/_liltoon_generated/"), "Smoothness generated separately");
                check(!((TextureImporter)AssetImporter.GetAtPath(generated)).sRGBTexture, "Generated smoothness is linear");
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                ImageConversion.LoadImage(decoded, File.ReadAllBytes(generated));
                var pixel = decoded.GetPixels32()[0];
                check(pixel.r == 223 && pixel.g == 223 && pixel.b == 223 && pixel.a == 255, "Smoothness = 255 - roughness R, no gamma conversion");
                UnityEngine.Object.DestroyImmediate(decoded);
                check(original_roughness == Convert.ToBase64String(File.ReadAllBytes(rough_path)), "Source Roughness bytes unchanged");
                string generated_guid = AssetDatabase.AssetPathToGUID(generated);
                string generated_bytes = Convert.ToBase64String(File.ReadAllBytes(generated));
                options.template = null;
                options.normal_directx = true;
                plan = LilToonMaterialSetup.scan(options);
                LilToonMaterialSetup.apply(plan);
                check(generated_guid == AssetDatabase.AssetPathToGUID(generated), "Repeated setup preserves generated texture GUID");
                check(generated_bytes == Convert.ToBase64String(File.ReadAllBytes(generated)), "Repeated setup does not invert twice");
                check(((TextureImporter)AssetImporter.GetAtPath(textures + "/jacket_NormalMap.png")).flipGreenChannel, "DirectX normal flips green");
                LilToonSetupBackup.restore(backup);
                check(original_material == Convert.ToBase64String(File.ReadAllBytes(material_path)), "Restore recovers original material bytes");
                check(original_metas.All(p => p.Value == Convert.ToBase64String(File.ReadAllBytes(textures + "/jacket_" + p.Key + ".png.meta"))), "Restore recovers all importer bytes");
                check(!File.Exists(generated) && !File.Exists(generated + ".meta"), "Restore removes generated texture and metadata");
                check(AssetDatabase.LoadAssetAtPath<Material>(material_path).shader.name == "Standard", "Restore reloads material shader");

                options.template = template;
                options.ao_mode = LilToonAoMode.MainColor2nd;
                backup = LilToonMaterialSetup.apply(LilToonMaterialSetup.scan(options));
                check(material.GetFloat("_UseMain2ndTex") == 1 && material.GetFloat("_Main2ndTexBlendMode") == 3 &&
                    AssetDatabase.GetAssetPath(material.GetTexture("_Main2ndTex")) == textures + "/jacket_AOMap.png", "Optional AO multiply mode assigns main color 2nd");
                LilToonSetupBackup.restore(backup);
                var duplicate_folder = textures + "/duplicate";
                AssetDatabase.CreateFolder(textures, "duplicate");
                write_texture(duplicate_folder + "/jacket_BaseColor.png");
                plan = LilToonMaterialSetup.scan(options);
                check(!plan.rows.Single(r => r.path == material_path).can_apply, "Duplicate texture blocks material instead of guessing");
                AssetDatabase.MoveAsset(duplicate_folder + "/jacket_BaseColor.png", duplicate_folder + "/unused_BaseColor.png");
                plan = LilToonMaterialSetup.scan(options);
                check(plan.rows.Single(r => r.path == material_path).can_apply, "Unrelated texture does not interfere");
                template.SetTexture("_Main2ndTex", matcap); template.SetFloat("_UseMain2ndTex", 1);
                plan = LilToonMaterialSetup.scan(options);
                check(!plan.rows.Single(r => r.path == material_path).can_apply, "Existing main color 2nd protected from AO overwrite");
                options.ao_mode = LilToonAoMode.ImportOnly;
                plan = LilToonMaterialSetup.scan(options);
                check(plan.rows.Single(r => r.path == material_path).can_apply, "AO import-only resolves layer conflict");
                backup = LilToonMaterialSetup.apply(plan);
                check(material.GetTexture("_Main2ndTex") == matcap && !((TextureImporter)AssetImporter.GetAtPath(textures + "/jacket_AOMap.png")).sRGBTexture, "Import-only keeps layer but disables AO sRGB");
                LilToonSetupBackup.restore(backup);
                options.ao_mode = LilToonAoMode.D9Ao;
                check(!LilToonMaterialSetup.scan(options).rows.Single(r => r.path == material_path).can_apply, "Missing D9 AO shader property detected");
                options.ao_mode = LilToonAoMode.MainColor2nd;
                template.SetTexture("_Main2ndTex", null); template.SetFloat("_UseMain2ndTex", 0);
                plan = LilToonMaterialSetup.scan(options);
                material.SetColor("_Color", Color.red);
                bool stale_rejected = false;
                try { LilToonMaterialSetup.apply(plan); } catch (InvalidOperationException) { stale_rejected = true; }
                check(stale_rejected, "Unsaved material changes invalidate reviewed preview");
                material.SetColor("_Color", Color.white); AssetDatabase.SaveAssetIfDirty(material);
                plan = LilToonMaterialSetup.scan(options);
                string before_failure = Convert.ToBase64String(File.ReadAllBytes(material_path));
                string before_failure_meta = Convert.ToBase64String(File.ReadAllBytes(textures + "/jacket_NormalMap.png.meta"));
                bool failure = false;
                using (var locked = new FileStream(rough_path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    try { LilToonMaterialSetup.apply(plan); } catch (IOException) { failure = true; }
                }
                check(failure, "Locked Roughness causes a controlled failure");
                check(before_failure == Convert.ToBase64String(File.ReadAllBytes(material_path)) &&
                    before_failure_meta == Convert.ToBase64String(File.ReadAllBytes(textures + "/jacket_NormalMap.png.meta")), "Failed setup rolls back material and importer changes");
                check(!File.Exists(generated), "Failed setup leaves no generated texture");

                var window = ScriptableObject.CreateInstance<LilToonMaterialSetupWindow>();
                window.CreateGUI();
                check(window.rootVisualElement.Q<Button>("apply_button") != null, "UI Toolkit layout imported");
                check(!window.rootVisualElement.Q<Button>("apply_button").enabledSelf, "Apply disabled with no reviewed targets");
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(LilToonMaterialSetupWindow).GetField("material_folder", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<DefaultAsset>(materials));
                typeof(LilToonMaterialSetupWindow).GetField("texture_folder", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<DefaultAsset>(textures));
                typeof(LilToonMaterialSetupWindow).GetMethod("build_preview", flags).Invoke(window, null);
                check(window.rootVisualElement.Q<Button>("apply_button").enabledSelf, "Apply enabled after successful preview");
                check(window.rootVisualElement.Q<VisualElement>("preview").childCount >= 2, "Preview includes per-material cards");
                UnityEngine.Object.DestroyImmediate(window);
                File.WriteAllText("Logs/liltoon_setup_results.txt", string.Join("\n", passed) + "\nPASS " + passed.Count + " checks\nFixture: " + fixture);
                File.WriteAllText("Logs/liltoon_setup_fixture.txt", fixture);
                Debug.Log("PASS liltoon material setup: " + passed.Count);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText("Logs/liltoon_setup_results.txt", string.Join("\n", passed) + "\nFAIL " + ex);
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void write_texture(string path)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(Enumerable.Repeat(new Color32(32, 64, 128, 255), 16).ToArray());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed.Add("PASS " + message);
        }
    }
}
