using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    public enum LilToonAoMode { ShadowAo, MainColor2nd, D9Ao, ImportOnly }

    [Serializable]
    public sealed class LilToonSetupOptions
    {
        public string material_folder = "";
        public string texture_folder = "";
        public Material template;
        public bool normal_directx;
        public LilToonAoMode ao_mode;
    }

    public sealed class LilToonSetupMap
    {
        public string suffix;
        public string property;
        public string feature;
        public bool srgb;
        public bool normal;
        public LilToonSetupMap(string suffix, string property, string feature = "", bool srgb = false, bool normal = false)
        {
            this.suffix = suffix; this.property = property; this.feature = feature;
            this.srgb = srgb; this.normal = normal;
        }
    }

    public sealed class LilToonSetupBinding
    {
        public LilToonSetupMap map;
        public string path;
        public string property;
    }

    public sealed class LilToonSetupRow
    {
        public Material material;
        public string path;
        public string shader_name;
        public bool selected = true;
        public readonly List<LilToonSetupBinding> bindings = new List<LilToonSetupBinding>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> errors = new List<string>();
        public bool can_apply => bindings.Count > 0 && errors.Count == 0;
    }

    public sealed class LilToonSetupPlan
    {
        public LilToonSetupOptions options;
        public string fingerprint;
        public readonly List<LilToonSetupRow> rows = new List<LilToonSetupRow>();
        public readonly List<string> errors = new List<string>();
    }

    public static class LilToonMaterialSetup
    {
        public const string menu_path = "D9speed/Tools/liltoonマテリアルセットアップ";
        public static readonly LilToonSetupMap[] maps =
        {
            new LilToonSetupMap("BaseColor", "_MainTex", srgb: true),
            new LilToonSetupMap("NormalMap", "_BumpMap", "_UseBumpMap", normal: true),
            new LilToonSetupMap("MatCapMask", "_MatCapBlendMask", "_UseMatCap"),
            new LilToonSetupMap("Emission", "_EmissionMap", "_UseEmission", srgb: true),
            new LilToonSetupMap("AOMap", "_ShadowBorderMask"),
            new LilToonSetupMap("Roughness", "_SmoothnessTex", "_UseReflection"),
            new LilToonSetupMap("MatCap2", "_MatCap2ndBlendMask", "_UseMatCap2nd"),
            new LilToonSetupMap("NormalMap2", "_Bump2ndMap", "_UseBump2ndMap", normal: true),
            new LilToonSetupMap("NormalMap2Mask", "_Bump2ndScaleMask"),
            new LilToonSetupMap("RimLightMask", "_RimColorTex", "_UseRim")
        };

        public static bool is_liltoon(Material material)
        {
            return material != null && material.shader != null &&
                material.shader.name.IndexOf("liltoon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                material.HasProperty("_MainTex") && material.HasProperty("_UseBumpMap") &&
                material.HasProperty("_UseMain2ndTex") && material.HasProperty("_MatCap2ndBlendMask");
        }

        public static bool is_assets_folder(string path) =>
            !string.IsNullOrEmpty(path) && (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) &&
            !path.Split('/').Any(part => part == ".." || part == ".") && AssetDatabase.IsValidFolder(path);

        public static LilToonSetupPlan scan(LilToonSetupOptions options)
        {
            var plan = new LilToonSetupPlan { options = new LilToonSetupOptions
            {
                material_folder = options.material_folder, texture_folder = options.texture_folder,
                template = options.template, normal_directx = options.normal_directx, ao_mode = options.ao_mode
            }};
            if (!is_assets_folder(options.material_folder)) plan.errors.Add("Assets内のマテリアルフォルダを指定してください。");
            if (!is_assets_folder(options.texture_folder)) plan.errors.Add("Assets内のテクスチャフォルダを指定してください。");
            if (options.template != null && !is_liltoon(options.template)) plan.errors.Add("雛形には通常版lilToon系のマテリアルを指定してください（Liteは対象外）。");
            if (plan.errors.Count > 0) return plan;

            var texture_paths = AssetDatabase.FindAssets("t:Texture2D", new[] { options.texture_folder })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var index = texture_paths.Where(p => !p.Contains("/_liltoon_generated/"))
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
            var material_paths = AssetDatabase.FindAssets("t:Material", new[] { options.material_folder })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var fingerprint_parts = new List<string>
            {
                options.material_folder, options.texture_folder, options.normal_directx.ToString(), options.ao_mode.ToString()
            };
            foreach (var path in material_paths.Concat(texture_paths).Concat(new[] { AssetDatabase.GetAssetPath(options.template) }).Where(p => !string.IsNullOrEmpty(p)))
                fingerprint_parts.Add(path + ":" + AssetDatabase.GetAssetDependencyHash(path));
            if (options.template != null) fingerprint_parts.Add(EditorJsonUtility.ToJson(options.template));

            foreach (var path in material_paths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material == options.template) continue;
                fingerprint_parts.Add(EditorJsonUtility.ToJson(material));
                var row = new LilToonSetupRow { material = material, path = path };
                plan.rows.Add(row);
                Material target = null;
                try
                {
                    target = make_target(material, options.template);
                    row.shader_name = target.shader.name;
                    string stem = Path.GetFileNameWithoutExtension(path);
                    foreach (var map in maps)
                    {
                        if (!index.TryGetValue(stem + "_" + map.suffix, out var candidates)) continue;
                        if (candidates.Length != 1)
                        {
                            row.errors.Add(map.suffix + " が重複: " + string.Join(" / ", candidates));
                            continue;
                        }
                        string property = map.property;
                        if (map.suffix == "AOMap")
                        {
                            property = options.ao_mode == LilToonAoMode.ImportOnly ? "" :
                                options.ao_mode == LilToonAoMode.D9Ao ? "_d9_ao_map" :
                                options.ao_mode == LilToonAoMode.ShadowAo ? "_ShadowBorderMask" : "_Main2ndTex";
                            if (property == "_Main2ndTex" && target.GetFloat("_UseMain2ndTex") > 0 &&
                                target.GetTexture(property) != null &&
                                AssetDatabase.GetAssetPath(target.GetTexture(property)) != candidates[0])
                            {
                                row.errors.Add("メインカラー2ndが使用中です。AOの割り当て方法を変更してください。");
                            }
                        }
                        if (property != "" && !target.HasProperty(property))
                        {
                            row.errors.Add(map.suffix + " の設定先がこのシェーダーにありません: " + property);
                            continue;
                        }
                        if (!(AssetImporter.GetAtPath(candidates[0]) is TextureImporter))
                        {
                            row.errors.Add(map.suffix + " は画像ファイルとしてインポートされていません。");
                            continue;
                        }
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(candidates[0]);
                        if (texture == null || texture.dimension != UnityEngine.Rendering.TextureDimension.Tex2D)
                        {
                            row.errors.Add(map.suffix + " は通常の2Dテクスチャではありません。");
                            continue;
                        }
                        if (map.suffix == "Roughness" && !new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(candidates[0]).ToLowerInvariant()))
                        {
                            row.errors.Add("Roughnessの変換はPNG / JPEGに対応しています。");
                            continue;
                        }
                        row.bindings.Add(new LilToonSetupBinding { map = map, path = candidates[0], property = property });
                    }
                    if (!row.bindings.Any(b => b.map.suffix == "BaseColor")) row.warnings.Add("BaseColorなし（現在の設定を維持）");
                    foreach (var pair in new[] { new[] { "MatCapMask", "_MatCapTex" }, new[] { "MatCap2", "_MatCap2ndTex" } })
                        if (row.bindings.Any(b => b.map.suffix == pair[0]) && target.GetTexture(pair[1]) == null)
                            row.warnings.Add(pair[0] + ": MatCap本体が未設定のため、マスクのみ割り当てます。");
                    if (row.bindings.Any(b => b.map.suffix == "NormalMap2Mask") &&
                        !row.bindings.Any(b => b.map.suffix == "NormalMap2") && target.GetTexture("_Bump2ndMap") == null)
                        row.warnings.Add("NormalMap2が未設定のため、2ndのマスクのみ割り当てます。");
                    if (row.bindings.Count == 0) row.selected = false;
                    if (!AssetDatabase.IsOpenForEdit(path)) row.errors.Add("マテリアルは読み取り専用です。");
                    foreach (var binding in row.bindings)
                        if (!AssetDatabase.IsOpenForEdit(binding.path + ".meta")) row.errors.Add("インポート設定が読み取り専用: " + binding.path);
                }
                catch (Exception ex) { row.errors.Add(ex.Message); }
                finally { if (target != null) UnityEngine.Object.DestroyImmediate(target); }
            }
            plan.fingerprint = Hash128.Compute(string.Join("\n", fingerprint_parts)).ToString();
            return plan;
        }

        private static Material make_target(Material material, Material template)
        {
            if (template != null) return new Material(template);
            if (is_liltoon(material)) return new Material(material);
            var shader = Shader.Find("lilToon");
            if (shader == null) throw new InvalidOperationException("lilToonが見つかりません。先にlilToonを導入してください。");
            var target = new Material(shader);
            if (material.HasProperty("_MainTex"))
            {
                target.SetTexture("_MainTex", material.GetTexture("_MainTex"));
                target.SetTextureScale("_MainTex", material.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_MainTex", material.GetTextureOffset("_MainTex"));
            }
            if (material.HasProperty("_Color")) target.SetColor("_Color", material.GetColor("_Color"));
            return target;
        }

        public static string smoothness_path(string source_path, string texture_folder) =>
            texture_folder + "/_liltoon_generated/" + Path.GetFileNameWithoutExtension(source_path) +
            "_smoothness_" + AssetDatabase.AssetPathToGUID(source_path).Substring(0, 8) + ".png";

        public static string apply(LilToonSetupPlan reviewed_plan)
        {
            if (reviewed_plan == null || reviewed_plan.errors.Count > 0) throw new InvalidOperationException("先に割り当てを確認してください。");
            var fresh = scan(reviewed_plan.options);
            if (fresh.fingerprint != reviewed_plan.fingerprint || fresh.errors.Count > 0)
                throw new InvalidOperationException("確認後にアセットが変更されました。割り当てを更新してから実行してください。");
            var selected = new HashSet<string>(reviewed_plan.rows.Where(r => r.selected && r.can_apply).Select(r => r.path));
            var rows = fresh.rows.Where(r => selected.Contains(r.path) && r.can_apply).ToArray();
            if (rows.Length == 0) throw new InvalidOperationException("適用できるマテリアルがありません。");
            var options = fresh.options;
            var paths = rows.Select(r => r.path).ToList();
            foreach (var row in rows)
            {
                AssetDatabase.SaveAssetIfDirty(row.material);
                foreach (var binding in row.bindings)
                {
                    paths.Add(binding.path + ".meta");
                    if (binding.map.suffix == "Roughness")
                    {
                        string output = smoothness_path(binding.path, options.texture_folder);
                        var existing_importer = AssetImporter.GetAtPath(output) as TextureImporter;
                        if (File.Exists(output) && (existing_importer == null || existing_importer.userData != "d9_liltoon_smoothness:" + AssetDatabase.AssetPathToGUID(binding.path)))
                            throw new InvalidOperationException("変換先に別の画像があります: " + output);
                        paths.Add(output); paths.Add(output + ".meta");
                    }
                }
            }
            string backup = LilToonSetupBackup.capture(paths);
            try
            {
                var imported = new HashSet<string>();
                foreach (var row in rows)
                {
                    var target = make_target(row.material, options.template);
                    try
                    {
                        foreach (var binding in row.bindings)
                        {
                            if (imported.Add(binding.path)) configure_import(binding.path, binding.map.srgb, binding.map.normal, options.normal_directx);
                            if (binding.property == "") continue;
                            var texture = binding.map.suffix == "Roughness"
                                ? convert_roughness(binding.path, options.texture_folder)
                                : AssetDatabase.LoadAssetAtPath<Texture2D>(binding.path);
                            target.SetTexture(binding.property, texture);
                            apply_feature(target, binding, options);
                        }
                        // Updating the existing asset keeps its GUID, local ID and renderer references.
                        row.material.shader = target.shader;
                        row.material.CopyPropertiesFromMaterial(target);
                        setup_multi_keywords(row.material);
                        EditorUtility.SetDirty(row.material);
                        AssetDatabase.SaveAssetIfDirty(row.material);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(target); }
                }
                LilToonSetupBackup.mark_latest(backup);
                return backup;
            }
            catch
            {
                LilToonSetupBackup.restore(backup);
                throw;
            }
        }

        public static void configure_import(string path, bool srgb, bool normal = false, bool directx = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("画像のインポート設定を取得できません: " + path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool changed = importer.textureType != type || importer.sRGBTexture != srgb ||
                (normal && (importer.flipGreenChannel != directx || importer.convertToNormalmap));
            if (!changed) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            if (normal) { importer.flipGreenChannel = directx; importer.convertToNormalmap = false; }
            importer.SaveAndReimport();
        }

        private static Texture2D convert_roughness(string source_path, string texture_folder)
        {
            // Decode source bytes in linear space, before Unity's compression or resolution limit.
            // No gamma conversion, GPU readback, or Read/Write change on the source is required.
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Texture2D result = null;
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(source_path))) throw new IOException("Roughnessを読み込めません: " + source_path);
                var pixels = decoded.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte value = (byte)(255 - pixels[i].r);
                    pixels[i] = new Color32(value, value, value, 255);
                }
                result = new Texture2D(decoded.width, decoded.height, TextureFormat.RGBA32, false, true);
                result.SetPixels32(pixels); result.Apply();
                string folder = texture_folder + "/_liltoon_generated";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(texture_folder, "_liltoon_generated");
                string path = smoothness_path(source_path, texture_folder);
                File.WriteAllBytes(path, result.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.userData = "d9_liltoon_smoothness:" + AssetDatabase.AssetPathToGUID(source_path);
                var source_importer = (TextureImporter)AssetImporter.GetAtPath(source_path);
                importer.maxTextureSize = source_importer.maxTextureSize;
                importer.wrapMode = source_importer.wrapMode;
                importer.filterMode = source_importer.filterMode;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded);
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
            }
        }

        private static void apply_feature(Material target, LilToonSetupBinding binding, LilToonSetupOptions options)
        {
            string suffix = binding.map.suffix;
            if (suffix == "AOMap")
            {
                if (options.ao_mode == LilToonAoMode.ShadowAo)
                {
                    target.SetFloat("_UseShadow", 1);
                }
                else if (options.ao_mode == LilToonAoMode.MainColor2nd)
                {
                    target.SetFloat("_UseMain2ndTex", 1);
                    target.SetFloat("_Main2ndTexBlendMode", 3);
                    target.SetColor("_Color2nd", Color.white);
                    target.SetFloat("_Main2ndTexAlphaMode", 0);
                    target.SetFloat("_Main2ndEnableLighting", 1);
                }
                else if (options.ao_mode == LilToonAoMode.D9Ao)
                {
                    target.SetFloat("_d9_ao_enabled", 1);
                    target.SetFloat("_d9_ao_uv", 0);
                    target.SetFloat("_d9_ao_channel", 0);
                }
                return;
            }
            if (suffix == "MatCapMask" && target.GetTexture("_MatCapTex") == null) return;
            if (suffix == "MatCap2" && target.GetTexture("_MatCap2ndTex") == null) return;
            if (!string.IsNullOrEmpty(binding.map.feature)) target.SetFloat(binding.map.feature, 1);
            if (suffix == "NormalMap2Mask" && target.GetTexture("_Bump2ndMap") != null) target.SetFloat("_UseBump2ndMap", 1);
        }

        private static void setup_multi_keywords(Material material)
        {
            if (material.shader.name.IndexOf("multi", StringComparison.OrdinalIgnoreCase) < 0) return;
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("lilToon.lilMaterialUtils")).FirstOrDefault(t => t != null);
            var method = type?.GetMethod("SetupMultiMaterial", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            if (method == null) throw new InvalidOperationException("lilToon Multiの設定APIが見つかりません。");
            method.Invoke(null, new object[] { material });
        }
    }

    // A disk snapshot restores importers as well as materials; ordinary Material Undo cannot do both.
    public static class LilToonSetupBackup
    {
        private const string backup_root = "Library/d9_liltoon_material_setup";
        [Serializable] private sealed class Snapshot { public List<Entry> files = new List<Entry>(); }
        [Serializable] private sealed class Entry { public string path; public string copy; public bool existed; }
        public static string latest => File.Exists(backup_root + "/latest.txt") ? File.ReadAllText(backup_root + "/latest.txt") : "";

        public static string capture(IEnumerable<string> paths)
        {
            string directory = backup_root + "/" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(directory);
            var snapshot = new Snapshot();
            foreach (var path in paths.Distinct(StringComparer.Ordinal))
            {
                validate_path(path);
                var entry = new Entry { path = path, copy = snapshot.files.Count.ToString("D6"), existed = File.Exists(path) };
                if (entry.existed) File.Copy(path, directory + "/" + entry.copy);
                snapshot.files.Add(entry);
            }
            File.WriteAllText(directory + "/manifest.json", JsonUtility.ToJson(snapshot, true));
            return directory;
        }

        public static void mark_latest(string directory) => File.WriteAllText(backup_root + "/latest.txt", directory);

        public static void restore(string directory)
        {
            string full = Path.GetFullPath(directory);
            if (!full.StartsWith(Path.GetFullPath(backup_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("バックアップのパスが不正です。");
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(directory + "/manifest.json"));
            foreach (var entry in snapshot.files)
            {
                validate_path(entry.path);
                if (Path.GetFileName(entry.copy) != entry.copy || (entry.existed && !File.Exists(directory + "/" + entry.copy)))
                    throw new IOException("バックアップが不足しています。");
            }
            foreach (var entry in snapshot.files)
            {
                if (entry.existed) File.Copy(directory + "/" + entry.copy, entry.path, true);
                else if (File.Exists(entry.path)) File.Delete(entry.path);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (latest == directory) File.Delete(backup_root + "/latest.txt");
        }

        private static void validate_path(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !Path.GetFullPath(path).StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Assets外のファイルは操作できません: " + path);
        }
    }
}
