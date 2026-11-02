# Changelog

## 0.2.0 - 2026-10-28

- 画面を開かずに実行できる入口`PackageExporterHeadless`を追加。`list` / `plan` / `export`の3モードとJSONの依頼・結果に対応。
- Unity CLI Loop 3.0以降向けのカスタムツール`uloop d9-package-export`と、LLM向けの手順書（Skill）を同梱。uloop未導入のプロジェクトではコンパイルされない。
- `-batchmode -executeMethod D9speedBaseEditorUtil.PackageExporterCommandLine.Run`からの実行に対応。
- 許可フォルダ（初期値`Assets/D9speed/`）以外のアセットが含まれる場合の警告を追加。画面では確認ダイアログ、CLIでは書き出しを停止。
- 許可フォルダを`Edit > Preferences > D9speed > Package Exporter`で設定可能に（EditorPrefsに保存、`;`区切りで複数指定）。

## 0.1.5 - 2026-09-21

- 除外キーワードの入力欄が削除ボタンを枠外へ押し出す問題を修正。
- 「サブフォルダを取得」ボタンをグリーンに変更。

## 0.1.4 - 2026-09-21

- 除外キーワードの入力行を「1. 入力欄」の横並びに変更。

## 0.1.3 - 2026-09-21

- 除外する拡張子・キーワードの一覧と追加ボタンを、初期状態で閉じた折りたたみに整理。
- 折りたたみの開閉状態を保持。

## 0.1.2 - 2026-09-21

- 本体をFluent 2の共通UIへ統一。設定をセクションに整理し、実行ボタンを下部へ固定。
- コンポーネント比較画面をIMGUIからUI Toolkitへ移行。複数JSON比較、絞り込み、列幅変更、並べ替え、配置詳細を維持。
- ライト／ダークテーマと小さいウィンドウでのスクロールに対応。
- Editor Coreの必要バージョンを0.1.5以降へ更新。

## 0.1.1 - 2026-09-20

- コンポーネント情報JSONの比較ツールPackage Component Report Compareを同梱。
- `D9speed > ExportBatch`の下に、本体`ExportBatch`と比較ツールを並べて配置。
- 比較ツールで欠落・追加・個数・配置の違いを確認可能に。

## 0.1.0 - 2026-09-20

- バッチエクスポーターを独立したEditor専用VPMパッケージとして公開。
- スタイルをパッケージ内から読み込み。
- Newtonsoft Json 3.2.1への依存を宣言。
- 個人用Discord通知への依存と固定ドライブの初期パスを除去。
- JSONプロファイルを読み直した際、追加文字列などのリストが重複する問題を修正。
