文章を画像にして、EditorOnlyの板ポリゴンプレハブとして保存する機能を追加しました。

メニュー: `D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)`

- TMPフォント・文章・文字サイズ・余白・揃え方・文字色・背景色・解像度をプレビューしながら調整できます。
- PNG、Unlitマテリアル、画像の縦横比に合わせたQuadプレハブをまとめて保存します。同名の場合は別名で作成します。
- プレハブはEditorOnlyタグ付き・Colliderなしで、TMPやフォントへの参照は持ちません。PNG単体の保存も可能です。
- 文字欠落の検出、はみ出し警告、Core共通のライト／ダーク・フォント設定に対応します。
- TMP連携は任意アセンブリに分離しています。TMP未導入でもCoreの既存機能を使用できます。

作成にはUnity 2022.3 / Built-in Render Pipeline、TextMeshPro 3.0.xとTMP Essential Resourcesが必要です。日本語を含む文章には日本語対応のTMPフォントを指定してください。

Unity 2022.3.22f1で描画・保存・既存アセット保護など33項目、およびTMP未導入／初期リソース未導入の2構成を確認しました。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#説明画像つき板ポリプレハブ) · [検証記録](https://github.com/d9speed/unity_editor_tools/blob/main/docs/editor_core_0_1_9_validation_report.md)

VCC / ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.9へ更新してください。
