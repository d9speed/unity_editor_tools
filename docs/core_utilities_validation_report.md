# Editor Core 0.1.2 統合・検証記録

2026-09-21、Unity 2022.3.22f1の検証用プロジェクトで確認しました。利用者のPrefab・アバターは変更していません。

Unity Utilityの公開可能な機能を`io.github.d9speed.editor_core`へ機能別に分割しました。設定画面は既存の`Preferences/D9speed Tools`へ集約し、既存フォントのEditorPrefsキーとメニューを維持しています。個人用Discord通知と未公開のカメラ・HTTP設定は公開パッケージに含めていません。

## 実行結果

`tools/validation_project/Editor/core_utilities_checks.cs`の8項目が成功しました。

- Editor専用アセンブリ、Settings Providerの登録、設定メニュー。
- Assets・フォルダ・UPMキャッシュ内の実パス、複数選択、未選択時のクリップボード維持。
- 入れ子のAnimatorを基準としたアニメーションパスと引用符を含む名前。
- ローカルTransformリセットとUndo。
- コンポーネント名コピー、複数コンポーネントの順序・値・Undo。
- Transform Mirrorの初期トグルと、親のスケールを含めたミラー複製・Undo。
- フォルダ複製での内部参照置換、外部参照維持、空フォルダ保持、再複製時の上書き防止。
- ローカル設定の保存、空白を含む外部コマンドの引数構築。外部アプリは起動していません。

テストは専用フォルダを作成し、終了時に削除します。変更したEditorPrefs・クリップボード・選択は復元します。

## 再実行

専用Unityプロジェクトの`Packages`へ開発中のCoreを配置し、チェック用ファイルを`Assets/Editor`へ配置します。Unityのバッチ起動で`D9speed.PackageValidation.CoreUtilitiesChecks.Run`を実行します。結果は`Logs/core_utilities_checks.json`です。

## 制限

外部のAutoHotkey・サクラエディタ実行、Unity 6、macOS/Linux、すべてのPrefab構成は未検証です。アセット内の参照置換はYAMLテキスト形式に限ります。
