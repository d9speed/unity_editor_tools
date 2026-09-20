# D9speed Editor Core

D9speedのEditor拡張が共有する補助処理です。オブジェクト検索、ヒューマノイド対応付け、階層パス、コンポーネントコピー、ファイル名の処理、共通フォント設定を収録します。

- 対応基準: Unity 2022.3.22f1。
- Editor専用。VRChat SDK、外部DLL、専用Runtimeは不要です。
- アセンブリ名: `D9speed.EditorUtils`。
- 名前空間: `D9speed_BaseEditorUtils`。

`FindSceneObjects<T>()`は有効なシーンオブジェクトを取得します。`true`を渡すと非アクティブも含めます。`GetObjectId()`はEditorセッション内の識別子を返します。

フォントは`Edit > Preferences > D9speed Tools`で設定できます。未指定時はUnity標準フォントを使用します。カメラ操作や個人用の通知・認証設定は含めません。0.1.1はScene Tools 0.1.0と併用できます。

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMへリポジトリを追加します。

VCC/ALCOMで各ツールを導入すると、本パッケージも依存パッケージとして導入されます。

既存のEditorToolがあるプロジェクトへの移行は未対応です。同じGUID・アセンブリ名・型を持つファイルの重複を避けるため、まず新規プロジェクトで検証してください。元ファイルを削除する`legacyFolders`・`legacyFiles`は設定していません。

## ライセンス・連絡先

MIT License。詳細は同梱の`LICENSE.md`を参照してください。

連絡先: d09pseed@gmail.com
