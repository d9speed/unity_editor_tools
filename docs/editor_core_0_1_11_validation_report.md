# Editor Core 0.1.11 検証記録

2026-09-28、説明画像のフォント選択を追加しました。Unity内にはWindowsへインストール済みのフォント一覧と「自動（メイリオ優先）」を表示し、選択した名前を描画DLLへ渡します。

Unity 2022.3.22f1のTMP・uGUI未導入プロジェクト `V:/Unity/d9speed_vpm/guide_board_dll_validation` で、`D9speed.PackageValidation.guide_board_dll_checks.run` の28項目が成功しました。結果は同プロジェクトの `Logs/guide_board_dll_result.txt` にあります。

- Windowsのフォント一覧を取得し、指定フォントでPNGを生成しました。存在しないフォントは拒否されます。
- Unityの画面にフォント一覧を表示し、選択値と「自動」の切り替えが設定に反映されることを確認しました。
- 日本語PNGの生成、長文のはみ出し検出、EditorOnlyプレハブの保存、シーン・既存アセットの保護を再確認しました。
- 生成プレハブはフォントや描画DLLへの参照を持ちません。パッケージにはTextMeshProやuGUIの参照がありません。

DLL単体でも同じ英数字をArialとTimes New Romanで描画し、PNGの内容が異なることを確認しました。

`tools/build_guide_board_renderer.ps1` でDLLを再ビルドし、`tools/build_packages.ps1` でEditor Core 0.1.11のZIPを作成しました。ZIP内の67ファイルはパッケージソースと一致し、フォントファイルや生成画像は含まれません。

- ZIP: `artifacts/editor_core_0_1_11/io.github.d9speed.editor_core-0.1.11.zip`
- サイズ: 71,027 bytes
- SHA-256: `c84d0657da65d7a40931a303664c724be4c056f6f21a291b1ce09ac1c6b57547`

公開VPM一覧から0.1.11を取得し、認証なしでダウンロードしたGitHub ReleaseのZIPが上記SHA-256と一致することを確認しました。

DLLはWindows用です。Unity画面での手操作、macOS、URP/HDRPの板ポリゴン表示、他言語のフォント表示は未検証です。
