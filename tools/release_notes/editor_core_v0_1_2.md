Unity UtilityのフルパスコピーなどをEditor Coreへ統合しました。従来の右クリックメニューを維持し、VPM／UPMパッケージ内のファイルと複数選択にも対応します。

- アニメーションパス・コンポーネント名コピー、階層検索、複数コンポーネントのコピー／貼り付け。
- Transformリセット／ミラー複製、メインカメラの向き合わせ、Prefab変更マーク。
- 選択したアセット群／フォルダの参照を保つ複製。既存コピーを上書きしません。
- `D9speed > Settings`から共通フォント、Prefab変更マーク、サクラエディタ連携を設定。

個人用の固定パス・Discord通知・HTTP制御は収録しません。旧`D9speed_UnityUtility.cs`がある場合は、バックアップ後に重複する機能を整理してください。

Unity 2022.3.22f1で8項目の自動確認が成功しました。パス解決・Undo・コピー後の内部参照・設定保存を確認しています。外部エディタ実行、Unity 6、macOS/Linuxは未検証です。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core) / [検証記録](https://github.com/d9speed/unity_editor_tools/blob/main/docs/core_utilities_validation_report.md)
