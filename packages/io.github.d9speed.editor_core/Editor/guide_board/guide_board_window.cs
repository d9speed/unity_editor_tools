using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    public sealed class guide_board_window : EditorWindow
    {
        [SerializeField] private guide_board_settings settings = new guide_board_settings();
        [SerializeField] private string last_save_directory = "Assets";
        private guide_board_preview preview;
        private Image preview_image;
        private Label status_label;
        private Label resolution_label;
        private Button export_button;
        private Button prefab_button;
        private IVisualElementScheduledItem pending_render;

        public void CreateGUI()
        {
            pending_render?.Pause();
            rootVisualElement.Clear();
            EditorUiTheme.Apply(rootVisualElement);
            var script_path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var layout_path = Path.GetDirectoryName(script_path).Replace('\\', '/') + "/guide_board_window.uxml";
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(layout_path);
            if (layout == null)
            {
                rootVisualElement.Add(new HelpBox("guide_board_window.uxml をスクリプトと同じフォルダーに配置してください。", HelpBoxMessageType.Error));
                return;
            }
            layout.CloneTree(rootVisualElement);
            // CloneTree's template container must stretch along with the window.
            rootVisualElement[0].AddToClassList("guide_root");
            preview_image = rootVisualElement.Q<Image>("preview_image");
            preview_image.scaleMode = ScaleMode.ScaleToFit;
            status_label = rootVisualElement.Q<Label>("status_label");
            resolution_label = rootVisualElement.Q<Label>("resolution_label");
            export_button = rootVisualElement.Q<Button>("export_button");
            prefab_button = rootVisualElement.Q<Button>("prefab_button");

            if (settings.font == null && TMP_Settings.instance != null) settings.font = TMP_Settings.defaultFontAsset;
            settings.normalize();
            var font_field = rootVisualElement.Q<ObjectField>("font_field");
            font_field.objectType = typeof(TMP_FontAsset);
            font_field.allowSceneObjects = false;
            font_field.SetValueWithoutNotify(settings.font);
            font_field.RegisterValueChangedCallback(e => { settings.font = e.newValue as TMP_FontAsset; schedule_render(); });
            bind<TextField, string>("text_field", settings.text, value => settings.text = value);
            bind<IntegerField, int>("width_field", settings.width, value => settings.width = value);
            bind<IntegerField, int>("height_field", settings.height, value => settings.height = value);
            rootVisualElement.Q<IntegerField>("width_field").isDelayed = true;
            rootVisualElement.Q<IntegerField>("height_field").isDelayed = true;
            bind<SliderInt, int>("font_size_field", settings.font_size, value => settings.font_size = value);
            bind<SliderInt, int>("padding_field", settings.padding, value => settings.padding = value);
            bind<ColorField, Color>("text_color_field", settings.text_color, value => settings.text_color = value);
            bind<ColorField, Color>("background_color_field", settings.background_color, value => settings.background_color = value);
            var alignment_field = rootVisualElement.Q<DropdownField>("alignment_field");
            alignment_field.choices = new List<string> { "左揃え", "中央揃え", "右揃え" };
            alignment_field.SetValueWithoutNotify(alignment_field.choices[settings.alignment]);
            alignment_field.RegisterValueChangedCallback(e => { settings.alignment = alignment_field.choices.IndexOf(e.newValue); schedule_render(); });
            rootVisualElement.Q<Button>("refresh_button").clicked += render_preview;
            export_button.clicked += export_png;
            prefab_button.clicked += export_prefab;
            schedule_render();
        }

        private void bind<TField, TValue>(string field_name, TValue value, Action<TValue> changed) where TField : BaseField<TValue>
        {
            var field = rootVisualElement.Q<TField>(field_name);
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(e => { changed(e.newValue); schedule_render(); });
        }

        private void schedule_render()
        {
            export_button?.SetEnabled(false);
            prefab_button?.SetEnabled(false);
            pending_render?.Pause();
            pending_render = rootVisualElement.schedule.Execute(render_preview).StartingIn(100);
        }

        private void set_status(string message, bool warning = false)
        {
            status_label.text = message;
            status_label.EnableInClassList("warning", warning);
        }

        private void sync_normalized_fields()
        {
            rootVisualElement.Q<IntegerField>("width_field").SetValueWithoutNotify(settings.width);
            rootVisualElement.Q<IntegerField>("height_field").SetValueWithoutNotify(settings.height);
            var padding_field = rootVisualElement.Q<SliderInt>("padding_field");
            padding_field.highValue = (Mathf.Min(settings.width, settings.height) - 16) / 2;
            padding_field.SetValueWithoutNotify(settings.padding);
        }

        private void render_preview()
        {
            pending_render?.Pause();
            if (preview_image == null) return;
            export_button.SetEnabled(false);
            prefab_button.SetEnabled(false);
            try
            {
                settings.normalize();
                sync_normalized_fields();
                resolution_label.text = $"{settings.width} × {settings.height} px";
                if (settings.font == null)
                {
                    preview_image.image = null;
                    set_status("TMP フォントを選択してください。未設定の場合は Window > TextMeshPro > Import TMP Essential Resources を実行してください。", true);
                    return;
                }
                preview ??= new guide_board_preview();
                preview.render(settings);
                preview_image.image = preview.texture;
                preview_image.MarkDirtyRepaint();
                if (preview.has_missing_characters)
                    set_status("選択したフォントに含まれない文字があります。日本語などに対応した TMP フォントを選んでください。", true);
                else if (preview.text_overflows)
                    set_status("文章が枠からはみ出しています。文字サイズ・余白・画像の高さを調整してください。", true);
                else
                    set_status("プレビューの画像を貼った EditorOnly の板ポリゴンをプレハブに保存できます。");
                export_button.SetEnabled(!preview.has_missing_characters);
                prefab_button.SetEnabled(!preview.has_missing_characters);
            }
            catch (Exception exception)
            {
                preview_image.image = null;
                preview?.Dispose();
                preview = null;
                set_status(exception.Message, true);
                Debug.LogException(exception);
            }
        }

        private void export_png()
        {
            render_preview();
            if (preview == null || !export_button.enabledSelf) return;
            if (preview.text_overflows && !EditorUtility.DisplayDialog("文章が収まりません", "末尾が切れた状態で保存しますか？", "保存", "戻る")) return;
            var target_path = EditorUtility.SaveFilePanel("説明画像を保存", last_save_directory, "guide_text", "png");
            if (string.IsNullOrEmpty(target_path)) return;
            try
            {
                // SaveFilePanel supplies Unity/OS's overwrite confirmation.
                File.WriteAllBytes(target_path, preview.encode_png());
                last_save_directory = Path.GetDirectoryName(target_path);
                var assets_directory = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
                var full_path = Path.GetFullPath(target_path);
                if (full_path.StartsWith(assets_directory, StringComparison.OrdinalIgnoreCase))
                {
                    var asset_path = "Assets/" + full_path.Substring(assets_directory.Length).Replace('\\', '/');
                    AssetDatabase.ImportAsset(asset_path, ImportAssetOptions.ForceSynchronousImport);
                    guide_board_exporter.configure_texture(asset_path);
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(asset_path));
                }
                set_status("保存しました: " + Path.GetFileName(target_path));
            }
            catch (Exception exception)
            {
                set_status("保存できませんでした: " + exception.Message, true);
                Debug.LogException(exception);
            }
        }

        private void export_prefab()
        {
            render_preview();
            if (preview == null || !prefab_button.enabledSelf) return;
            if (preview.text_overflows && !EditorUtility.DisplayDialog("文章が収まりません", "末尾が切れた状態で保存しますか？", "保存", "戻る")) return;
            var target_path = EditorUtility.SaveFilePanelInProject("説明板プレハブを保存", "guide_board", "prefab", "PNG・マテリアル・EditorOnly のプレハブを同じフォルダーへ保存します。同名の場合は連番で作成します。");
            if (string.IsNullOrEmpty(target_path)) return;
            try
            {
                var prefab = guide_board_exporter.save_prefab(target_path, preview.encode_png());
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                set_status("保存しました: " + AssetDatabase.GetAssetPath(prefab));
            }
            catch (Exception exception)
            {
                set_status("保存できませんでした: " + exception.Message, true);
                Debug.LogException(exception);
            }
        }

        private void OnInspectorUpdate()
        {
            EditorUiTheme.RefreshTheme(rootVisualElement);
        }

        private void OnDisable()
        {
            pending_render?.Pause();
            if (preview_image != null) preview_image.image = null;
            preview?.Dispose();
            preview = null;
        }
    }
}
