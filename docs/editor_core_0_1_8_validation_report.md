# Editor Core 0.1.8 検証記録

2026-09-26、専用の `scene_tools_validation` プロジェクトをUnity 2022.3.22f1のバッチモードで起動し、Editor Coreのコンパイルと `InspectorLockChecks.Run` の5項目が成功しました。

- `Custom/ShortCutEX/InspectorLock` がShortcut Managerに登録され、初期割り当てがL + Action（WindowsではCtrl）であること。
- Inspectorなしで実行してもウィンドウを作らず、選択を変更しないこと。
- ロック後に別のGameObjectを選択しても表示対象を保持し、解除後は新しい選択を表示すること。
- 複数Inspectorのうち1つだけが切り替わり、選択と現在のフォーカスを変更しないこと。
- 未選択で連続実行しても例外が発生せず、Inspector数と選択が変化しないこと。

検証コード: `tools/validation_project/Editor/inspector_lock_checks.cs`。実行先の `Logs/inspector_lock_results.json` に結果を出力します。検証はInspectorを閉じるため、通常の作業プロジェクトでは実行しないでください。

バッチモードでは `EditorWindow.Focus()` によるGUIフォーカスを再現できないため、物理キー入力、フォーカス中とマウス下のInspectorの優先順位、Shortcuts画面での割り当て変更は未検証です。macOS上の動作確認は行っていません。

全パッケージのマニフェスト・メタファイル・GUID・Editor専用アセンブリの検査を通過しました。配布ZIPは51,269 bytes、SHA-256は `06c1e7b94c0a29937ab4237f2d0e2cdac226c881eaf80ebd302a7eba6fae053d` です。VPM一覧には0.1.8のみを追加し、既存の全パッケージ・全バージョンの内容が維持されていることを比較確認しました。
