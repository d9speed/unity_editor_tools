using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils
{
    public static class EditorUiTheme
    {
        private const string style_directory = "Packages/io.github.d9speed.editor_core/Editor/ui/";
        private static readonly string[] style_files =
        {
            "design_tokens.uss", "theme_light.uss", "theme_dark.uss", "controls.uss"
        };

        public static void Apply(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.AddToClassList("d9_ui_root");
            foreach (var file in style_files)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(style_directory + file);
                if (sheet == null)
                {
                    Debug.LogWarning("D9speed UIスタイルを読み込めません: " + file);
                    continue;
                }
                if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
            RefreshTheme(root);
            D9speedEditorFontUtility.Apply(root);
        }

        public static void RefreshTheme(VisualElement root)
        {
            if (root == null) return;
            root.EnableInClassList("d9_theme_dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("d9_theme_light", !EditorGUIUtility.isProSkin);
        }
    }
}
