# Editor Core 0.1.10 検証記録

2026-09-28、説明画像の文字描画を同梱DLLへ分離し、TextMeshProを使わずに日本語を描画できることを確認しました。

## 構成

- Unityの画面は文章入力、プレビュー、PNG／Prefab保存だけです。
- Windows用 `Renderer~/guide_board_renderer.dll` がシステムフォントを使用し、1024×512 pxのPNGを返します。DLLのソースは同じフォルダーの `guide_board_renderer.cs` です。
- メイリオの英語名・日本語名の両方を認識し、利用できれば選択します。文字サイズは12〜48 pxの範囲で文章量に合わせて自動調整します。
- Editor Coreは返されたPNGをプレビューし、保存時にPNG・マテリアル・EditorOnlyのQuadプレハブを作成します。
- パッケージのC#アセンブリにTextMeshProやuGUIの参照はありません。DLLはWindowsの.NET Frameworkの `System.Drawing` を使用し、フォントファイルを同梱しません。

## 結果

Windows上のDLL単体確認で、システムフォントの列挙、日本語PNGの生成、1024×512 pxの出力、長文のはみ出し検出に成功しました。出力画像の文字と余白を目視確認しています。

Unity 2022.3.22f1のTMP・uGUI未導入の専用プロジェクト `guide_board_dll_validation` で、`D9speed.PackageValidation.guide_board_dll_checks.run` の23項目が成功しました。検証コードは `tools/validation_project/Editor/guide_board_dll_checks.cs`、結果は同プロジェクトの `Logs/guide_board_dll_result.txt` です。日本語の出力PNGは同プロジェクトの `Logs/guide_board_dll_japanese.png` にあります。

- Unity内でDLLを読み込み、日本語PNGとプレビューテクスチャを生成。
- 作業シーンを変更せず、長文のはみ出しと再描画を検出。
- EditorOnlyのPrefab、PNGとマテリアルへの参照、Collider・フォント・ツールへの依存がないことを確認。
- 同名で保存しても元のアセット・GUIDを維持し、Assets外のパスを拒否。
- Unityの画面に文章入力・プレビュー・保存ボタンのみを表示し、指定のメニュー階層を維持。

`tools/build_guide_board_renderer.ps1` でDLLを再ビルドし、`tools/build_packages.ps1` でEditor Core 0.1.10のZIPを作成しました。ZIP内のDLL・C#ソースを含む全ファイルがパッケージソースと一致し、検証用フォント・生成画像・テストコードは含まれていません。

- ZIP: `artifacts/editor_core_0_1_10/io.github.d9speed.editor_core-0.1.10.zip`
- サイズ: 70,233 bytes
- SHA-256: `e5b455145540ed4d76930bd0a8510c800a8307627fedaf6f08d805673b3e64f2`

## 範囲

DLLはWindows用です。Unity画面の手操作、macOS、URP/HDRPの板ポリゴン表示、他言語のシステムフォントは未検証です。Editor Core 0.1.10のZIPはGitHub Releaseに公開し、VPM一覧へ追加しました。
