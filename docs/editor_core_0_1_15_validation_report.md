# Editor Core 0.1.15 検証記録

2026-11-21、Unity 2022.3.22f1 / lilToon 2.4.1で、`D9speed/Tools/liltoonマテリアルセットアップ` を検証しました。

## 結果

- 専用プロジェクト `liltoon_material_setup_validation` で74項目成功。コンパイルエラー・検証例外なし。
- lilToon未導入の `guide_board_sprite_validation` でもコンパイル成功。既存のEditor CoreへlilToonの必須アセンブリ依存を追加していません。
- `BaseColor`、`NormalMap`、`MatCapMask`、`Emission`、`AOMap`、`Roughness`、`MatCap2`、`NormalMap2`、`NormalMap2Mask`、`RimLightMask` の照合・割り当てを確認。
- AOMap / Roughness / MatCapMask / MatCap2と各マスクのsRGBオフ、色画像のsRGBオン、Normal map形式とDirectXの緑チャンネル反転を確認。
- AOの既定はlilToonの影設定のAO Map (`_ShadowBorderMask`)。メインカラー2ndを消費しません。任意の乗算モード、競合時の停止、割り当てずsRGBのみ変更するモードも検証。
- 元Roughnessのピクセルを維持し、R=32からSmoothness=223を生成。再実行時の二重反転がなく、生成PNGのGUIDを維持。
- 既存 `.mat` のGUIDとPrefabのマテリアル参照、雛形のRender Queueと強度を維持。
- 確認だけではアセットを変更せず、重複候補・未一致・雛形自身を適用対象から除外。確認後の未保存マテリアル編集も検出。
- 元マテリアルと全インポート設定のバイト一致で復元を確認。元Roughnessを排他ロックする故障試験でもマテリアル／インポート設定を自動復元し、生成画像を残さないことを確認。
- UI ToolkitのUXML読込、初期状態の実行無効化、確認後の有効化、マテリアル別カード表示を検証。

検証コード: [liltoon_material_setup_checks.cs](../tools/liltoon_material_setup_validation/Editor/liltoon_material_setup_checks.cs)

結果: [liltoon_setup_results.txt](../../liltoon_material_setup_validation/Logs/liltoon_setup_results.txt)

ログ: [checks_final.log](../../liltoon_material_setup_validation/Logs/checks_final.log)、[no_liltoon_compile.log](../../liltoon_material_setup_validation/Logs/no_liltoon_compile.log)

## 画面確認

既存Editor Coreと同じUI Toolkit・共通テーマを使用。専用Editorを開き、実メニューから起動した720×840のウィンドウをComputer Useで確認しました。画面は会話内のキャプチャで確認し、配布パッケージへ検証画像は含めていません。

1. 初回表示を確認後、フォルダ欄とオプションを横並びに変更。更新後のキャプチャで説明と入力欄の読みやすさを確認。
2. マテリアル行が縦に折り返される点を修正し、チェック・マテリアル名・枚数を横並びに調整。更新後のキャプチャで一覧、MatCap不足の警告、固定フッターの実行ボタンを確認。

画面確認用Editorは終了しています。実際の衣装マテリアルには適用していません。最小ウィンドウ幅やライトテーマ、実際のPainter画像による外観比較は今回の目視対象外です。

## パッケージ

- バージョン: `0.1.15`
- ZIP: [io.github.d9speed.editor_core-0.1.15.zip](../artifacts/editor_core_0_1_15/io.github.d9speed.editor_core-0.1.15.zip)
- サイズ: 86,859 bytes
- 全78ファイルがソースとSHA-256で一致。GUID重複・不足meta・Editor専用アセンブリなど既存ビルドスクリプトの検査も成功。
- ZIP SHA-256: `6f26aaaa0554c0ec642cc70ade52edbf1e9caf1b58bf496f32c14dc1be5a5b41`
- GitHub Release・VPM公開インデックスへの反映は含めていません。

## 参照

- Unityスキル: `using-unity`、`create-ui.md`、`uitk-skill.md`、`ui-visual-design.md`、`unity-cli.md`、`build-run-test.md`、`projects-templates.md`。
- [Unity 2022.3: Create a custom Editor window](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-HowTo-CreateEditorWindow.html)
- [Unity 2022.3: TextureImporter.flipGreenChannel](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/TextureImporter-flipGreenChannel.html)
- [lilToon: はじめに](https://lilxyzw.github.io/lilToon/ja_JP/first)
- [lilToon: 影設定・AO Map](https://lilxyzw.github.io/lilToon/ja_JP/color/shadow.html)
- ローカルlilToon 2.4.1の `lts.shader`、`lilPropertyGroupDrawerColorSetting.cs`、`lil_common_frag.hlsl` でプロパティと動作を照合。

Unity CLIは既存の1.0.0-beta.15で実行しました。更新確認では1.0.1とCodexプラグイン設定の案内がありましたが、Unity本体・CLI・認証の設定変更は行っていません。CLIのサインインなしでもインストール済みEditorで検証できました。
