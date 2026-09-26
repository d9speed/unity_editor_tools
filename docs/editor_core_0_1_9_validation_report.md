# Editor Core 0.1.9 検証記録

2026-09-26、Unity 2022.3.22f1 / Built-in Render Pipeline / Linearで、説明画像つき板ポリプレハブ作成を検証しました。

## 実装

- メニューは `D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)`。
- `guide_board` の試作をCoreへ統合し、PNG・Unlitマテリアル・EditorOnlyのQuadプレハブを保存する処理を追加しました。
- TMP連携を `D9speed.EditorUtils.GuideBoard` に分離し、Coreの既存アセンブリはTMPを参照しません。
- 元のAssets版と型が衝突しない名前空間を使用します。元フォルダー・元パッケージは変更していません。

## 検証結果

専用の `guide_board_core_validation` プロジェクトで `D9speed.PackageValidation.guide_board_checks.run` の33項目が成功しました。検証コードは `tools/validation_project/Editor/guide_board_checks.cs`、結果は検証プロジェクトの `Logs/guide_board/validation_result.txt` にあります。

- プレビューのシーン分離、RenderTextureの復元・再利用・サイズ変更、必要時のみの描画。
- PNGの1024×512解像度、文字ピクセル、背景色の一致、日本語の描画。
- はみ出し・フォント未収録文字の検出。
- EditorOnlyのPrefabアセット、Collider・専用スクリプトなし、縦横比2:1のQuad。
- 保存先のPNG・Unlitマテリアルへの参照と、TMP・フォント・ツールへの依存がないこと。
- PNGのsRGB・無圧縮・Mip Mapなし・Clamp設定。
- 同名Prefabへの再保存で別名を作成し、既存画像とGUIDを保持すること。
- Assets外のパスと空データを拒否し、アセットを作成しないこと。
- プレビュー・保存用シーンの解放、作業シーンの選択・dirty状態を維持すること。
- Packages内からのUXML読み込み、共通テーマ適用、保存ボタンの有効／無効、指定メニューの完全一致。

別の `guide_board_no_tmp_validation` プロジェクトで、次の2構成も成功しました。

1. TMP・uGUIパッケージなし: Coreのコンパイル、TMP連携アセンブリの除外、メニュー・既存ヘルパーの利用可能性を確認。
2. TMP 3.0.6導入後・TMP Essential Resourcesなし: ウィンドウ作成時に例外が出ず、リソースの導入案内と保存ボタンの無効化を確認。

検証コードは `tools/validation_project/Editor/guide_board_no_tmp_checks.cs`。それぞれ `run`、`run_without_resources` を実行します。結果は同プロジェクトの `Logs/guide_board_no_tmp_result.txt` と `Logs/guide_board_no_resources_result.txt` に保存しています。2構成目の実行後はTMP導入済みです。

日本語の出力PNGを目視確認し、文字化け・切れ・縦横の反転がないことを確認しました。検証用フォント・生成Prefab・PNGは配布ZIPに含めていません。

## 配布物

`tools/build_packages.ps1` による全パッケージのマニフェスト・メタファイル・GUID・Editor専用アセンブリの検査が成功しました。

- ZIP: `artifacts/editor_core_0_1_9/io.github.d9speed.editor_core-0.1.9.zip`
- サイズ: 66,563 bytes
- SHA-256: `44e26fea857489c09badfba8fdfcce0e408706a4815f13a2636511ccb03d3923`

ZIP内の全65ファイルとパッケージソースの一致、フォント・検証コード・検証出力が含まれないことを確認しました。VPM一覧は0.1.9のみを追加し、既存32バージョンの内容を保持しています。配布先はGitHub Releaseの `editor_core_v0.1.9` とVPMリポジトリです。

## 確認範囲

テストは専用の検証プロジェクトで実行します。TMP Essential Resources・テスト用アセットを作成するため、普段の作業プロジェクトでは実行しないでください。

保存ダイアログの手操作、GUIの目視レイアウト、実際のビルドによるEditorOnly除外、macOS、Unity 6、URP/HDRPは今回の確認対象外です。EditorOnlyはPrefabルートへ設定したUnity標準タグを使用します。
