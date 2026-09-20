# D9speed Package Exporter

選択したフォルダをまとめて`.unitypackage`へ書き出すバッチエクスポーターです。除外設定、ファイル名の組み立て、設定のJSON保存・読み込み、コンポーネント情報のJSON出力に対応します。

## 導入と使用

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMに登録し、`D9speed Package Exporter`を導入してください。

1. Unityの`D9speed > ExportBatch`を開きます。
2. 対象フォルダ、出力先、パッケージ名と日付書式を指定します。
3. 書き出すサブフォルダと除外条件を確認し、実行します。

設定は各プロジェクトの`ProjectSettings/PackageExporterSettings.asset`に保存します。JSONプロファイルも任意の保存先で管理できます。配布パッケージの中に個人の出力先やプロファイルは含めていません。

Tablacus Explorerで出力先を開く機能は任意です。有効にする場合は実行ファイルを自分で指定してください。実行ファイルは同梱しません。公開版はDiscord通知を行いません。

## 依存関係・動作環境

- Unity 2022.3.22f1、Editor専用。
- D9speed Editor Core: 0.1.1以降、0.2.0未満。
- Unity公式`com.unity.nuget.newtonsoft-json`: 3.2.1。Unity Package Managerが取得します。
- VRChat SDKは必須ではありません。SDK固有コンポーネントの情報出力は、SDKのバージョンや構成により追加確認が必要です。

旧Assets版と同時に導入すると型やGUIDが重複します。まず新規プロジェクトに導入してください。

MIT License。連絡先: d09pseed@gmail.com
