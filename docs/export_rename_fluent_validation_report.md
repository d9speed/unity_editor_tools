# Exporter / Rename Tool のFluent UI検証

実施日: 2026-09-21。Unity 2022.3.22f1 / Windows。

## 対象

- Editor Core 0.1.5: 共通のフィールド・折りたたみ・一覧表・状態色。
- Package Exporter 0.1.2: エクスポーター本体とコンポーネント比較画面。
- Rename Tool 0.1.1: Fluent UI、日本語表示、変更前後の一覧と行ごとの選択。

メニュー階層と既存の保存形式は維持。本体の書き出し、JSONの解析・差分判定、置換・Animator参照更新の処理は継承しています。

## 結果

専用の検証プロジェクトで23項目に成功。共通スタイル変更に対するTransform Mirrorの回帰確認6項目も成功しました。

- 新規のテキストアセットから実際の `.unitypackage` とコンポーネントレポートJSONを書き出し。
- 日本語を含むパッケージ名・追加文字列のJSON保存と読み込み。
- 基準と複数の比較対象について、一致・不足・追加・個数差・配置差を判定。入力JSONを変更しないことを確認。
- UITK表の行選択、数値列の並べ替え、列幅の再読み込み後の維持、配置場所による絞り込み。
- 通常置換・大小文字の扱い・正規表現のグループ参照・無効な正規表現の処理。
- Hierarchyのライブプレビュー、行の適用チェック、全選択・解除、適用件数とボタン状態、フォルダ欄の編集可否。
- 合成GameObjectのリネームとUndo。プレビューやチェック操作で元オブジェクトが変わらないことを確認。
- 3画面それぞれのライト／ダークテーマと小さいウィンドウ。Unity Editorの実際のUITKパネルを描画し、一覧・固定フッター・スクロールを確認。
- Transform Mirrorのドロップ、基準点・回転設定、キーボード実行、Undo、テーマ切替、小さいウィンドウ、一覧クリア。

最終実行ログにC#コンパイルエラー・USS解析警告はありません。検証には合成データを使用し、ユーザーのPrefabは変更していません。

## 再実行

専用Unity検証プロジェクトに対象パッケージと `tools/validation_project/Editor` の検証コードを配置して実行します。

- `D9speed.PackageValidation.ExportRenameFluentChecks.Run`
- `D9speed.PackageValidation.TransformMirrorUiChecks.Run`

画像検証にはグラフィックスデバイスが必要です。バッチモードでは `-force-d3d11` を使い、`-nographics` は付けません。結果と画面画像は検証プロジェクトの `Logs/export_rename_fluent` に出力します。

実アバターのSDK固有データを含む出力や、全Animator構成の網羅検証は今回のUI変更確認には含めていません。
