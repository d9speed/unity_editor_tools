# Editor Core 0.1.13 検証記録

2026-09-28、Unity標準のHierarchy検索にブレンドシェイプ名用の`bs:`フィルターを追加しました。従来の単独検索`bs:smile`も維持しています。

Unity 2022.3.22f1の検証プロジェクト`guide_board_core_validation`でパッケージをコンパイルし、`tools/validation_project/Editor/blend_shape_search_checks.cs`を実行しました。次の9種類の検索が成功しています。

- `bs:smile`と`bs:SMILE`で`Smile_Left`を持つGameObjectだけを取得。
- `bs:"happy face"`で`Happy Face`を取得。
- `bs:missing`で結果0件。
- `h:t:SkinnedMeshRenderer bs:smile`と`h:t:SkinnedMeshRenderer bs:SMILE`で`Smile_Left`を持つGameObjectだけを取得。
- `h:t:SkinnedMeshRenderer bs:"happy face"`で`Happy Face`を取得。
- `h:t:SkinnedMeshRenderer bs:missing`で結果0件。メッシュ未設定のSkinnedMeshRendererも検索中に存在させました。
- 入力例そのままの`h:t:Skinnedmeshrenderer bs:smile`でも`Smile_Left`を持つGameObjectだけを取得。

`tools/build_packages.ps1`でEditor Core 0.1.13のZIPを作成し、ルートの`package.json`が0.1.13であること、変更したコードと`.meta`が同梱されていることを確認しました。

- ZIP: `artifacts/editor_core_0_1_13/io.github.d9speed.editor_core-0.1.13.zip`
- サイズ: 73,315 bytes
- SHA-256: `fce1249479ebce497a7b2a2917960a1febfbac58688c217892b7413a65f2a041`

公開後、[VPM一覧](https://d9speed.github.io/Unity_Tools/index.json)に0.1.13と上記SHA-256が表示されること、[案内ページ](https://d9speed.github.io/Unity_Tools/)に新しい検索式が表示されることを確認しました。認証なしでGitHub ReleaseのZIPをダウンロードし、サイズとSHA-256が手元のZIPと一致しました。

Unity Searchウィンドウでの手操作、VCC / ALCOM画面からの導入、複数の大規模シーンでの応答時間は未検証です。
