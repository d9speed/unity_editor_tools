# D9speed Unity Editor Tools

Unity Editor拡張を、機能ごとのVPMパッケージとして管理します。このリポジトリ自体はUnityプロジェクトではありません。

## 初回パッケージ

| パッケージ | 内容 |
|---|---|
| `io.github.d9speed.editor_core` | Scene Toolsに必要な共通ヘルパー |
| `io.github.d9speed.scene_tools` | Display Child Names、Package / Prefab Info |

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMにリポジトリを追加し、プロジェクト管理画面で`D9speed Scene Tools`を導入してください。Editor Coreも依存パッケージとして導入されます。

登録用URL: `https://d9speed.github.io/Unity_Tools/index.json`

Unity 2022.3.22f1で検証しています。旧Assets版と同時に入れるとGUIDやアセンブリが重複するため、新規プロジェクトでの導入を推奨します。旧ファイルの自動削除は行いません。

## 作業フォルダ

- `packages/`: 各パッケージの配布対象。
- `tools/`: ローカルZIP作成・検証用処理。ZIPには入りません。
- `docs/`: 移行方針と検証記録。
- `artifacts/`: ZIPとハッシュ。Git対象外。

検証用Unityプロジェクトはリポジトリに含めません。開発時は、同じ親フォルダの`scene_tools_validation`で検証しています。

2026-09-20、Unity 2022.3.22f1の新規プロジェクトでZIPから導入し、7項目の自動確認が成功しました。詳細は[検証記録](docs/validation_report.md)を参照してください。

同日、公開URLからvrc-get 1.9.2でScene Toolsを指定して導入し、Editor Coreの自動導入と配布ファイルの一致も確認しました。

## ZIPの作成

PowerShellで`./tools/build_packages.ps1`を実行します。各ZIPの直下に`package.json`、`LICENSE.md`、`Editor`が入ります。既存の同名ZIPは上書きしません。出力先を変える場合は`-output_directory`で指定できます。

0.1.0の公開用ZIPは`artifacts/release_0_1_0`に生成しました。今後の更新では、パッケージのバージョンとダウンロードURLを変更し、新しいタグ・ZIPを追加します。公開済みのZIPは置き換えず、旧バージョンも維持してください。

Unity Editor 2022.3.22f1で、ZIPを展開した2パッケージを導入して検証します。`tools/validation_project/Editor/package_smoke_checks.cs`を検証プロジェクトの`Assets/Editor`へコピーし、`D9speed.PackageValidation.PackageSmokeChecks.Run`をバッチ実行すると、プロジェクトの`Logs/package_smoke_results.json`へ結果を保存します。

## 検証範囲と今後の作業

- SDK導入済み環境で任意連携を網羅的に確認。
- 公開URLからVCC/ALCOMで新規導入・将来のバージョン更新を確認。
- 既存Assets版からの移行手順を整備。

認証情報、個人設定、非公開ツールはこのリポジトリへ含めません。

## ライセンス・連絡先

[MIT License](LICENSE)。各パッケージにも同じライセンスを同梱しています。

不具合は[Issues](https://github.com/d9speed/unity_editor_tools/issues)、連絡は d09pseed@gmail.com へお願いします。
