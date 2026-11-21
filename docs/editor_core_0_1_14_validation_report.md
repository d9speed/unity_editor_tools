# Editor Core 0.1.14 検証記録

2026-11-02、Unity 2022.3.22f1 の最小構成プロジェクト `guide_board_sprite_validation` で、説明画像のSprite保存を検証しました。

- `D9speed.PackageValidation.guide_board_dll_checks.run` の25項目が成功。日本語PNG描画、Sprite設定、1024×512 px → 2×1 Unity単位、同名保存時の連番、既存画像のGUID維持を確認しました。
- 保存先フォルダーにマテリアル・プレハブが作られないこと、ツール画面からプレハブ保存ボタンがなくなったこと、作業中のシーンを変更しないことを確認しました。
- TMP・uGUI未導入でもコンパイルと描画に成功しました。Windowsのインストール済みフォントを使用し、フォントファイルはZIPに含みません。
- パッケージZIPの全69ファイルがソースとSHA-256で一致しました。

検証結果: `../../guide_board_sprite_validation/Logs/guide_board_dll_result.txt`

ZIP: `../artifacts/editor_core_0_1_14/io.github.d9speed.editor_core-0.1.14.zip`

SHA-256: `47f47a3ecf4430a7df4091086c64d8cadbff2f3b820d0a8ac89ffb3a64e3e49c`

VPMの公開インデックスとGitHub Releaseへの反映は含めていません。
