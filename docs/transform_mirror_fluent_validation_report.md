# Transform Mirror / Fluent 2 初回導入

2026-09-21。Editor Core 0.1.4へ、Fluent 2のトークン設計を基にした共通スタイルを追加し、Transform Mirrorへ適用しました。Unity 2022.3.22f1で検証しています。

## 実装

- `Editor/ui/design_tokens.uss`: 余白、角丸、文字サイズ、ブランド色の基準値。
- `Editor/ui/theme_light.uss` / `theme_dark.uss`: 用途別の色とライト／ダークの対応。
- `Editor/ui/controls.uss`: `d9_ui_root`配下に限定した見出し・セクション・ボタン・一覧・入力欄・チェックの装飾。
- `Editor/ui/editor_ui_theme.cs`: スタイルの読み込み、Unityのスキンへの追従、既存の共通フォント設定の適用。
- Transform Mirror: 対象数、空の一覧の案内、一覧クリア、対象が空のときの実行無効化、カスタム基準点の編集可否を追加。対象・オプションをシリアライズ対象にしました。

画面本体はスクロールし、実行ボタンは下部に残ります。UIは既存と同じC#構築のUI Toolkitです。Unity標準のListView、Toggle、Vector3Field、Buttonを使い、外部のUIライブラリやフォントを追加していません。

`InstantiateMirroredAll()`以降の処理が前版と同じことをソース比較で確認しました。Prefabの生成、回転、BlendShapeコピー、Undoの処理は変更していません。

## 検証

専用プロジェクトへ配布ZIPを展開し、Unity Editorのパネル上で実際のVisualElementとUSSを評価しました。利用者のシーン・Prefabをテスト対象にはしていません。

`tools/validation_project/Editor/transform_mirror_ui_checks.cs`の6項目が成功しています。

1. 空の対象一覧、初期トグル、無効な実行ボタンと座標欄。
2. GameObjectを受け入れ、非GameObjectを除外するドロップ。ドラッグの中断時に強調表示を解除。画面の再構築で対象・オプションを維持し、スタイルを重複登録しないこと。
3. キーボードのSubmitイベントから実行でき、指定基準点と回転オプションが複製結果へ反映されること。Undoで元のオブジェクトを変えずに複製を戻せること。
4. ライト／ダークのUSS取り込み、色のエイリアスと角丸の解決、レイアウト。
5. 幅340・高さ380の画面で本体がスクロールし、実行ボタンが横に切れないこと。
6. 一覧クリアがシーンオブジェクトを削除せず、空の状態へ戻すこと。

Direct3D 11を使ってEditorパネルをRenderTextureへ描画し、ライト／ダーク・狭い幅・空の一覧の画像も確認しました。最終実行のログにC#エラー、USS警告、例外はありません。検証用コードだけがUnity内部のパネルAPIを使い、配布されるコードには含めていません。

既存の`core_utilities_checks.cs`も8項目が成功しています。UIの再構築は検証しましたが、全環境でのドメインリロード・カスタムフォント・高DPI・macOS/Linuxの表示を確認したものではありません。

## 配布物

- バージョン: `io.github.d9speed.editor_core` 0.1.4
- サイズ: 36,179 bytes
- SHA-256: `21ff025082f8bbc76f8a102ec2fd88ceae7fe11f0267c25204545e5efbf30a10`
- 適用範囲: Transform Mirror。その他のツールへの展開は今後の作業です。

設計の参照先は[Fluent 2 Design tokens](https://fluent2.microsoft.design/design-tokens)、Unity側の仕様は[USS変数](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-USS-CustomProperties.html)と[対応プロパティ](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-USS-Properties-Reference.html)です。
