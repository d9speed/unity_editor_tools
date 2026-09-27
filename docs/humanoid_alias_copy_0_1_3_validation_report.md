# Humanoid Alias Copy 0.1.3 検証記録

2026-09-27、Unity 2022.3.22f1で検証しました。

SkinnedMeshRendererのマテリアルコピーを、上部のコピー元・コピー先の直下へ移動しました。チェック、対象メッシュの折りたたみ一覧、マテリアル専用ボタンをまとめています。「その他・共通」はUnity Collider・Contact・Particle専用です。

## 検証

`tools/humanoid_alias_validation/Editor/humanoid_alias_ui_checks.cs` を検証専用プロジェクトで実行しました。

- SDKなし / Editor Core 0.1.6: 13/13成功。
- SDK 3.10.5 / MA 1.18.7 / NDMF 1.14.8 / Editor Core 0.1.6: 15/15成功。
- メッシュごとのマテリアル選択、無効時・全件除外時の無操作、Undoを確認。
- マテリアルコピーがコンポーネントを変更しないこと、各タブ・その他のコピーがマテリアルを変更しないことを確認。
- ライト／ダーク、1200×850、860×620、マテリアル一覧を開いた1200×1000の実UI Toolkit描画を確認。
- マテリアル操作が入力欄より下、3タブより上にあること、ボタンが画面外に切れないことを確認。

[検証結果JSON](humanoid_alias_copy_0_1_3_validation_results.json) / [画面例（合成テストデータ）](humanoid_alias_copy_0_1_3_dark.png)

`importtest_2`のパッケージ・シーンは変更していません。ALCOMから更新できます。

## 配布物

- ZIP: `io.github.d9speed.humanoid_alias_copy-0.1.3.zip`
- サイズ: 31,586 bytes
- SHA-256: `787fe195c80815f8c0a563f281b946a4fd51e4b3a14a45365ea0717d23764e63`
- タグ: `humanoid_alias_copy_v0.1.3`

配布スクリプトによるマニフェスト・GUID・Editorアセンブリ検査を実施。VPM一覧の既存35バージョンを保持して0.1.3を追加しています。
