# NVENC GPU Recorder 0.1.0 公開検証

2026-09-21、専用の新規Unityプロジェクトに配布ZIPを展開して確認しました。作業中のアバター・Prefabは使用していません。

## 環境と配布内容

- Windows x64 / Unity 2022.3.22f1 / Direct3D 11
- NVIDIA GeForce RTX 5090 / ドライバー591.44
- 外部FFmpeg 8.1 full build
- パッケージ: `io.github.d9speed.nvenc_gpu_recorder` 0.1.0
- ZIP: 184,023 bytes / SHA-256 `66a89b0182390dbdf0c8944fe8cd17f45ba3ca9f2b40584cc90d0f7e2e366e63`
- ネイティブDLLは同梱ソースからVisual Studio C++ x64で再ビルド。SHA-256 `1c3d4cda56ad76642da10224359ee58fbd6473fdd46583c692b7f4f23fa7bd45`
- NVIDIAヘッダーは明記した上流タグ`n13.0.19.0`の内容とSHA-256一致。著作権・許諾表示を保持。
- FFmpeg実行ファイル、録画結果、個人設定、開発用メニューはZIPに含めていません。

## Unityでの確認

7項目成功、Unity終了コード0。

1. パッケージ登録、RuntimeのPlayer向けアセンブリへの組み込み、Editorの分離、ネイティブDLLのWindows x64限定設定。
2. `D9speed / Recorder / FFmpeg Recorder`メニューからウィンドウを開けること。
3. 存在しないFFmpeg、奇数の解像度を拒否すること。
4. HEVC 1920×1080 / 60 fps / 180フレームの取得・圧縮・保存、MP4格納、ネイティブセッション解放。生画像CPU読み戻し0 bytes、入力RenderTextureの所有権維持、既存動画の上書き拒否。
5. カメラ入力のCPU ProRes 4444 / 640×360 / 30 fps / 12フレーム、PNG出力、カメラ設定と`Time.captureFramerate`の復元。
6. RenderTexture入力のVulkan ProRes 4444 / 640×360 / 30 fps / 12フレーム、PNG出力、終了処理。
7. 録画セッションが残らないこと。

## 保存した動画・PNGの確認

FFprobeで動画を全フレーム読み取り、コーデック・フレーム数・時刻・順序を確認しました。元画像との比較で上下・RGB・透明度を検証しました。

| 出力 | フレーム数 | RGB平均絶対誤差（0〜255） | アルファ最大誤差 | 結果 |
|---|---:|---:|---:|---|
| HEVC / MP4 | 180 | 0.263（先頭フレーム） | 対象外 | 成功 |
| CPU ProRes / MOV → PNG | 12 | 0.001未満（全フレーム中の最大値） | 0 | 成功 |
| Vulkan ProRes / MOV → PNG | 12 | 0.556未満（全フレーム中の最大値） | 0 | 成功 |

ProResのRGBはアルファ32以上の領域で比較し、アルファは全画素で比較しました。PNGは各12枚です。これは短時間の機能検証であり、実時間録画の性能を示すベンチマークではありません。

## 再実行

配布ZIPを専用プロジェクトの`Packages/io.github.d9speed.nvenc_gpu_recorder`へ展開し、`tools/nvenc_validation_project/Editor`のC#ファイルを`Assets/Editor`へコピーします。外部FFmpegをPATHへ登録するか、`D9_NVENC_FFMPEG`環境変数に指定します。

Unityを`-batchmode -force-d3d11 -executeMethod D9speed.PackageValidation.nvenc_package_smoke_checks.Run`で実行します。`-nographics`は使えません。テストが完了時の終了を制御します。結果はプロジェクト内の`Logs/nvenc_smoke`に保存します。

続いて`python tools/verify_nvenc_outputs.py <検証プロジェクト>/Logs/nvenc_smoke`を実行します。Pythonにはnumpy・Pillow、PATHにはFFmpeg・FFprobeが必要です。録画結果は公開リポジトリへ追加しないでください。

## 確認範囲

Standalone Player、IL2CPP、Unity 6、URP/HDRP、他GPU、長時間録画、Playモードのウィンドウ操作全体は今回の検証対象外です。Runtimeを同梱しますが、VRChatへアップロードしたアバターでこの録画処理が動くことを意味しません。

Screen Texture Captureは公開用ソース・VPM一覧・案内ページから削除し、本パッケージを追加します。既に導入済みのScreen Texture Captureや旧Assets版録画ツールを自動削除する処理はありません。
