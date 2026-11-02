Package Exporterを画面なしで実行できるようにし、uloop（Unity CLI Loop）やLLMからの書き出しに対応しました。

- `uloop d9-package-export`で、サブフォルダ（アバター）ごとのバッチ書き出しを実行できます。既定は書き出さずに内容を確認する`plan`で、`--mode export`で書き出します。Unity CLI Loop 3.0以降が導入されている場合だけ有効になります。
- Claude CodeやCodex向けの手順書（Skill）を同梱しました。`uloop skills install --claude`（Codexは`--agents`）で登録できます。
- `-batchmode -executeMethod D9speedBaseEditorUtil.PackageExporterCommandLine.Run`からも実行できます。
- 許可フォルダ（初期値`Assets/D9speed/`）以外のアセットが含まれる場合に警告します。画面では確認ダイアログを出し、CLIでは書き出しを止めます。
- 許可フォルダは`Edit > Preferences > D9speed > Package Exporter`で変更できます。`;`区切りで複数指定できます。

ALCOM / VCCから0.2.0に更新してください。
