using UnityEditor;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    internal static class guide_board_menu
    {
        [MenuItem("D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)")]
        private static void open_window()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<EditorWindow>())
            {
                if (type.FullName != "D9speed_BaseEditorUtils.GuideBoard.guide_board_window") continue;
                var window = EditorWindow.GetWindow(type);
                window.titleContent = new GUIContent("説明板プレハブ");
                window.minSize = new Vector2(720f, 500f);
                return;
            }

            EditorUtility.DisplayDialog("説明板プレハブ", "説明板の編集画面を読み込めませんでした。Editor Coreを再導入してください。", "閉じる");
        }
    }
}
