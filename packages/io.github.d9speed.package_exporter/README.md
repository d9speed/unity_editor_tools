# D9speed Package Exporter

選択したフォルダをまとめて`.unitypackage`へ書き出すバッチエクスポーターと、書き出したコンポーネント情報JSONの比較ツールを同梱しています。

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMに登録し、`D9speed Package Exporter`を導入してください。

本体と比較ツールは同時に導入・更新されます。Unityの`D9speed > ExportBatch`に両方のメニューが並びます。

## ExportBatch

除外設定、ファイル名の組み立て、設定のJSON保存・読み込み、コンポーネント情報のJSON出力に対応します。

1. Unityの`D9speed > ExportBatch > ExportBatch`を開きます。
2. 対象フォルダ、出力先、パッケージ名と日付書式を指定します。
3. 書き出すサブフォルダと除外条件を確認し、実行します。

設定は各プロジェクトの`ProjectSettings/PackageExporterSettings.asset`に保存します。JSONプロファイルも任意の保存先で管理できます。配布パッケージの中に個人の出力先やプロファイルは含めていません。

Tablacus Explorerで出力先を開く機能は任意です。有効にする場合は実行ファイルを自分で指定してください。実行ファイルは同梱しません。公開版はDiscord通知を行いません。

## Package Component Report Compare

1. `D9speed > ExportBatch > Package Component Report Compare`を開きます。
2. `基準 JSON`に基準となる`*_components.json`を指定します。
3. `比較対象を追加…`で比較対象のレポートを追加します。複数のレポートを並べて比較できます。
4. 不足・追加・個数差・配置差を確認します。`差分のみ`や`絞り込み`で対象を限定できます。
5. 列見出しで並べ替え、列の境界で幅を調整できます。行を選ぶと配置場所の詳細を表示します。

本体・比較画面ともにUI Toolkitを使用し、Editor CoreのFluent 2共通スタイルとUnityのライト／ダークテーマに対応します。

このツールはレポートを読み取って比較します。Prefabやコンポーネントを変更しません。コンポーネント内部の設定値の差分比較は対象外です。

## 依存関係・動作環境

- Unity 2022.3.22f1、Editor専用。
- D9speed Editor Core: 0.1.5以降、0.2.0未満。
- Unity公式`com.unity.nuget.newtonsoft-json`: 3.2.1。Unity Package Managerが取得します。
- VRChat SDKは必須ではありません。SDK固有コンポーネントの情報出力は、SDKのバージョンや構成により追加確認が必要です。

旧Assets版と同時に導入すると型やGUIDが重複します。まず新規プロジェクトに導入してください。

MIT License。連絡先: d09pseed@gmail.com
