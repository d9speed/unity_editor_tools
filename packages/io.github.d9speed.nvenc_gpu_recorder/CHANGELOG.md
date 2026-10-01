# Changelog

## 0.1.2 — 2026-09-30

- HEVC/MP4のカメラ入力で、描画済みの映像を録画用RenderTextureへコピーする方式に変更。Game Viewカメラの描画先とアスペクト比を変更せず、停止後も元の表示を維持します。

## 0.1.1 — 2026-09-28

- 手動停止、シングルフレーム(png)、指定秒数、指定フレーム数の録画モードを追加。
- PlayモードのGame View解像度をキャプチャサイズへ反映するボタンを追加。
- 録画時のJSONと`.log`ファイル出力を削除。FFmpegのエラーは画面に表示。

## 0.1.0 — 2026-09-21

- NVENC GPU Recorderを初公開。Runtime・Editor・Windows x64用ネイティブプラグインを同梱。
- HEVC/MP4、透過ProRes 4444/MOV、透過PNG連番に対応。
- `D9speed / Recorder / FFmpeg Recorder` メニューを継承。
- FFmpegをPATHまたは利用者の指定先から使用。実行ファイルは同梱しません。
- 個人用の固定パスと開発用メニューを除去。録画結果・検証用アセットは配布対象外。
