using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils
{
    public sealed class LilToonMaterialSetupWindow : EditorWindow
    {
        private const string ui_path = "Packages/io.github.d9speed.editor_core/Editor/liltoon_material_setup/";
        [SerializeField] private DefaultAsset material_folder;
        [SerializeField] private DefaultAsset texture_folder;
        [SerializeField] private Material template;
        [SerializeField] private bool normal_directx;
        [SerializeField] private LilToonAoMode ao_mode;
        private LilToonSetupPlan plan;
        private VisualElement preview;
        private HelpBox status;
        private Label count;
        private Button apply_button;
        private Button restore_button;
        private bool busy;

        [MenuItem(LilToonMaterialSetup.menu_path, false, 230)]
        public static void open_window()
        {
            GetWindow<LilToonMaterialSetupWindow>("liltoonマテリアルセットアップ");
        }

        public void CreateGUI()
        {
            minSize = new Vector2(540, 540);
            var root = rootVisualElement;
            root.Clear();
            EditorUiTheme.Apply(root);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ui_path + "liltoon_material_setup_window.uxml");
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(ui_path + "liltoon_material_setup_window.uss");
            if (tree == null || styles == null)
            {
                root.Add(new HelpBox("画面ファイルを読み込めません。Editor Coreを再導入してください。", HelpBoxMessageType.Error));
                return;
            }
            tree.CloneTree(root);
            root.styleSheets.Add(styles);
            var settings = root.Q<VisualElement>("settings");
            add_object_field<DefaultAsset>(settings, "マテリアルフォルダ", "material_folder", material_folder,
                value => { material_folder = value; invalidate(); });
            add_object_field<DefaultAsset>(settings, "テクスチャフォルダ", "texture_folder", texture_folder,
                value => { texture_folder = value; invalidate(); });
            add_object_field<Material>(settings, "雛形（任意）", "template", template,
                value => { template = value; invalidate(); });
            settings.Add(EditorUiControls.Label("Assets内のフォルダを指定してください。サブフォルダも検索します。雛形は適用対象から除外します。"));

            var options = root.Q<VisualElement>("options");
            var normals = new PopupField<string>("ノーマルの形式", new List<string> { "OpenGL（通常）", "DirectX（緑チャンネル反転）" }, normal_directx ? 1 : 0) { name = "normal_format" };
            normals.RegisterValueChangedCallback(_ => { normal_directx = normals.index == 1; invalidate(); });
            options.Add(EditorUiControls.Field(normals));
            var ao = new PopupField<string>("AOMapの割り当て", new List<string> { "影設定のAO Map（標準）", "メインカラー2ndに乗算", "D9speed AO拡張", "割り当てずsRGBのみオフ" }, (int)ao_mode) { name = "ao_mode" };
            ao.RegisterValueChangedCallback(_ => { ao_mode = (LilToonAoMode)ao.index; invalidate(); });
            options.Add(EditorUiControls.Field(ao));
            options.Add(EditorUiControls.Label("AO・Roughness・各マスクはsRGBオフ。BaseColor・Emissionはオン。NormalMapはNormal map形式にします。"));
            options.Add(EditorUiControls.Label("Roughnessは反転したSmoothness画像を生成します。MatCap本体と透過方式は雛形または既存マテリアルで設定してください。"));
            options.Add(EditorUiControls.Label("雛形なしでは既存のlilToon設定を維持し、それ以外のマテリアルは通常の不透明lilToonへ変更します。"));
            root.Q<VisualElement>("scan_actions").Add(EditorUiControls.Button("割り当てを確認 / 更新", build_preview));
            preview = root.Q<VisualElement>("preview");
            status = root.Q<HelpBox>("status");
            count = root.Q<Label>("count");
            var footer = root.Q<VisualElement>("footer");
            restore_button = EditorUiControls.Button("前回の設定を復元", restore_previous);
            restore_button.name = "restore_button";
            footer.Add(restore_button);
            apply_button = EditorUiControls.Button("一括セットアップ", apply_preview, true);
            apply_button.name = "apply_button";
            footer.Add(apply_button);
            invalidate();
        }

        private static void add_object_field<T>(VisualElement parent, string label, string name, T value, Action<T> changed) where T : UnityEngine.Object
        {
            var field = new ObjectField(label) { name = name, objectType = typeof(T), allowSceneObjects = false, value = value };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue as T));
            parent.Add(EditorUiControls.Field(field));
        }

        private void OnProjectChange() { if (!busy) invalidate(); }
        private void OnInspectorUpdate() { EditorUiTheme.RefreshTheme(rootVisualElement); }

        private void invalidate()
        {
            plan = null;
            if (preview == null) return;
            preview.Clear();
            preview.Add(EditorUiControls.Label("フォルダを指定して「割り当てを確認 / 更新」を押してください。"));
            show_status("マテリアル名_BaseColor.png など、マテリアル名と末尾の用途名で照合します。", HelpBoxMessageType.Info);
            update_buttons();
        }

        private void build_preview()
        {
            plan = LilToonMaterialSetup.scan(new LilToonSetupOptions
            {
                material_folder = AssetDatabase.GetAssetPath(material_folder),
                texture_folder = AssetDatabase.GetAssetPath(texture_folder),
                template = template, normal_directx = normal_directx, ao_mode = ao_mode
            });
            preview.Clear();
            if (plan.errors.Count > 0)
            {
                show_status(string.Join("\n", plan.errors), HelpBoxMessageType.Error);
                update_buttons();
                return;
            }
            foreach (var row in plan.rows)
            {
                var card = new VisualElement();
                card.AddToClassList("d9_section");
                var header = EditorUiControls.Row(false);
                header.AddToClassList("setup_card_header");
                var toggle = EditorUiControls.Toggle("", row.selected && row.can_apply, value => { row.selected = value; update_buttons(); });
                toggle.SetEnabled(row.can_apply);
                header.Add(toggle);
                var material = new ObjectField { objectType = typeof(Material), value = row.material, allowSceneObjects = false };
                material.SetEnabled(false);
                material.AddToClassList("setup_material");
                header.Add(material);
                var label = EditorUiControls.Label(row.errors.Count > 0 ? "要確認" : row.bindings.Count > 0 ? row.bindings.Count + "枚" : "一致なし", "d9_badge");
                header.Add(label);
                card.Add(header);
                card.Add(EditorUiControls.Label(row.path));
                var details = EditorUiControls.Foldout("割り当ての詳細", row.errors.Count > 0);
                details.Add(EditorUiControls.Label("シェーダー: " + row.shader_name));
                foreach (var map in LilToonMaterialSetup.maps)
                {
                    var binding = row.bindings.FirstOrDefault(b => b.map == map);
                    string description = binding == null ? "該当なし・設定を維持" :
                        binding.property == "" ? "sRGBのみオフ" :
                        map.suffix == "Roughness" ? "Smoothnessへ反転 / sRGBオフ" :
                        map.normal ? "Normal map / " + (normal_directx ? "DirectX" : "OpenGL") :
                        "sRGB" + (map.srgb ? "オン" : "オフ");
                    var text = EditorUiControls.Label(map.suffix + " → " + description);
                    text.tooltip = binding?.path ?? "";
                    details.Add(text);
                    if (binding != null)
                    {
                        var texture = new ObjectField { objectType = typeof(Texture2D), value = AssetDatabase.LoadAssetAtPath<Texture2D>(binding.path), allowSceneObjects = false };
                        texture.SetEnabled(false);
                        details.Add(texture);
                    }
                }
                card.Add(details);
                foreach (var message in row.errors) card.Add(new HelpBox(message, HelpBoxMessageType.Error));
                foreach (var message in row.warnings) card.Add(new HelpBox(message, HelpBoxMessageType.Warning));
                preview.Add(card);
            }
            show_status(plan.rows.Count == 0 ? ".matが見つかりません。FBXからExtractしたマテリアルのフォルダを指定してください。" :
                "チェックしたマテリアルを更新します。重複・設定先の競合がある行は適用しません。", HelpBoxMessageType.Info);
            update_buttons();
        }

        private void update_buttons()
        {
            int ready = plan?.rows.Count(r => r.selected && r.can_apply) ?? 0;
            count.text = ready + " / " + (plan?.rows.Count ?? 0) + " マテリアルを適用";
            apply_button.SetEnabled(!busy && ready > 0 && plan.errors.Count == 0);
            restore_button.SetEnabled(!busy && !string.IsNullOrEmpty(LilToonSetupBackup.latest));
        }

        private void apply_preview()
        {
            busy = true;
            update_buttons();
            try
            {
                int applied = plan.rows.Count(r => r.selected && r.can_apply);
                LilToonMaterialSetup.apply(plan);
                build_preview();
                show_status(applied + "件のマテリアルを設定しました。マテリアルとインポート設定は「前回の設定を復元」で戻せます。", HelpBoxMessageType.Info);
            }
            catch (Exception ex) { show_status(ex.Message, HelpBoxMessageType.Error); }
            finally { busy = false; update_buttons(); }
        }

        private void restore_previous()
        {
            if (!EditorUtility.DisplayDialog("前回の設定を復元", "前回の実行で変更したマテリアルとインポート設定を実行前へ戻します。実行後に加えた編集も戻ります。", "復元する", "キャンセル")) return;
            busy = true;
            try
            {
                LilToonSetupBackup.restore(LilToonSetupBackup.latest);
                invalidate();
                show_status("前回の設定を復元しました。", HelpBoxMessageType.Info);
            }
            catch (Exception ex) { show_status(ex.Message, HelpBoxMessageType.Error); }
            finally { busy = false; update_buttons(); }
        }

        private void show_status(string message, HelpBoxMessageType type)
        {
            status.text = message;
            status.messageType = type;
        }
    }
}
