# Editor Core 0.1.12 検証記録

2026-09-28、Unity Searchにブレンドシェイプ名検索用の`bs:`プロバイダを追加しました。

Unity 2022.3.22f1の検証プロジェクト`guide_board_core_validation`でパッケージをコンパイルし、`tools/validation_project/Editor/blend_shape_search_checks.cs`を実行しました。次の4種類の検索が成功しています。

- `bs:smile`で`Smile_Left`を持つGameObjectだけを取得。
- `bs:SMILE`でも同じGameObjectを取得。
- `bs:"happy face"`で空白を含む`Happy Face`を取得。
- `bs:missing`で結果0件。メッシュ未設定のSkinnedMeshRendererも検索中に存在させました。

`tools/build_packages.ps1`でEditor Core 0.1.12のZIPを作成し、ルートの`package.json`が0.1.12であること、新しいコードと`.meta`が同梱されていることを確認しました。

- ZIP: `artifacts/editor_core_0_1_12/io.github.d9speed.editor_core-0.1.12.zip`
- サイズ: 72,994 bytes
- SHA-256: `84dc8a9ddf2c62d658d5e87e55ce438c240ccd7807cf4e3481549c4209414744`

Unity Searchウィンドウでの手操作と、複数の大規模シーンでの応答時間は未検証です。
