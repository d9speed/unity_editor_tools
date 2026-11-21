using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils.GuideBoard
{
    public sealed class guide_board_window : EditorWindow
    {
        private const string automatic_font_label = "自動（メイリオ優先）";
        [SerializeField] private guide_board_settings settings = new guide_board_settings();
        private guide_board_preview preview;
        private Image preview_image;
        private Label status_label;
        private Label resolution_label;
        private Button export_button;
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

            var font_field = rootVisualElement.Q<DropdownField>("font_field");
            try
            {
                var installed_fonts = guide_board_preview.get_font_families();
                var choices = new List<string>(installed_fonts.Length + 1) { automatic_font_label };
                choices.AddRange(installed_fonts);
                font_field.choices = choices;
                var selected_font = Array.Find(installed_fonts, name =>
                    string.Equals(name, settings.font_name, StringComparison.OrdinalIgnoreCase));
                settings.font_name = selected_font ?? string.Empty;
                font_field.SetValueWithoutNotify(selected_font ?? automatic_font_label);
                font_field.RegisterValueChangedCallback(e => select_font(e.newValue));
            }
            catch (Exception exception)
            {
                font_field.SetEnabled(false);
                set_status(exception.Message, true);
                Debug.LogException(exception);
            }

            bind<TextField, string>("text_field", settings.text, value => settings.text = value);
            rootVisualElement.Q<Button>("refresh_button").clicked += render_preview;
            export_button.clicked += export_sprite;
            schedule_render();
        }

        private void bind<TField, TValue>(string field_name, TValue value, Action<TValue> changed) where TField : BaseField<TValue>
        {
            var field = rootVisualElement.Q<TField>(field_name);
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(e => { changed(e.newValue); schedule_render(); });
        }

        private void select_font(string value)
        {
            settings.font_name = value == automatic_font_label ? string.Empty : value;
            schedule_render();
        }

        private void schedule_render()
        {
            export_button?.SetEnabled(false);
            pending_render?.Pause();
            pending_render = rootVisualElement.schedule.Execute(render_preview).StartingIn(100);
        }

        private void set_status(string message, bool warning = false)
        {
            status_label.text = message;
            status_label.EnableInClassList("warning", warning);
        }

        private void render_preview()
        {
            pending_render?.Pause();
            if (preview_image == null) return;
            export_button.SetEnabled(false);
            try
            {
                resolution_label.text = "1024 × 512 px";
                preview ??= new guide_board_preview();
                preview.render(settings);
                preview_image.image = preview.texture;
                preview_image.MarkDirtyRepaint();
                if (preview.text_overflows)
                    set_status("文章が枠からはみ出しています。文章を短くしてください。", true);
                else
                    set_status("フォント: " + (string.IsNullOrEmpty(settings.font_name) ? "自動" : settings.font_name) +
                        "。Sprite として保存できます。");
                export_button.SetEnabled(true);
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

        private void export_sprite()
        {
            render_preview();
            if (preview == null || !export_button.enabledSelf) return;
            if (preview.text_overflows && !EditorUtility.DisplayDialog("文章が収まりません", "末尾が切れた状態で保存しますか？", "保存", "戻る")) return;
            var target_path = EditorUtility.SaveFilePanelInProject("説明画像を Sprite として保存", "guide_text", "png", "Assets 内に Sprite 設定の PNG を保存します。同名の場合は連番で作成します。");
            if (string.IsNullOrEmpty(target_path)) return;
            try
            {
                var sprite = guide_board_exporter.save_sprite(target_path, preview.encode_png());
                Selection.activeObject = sprite;
                EditorGUIUtility.PingObject(sprite);
                set_status("保存しました: " + AssetDatabase.GetAssetPath(sprite));
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
