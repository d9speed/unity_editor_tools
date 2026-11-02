---
name: uloop-d9-package-export
toolName: d9-package-export
description: "Batch-export D9speed outfit assets to .unitypackage files, one per avatar subfolder, with D9speed Package Exporter. Use when the user asks (often casually, in Japanese) to export, package, or 書き出し an outfit for one or more avatars."
---

# uloop d9-package-export

D9speed Package Exporter（`D9speed > ExportBatch > ExportBatch`）を画面なしで実行します。
エクスポート元フォルダの**直下のサブフォルダ1つにつき1つ**の `.unitypackage` を書き出します。
衣装アセットでは、エクスポート元フォルダ＝衣装フォルダ、サブフォルダ＝アバター別フォルダとして使います。

## Usage

```bash
uloop d9-package-export [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--mode` | string | `plan` | `list`: サブフォルダ（アバター）一覧 / `plan`: 書き出さずに確認 / `export`: 書き出す |
| `--profile-path` | string | - | プロファイルJSON（絶対パスかプロジェクト相対）。省略時はExportBatch画面の現在の設定 |
| `--export-path` | string | - | エクスポート元フォルダ（`Assets/` から）。例: `Assets/D9speed/Outfits/Shirt` |
| `--out-path` | string | - | `.unitypackage` の出力先フォルダ |
| `--package-name` | string | - | ファイル名の先頭のパッケージ名 |
| `--subfolders` | array | - | 書き出すサブフォルダ名（完全一致、カンマ区切り）。省略時はプロファイルの選択、なければ全部 |
| `--overwrite` | flag | - | 同名の `.unitypackage` を上書きする |
| `--allow-outside-root` | flag | - | 許可フォルダ外のアセットが含まれていても書き出す |
| `--allowed-root` | string | - | 許可フォルダ（`;` 区切り）。省略時は Preferences > D9speed > Package Exporter の値 |
| `--list-assets` | flag | - | 各パッケージの全アセットパスを結果に含める |

## 口語の指示を実行する手順（必ずこの順で）

1. **エクスポート元を決める。** 衣装名からフォルダを探します。プロファイルJSONがあればそれを使います。
   ```bash
   uloop d9-package-export --mode list --export-path "Assets/D9speed/Outfits/Shirt"
   ```
2. **アバター名を `AvailableSubfolders` の正確な名前に対応させる。** 「マヌカ」→`Manuka` のような読み替えは行ってよいですが、
   候補が複数ある・見つからない場合は推測で選ばず、候補を示してユーザーに確認します。存在しない名前を渡すと、何も書き出さずにエラーになります。
3. **`plan` で確認する（既定のモード）。** 出力ファイル名・件数・警告をユーザーに要約して見せます。
   ```bash
   uloop d9-package-export --export-path "Assets/D9speed/Outfits/Shirt" --subfolders "Manuka,Shinano"
   ```
4. **同じ引数に `--mode export` を足して書き出す。**
   ```bash
   uloop d9-package-export --mode export --export-path "Assets/D9speed/Outfits/Shirt" --subfolders "Manuka,Shinano"
   ```
5. 結果の `Packages[].Status` と `PackagePath` を報告します。

## 安全のための決まり

- `--overwrite` と `--allow-outside-root` は、ユーザーがその回の依頼で明示的に許可したときだけ付けます。自分の判断で付けません。
- `Status` が `blocked_outside_root` のときは、`OutsideRootAssets` の一覧をユーザーに見せて判断を仰ぎます。
  他者の販売アセットやシェーダーが混ざっている可能性があります。
- `export` は1件でも `blocked_outside_root` があると、**1つも書き出さずに**終了します（`not_run`）。
- `Success` が `false` のときは `Errors` を読んで原因を伝え、引数を変えて黙って再試行しません。
- ExportBatchの設定（ProjectSettings）やプロファイルJSONを書き換えません。このツールも書き換えません。
- プレイモード中やコンパイル中は実行できません。

## Status の意味

| Status | 意味 |
|--------|------|
| `ready` | plan: 書き出し可能 |
| `would_skip_exists` | plan: 同名ファイルがあるため `--overwrite` なしでは書き出さない |
| `exported` | 書き出し完了（`ReportPath` にコンポーネント情報JSON） |
| `skipped_exists` | 同名ファイルがあるため書き出していない |
| `blocked_outside_root` | 許可フォルダ外のアセットを含むため止めた |
| `no_assets` | 対象アセットがない |
| `not_run` | 他のパッケージの問題で、全体を書き出さなかった |
| `failed` | 書き出しに失敗 |

## Output

JSON: `Success`, `Mode`, `Message`, `ProfilePath`, `ExportPath`, `OutPath`, `AllowedRoot`,
`AvailableSubfolders`, `SelectedSubfolders`, `Packages[]`（`subfolder`, `packageFile`, `packagePath`, `reportPath`, `status`, `fileExists`, `assetCount`, `outsideRootAssets`, `assets`）, `Warnings`, `Errors`。

## このツールが使えない場合

uloop V2 などでカスタムツールが読み込まれない場合は、同じ処理を `execute-dynamic-code` から呼べます。依頼JSONのキーは上のパラメーターのcamelCase版です（`mode`, `exportPath`, `subfolders` など）。

```bash
uloop execute-dynamic-code --code 'return D9speedBaseEditorUtil.PackageExporterHeadless.RunJson(@"{""mode"":""plan"",""exportPath"":""Assets/D9speed/Outfits/Shirt"",""subfolders"":[""Manuka""]}");'
```
