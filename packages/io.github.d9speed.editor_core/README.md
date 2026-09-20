# D9speed Editor Core

D9speedのEditor拡張が共有する補助処理です。初版はScene Toolsに必要な`EditorObjectHelper`を収録します。

- 対応基準: Unity 2022.3.22f1。
- Editor専用。VRChat SDK、外部DLL、専用Runtimeは不要です。
- アセンブリ名: `D9speed.EditorUtils`。
- 名前空間: `D9speed_BaseEditorUtils`。

`FindSceneObjects<T>()`は有効なシーンオブジェクトを取得します。`true`を渡すと非アクティブも含めます。`GetObjectId()`はEditorセッション内の識別子を返します。

既存のEditorToolにあるカメラ操作、通知、設定画面などは今後の切り分け対象です。この初版には含めません。

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMへリポジトリを追加します。

VCC/ALCOMでScene Toolsを導入すると、本パッケージも依存パッケージとして導入されます。ローカル検証では両方を新規プロジェクトの`Packages`へ配置します。

既存のEditorToolがあるプロジェクトへの移行は未対応です。同じGUID・アセンブリ名・型を持つファイルの重複を避けるため、まず新規プロジェクトで検証してください。元ファイルを削除する`legacyFolders`・`legacyFiles`は設定していません。

## ライセンス・連絡先

MIT License。詳細は同梱の`LICENSE.md`を参照してください。

連絡先: d09pseed@gmail.com
