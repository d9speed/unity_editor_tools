# D9speed NVENC GPU Recorder

Unityのカメラまたは描画済みRenderTextureを動画に保存するツールです。Runtime・Editor・専用ネイティブDLLを一つのVPMパッケージで導入できます。

メニュー: **D9speed → Recorder → FFmpeg Recorder**

## 動作環境

- Windows x64、Unity 2022.3.22f1以降の2022.3系。
- HEVC録画: HEVCエンコードに対応したNVIDIA GPU、Direct3D 11、NVENC API 13.0対応ドライバー。使用ヘッダーの[最低ドライバー条件は570.0](https://github.com/FFmpeg/nv-codec-headers/blob/n13.0.19.0/README)です。GPUごとの対応は異なります。
- 動画録画にはFFmpegを別途用意してください。シングルフレームPNGには不要です。FFmpeg本体、NVIDIAドライバー、CUDA Toolkitは同梱しません。CUDA Toolkitの追加インストールは不要です。
- Built-in Render Pipelineはカメラを直接指定できます。URP/HDRPは描画済みRenderTextureを指定してください。
- Editor Core、VRChat SDK、NuGetへの依存はありません。

## 導入・録画

1. [配布ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMにリポジトリを登録し、**D9speed NVENC GPU Recorder**を追加します。
2. メニューを開き、録画モード・カメラまたはRenderTexture・解像度を指定します。動画は出力形式とFPSも指定します。
3. 動画を録画するときは`ffmpeg.exe`に実行ファイルのフルパスを指定します。PATHに登録済みなら自動検出します。HEVC用・ProRes用の指定は個別に保存されます。
4. 出力フォルダーを確認します。初期値はプロジェクト内の`Recordings/nvenc_gpu`です。
5. Playモードに入り「録画開始」または「PNGを保存」を押します。手動停止モードでは「停止してMP4を保存」または「停止してMOVを保存」で終了します。

録画対象のカメラ・RenderTextureを描画し続けてください。URP/HDRPの描画設定をこのツールが自動作成することはありません。
Built-in Render PipelineのHEVCカメラ入力は、カメラの描画済み映像を録画用RenderTextureへコピーします。録画中もGame Viewカメラの描画先とアスペクト比は変更しません。

## 録画モードとキャプチャサイズ

- **手動停止**: 停止ボタンを押すまで動画を録画します。
- **シングルフレーム(png)**: 次の描画フレームをPNGで1枚保存します。透過背景を選べます。FFmpegは不要です。
- **指定秒数**: 秒数×FPSを切り上げたフレーム数で動画を自動停止します。実時間ではなく出力動画の長さを指定します。
- **指定フレーム数**: 指定した枚数を録画すると自動停止します。ProResのリアルタイム録画では取りこぼしたフレームを数えず、指定枚数まで続けます。

「キャプチャサイズをGame Viewに合わせる」はPlayモード中のGame View解像度を幅・高さに設定します。動画ではエンコーダーの制約により奇数値を直下の偶数へ調整します。PNGでは元の値をそのまま使用します。

## 出力形式

| 形式 | 処理 | 条件・注意点 |
|---|---|---|
| HEVC / MP4 | GPU内の画像をネイティブNVENCへ渡し、FFmpegで再圧縮せずMP4へ格納 | NVIDIA / D3D11専用。アルファなし。FFmpegの`setts`ビットストリームフィルターが必要 |
| ProRes 4444 / MOV | GPUから画像を読み戻し、外部FFmpegで圧縮 | アルファ付き。`prores_ks`対応FFmpegが必要。容量・CPU負荷に注意 |
| 透過PNG連番 | 録画したMOVからFFmpegで出力 | 「停止後に透過PNG連番も出力」を有効にするか、停止後の出力ボタンを使用 |

全形式で音声は録音しません。デスクトップ画面の範囲キャプチャ機能ではありません。

ProResの「Vulkanを優先」は、対応するFFmpegビルド・GPU・ドライバーがある場合に利用します。`prores_ks_vulkan`、`libplacebo`、`hwupload`などの機能を録画前に実エンコードで確認し、利用できなければCPUの`prores_ks`へ切り替えます。Vulkan経路は環境差があるため、短い録画で結果を確認してください。

Vulkan経路のMOVはプリマルチプライド、CPU経路はストレートアルファです。PNG出力時にはストレートへ変換します。Vulkan経路のPNG変換には`setparams=alpha_mode`と`format=alpha_modes`に対応したFFmpegが必要です。検証にはFFmpeg 8.1 full buildを使用しています。

## 設定と保存

- 幅・高さは16以上の偶数、FPSは1〜240。GPU・エンコーダーの上限は別途適用されます。
- 「上下反転」はD3D11で通常オンです。出力の上下を確認して調整してください。
- 「ゲーム時間をFPS固定」はオフライン描画向けです。終了時に元の`Time.captureFramerate`へ戻します。実時間に合わせた録画ではオフにしてください。
- 解像度・FPS・描画負荷によっては実時間どおりに録画できません。ProResのリアルタイム録画では画面にドロップ数を表示します。
- 出力先が既に存在する場合は上書きしません。HEVCの中間ファイルは出力先へ保存します。録画時のJSON・ログファイルは生成しません。
- FFmpegの場所や出力先はローカルのEditorPrefsに保存します。
- Playモード終了・スクリプト再読み込み・Editor終了時に録画を停止します。保存が完了するまで待ってください。

## 旧Assets版からの移行

旧`nvenc_gpu_recorder`と本パッケージの同時導入は避けてください。スクリプト・アセンブリ・GUIDが重複します。Unityを終了してから旧フォルダーと隣接する`.meta`をプロジェクト外へ退避し、VCC/ALCOMで本パッケージを導入してください。録画結果は退避先から別途取り出せます。既存のFFmpeg指定などのEditorPrefsキーは引き継ぎます。

Screen Texture Captureとは別のパッケージです。公開一覧からの削除によって、既に導入済みのScreen Texture Captureが自動削除されることはありません。不要な場合はVCC/ALCOMのプロジェクト管理画面から削除してください。

## 開発・ライセンス

- Runtime: `D9speed.NvencGpu.Runtime`。`gpu_camera_recorder`、`gpu_recorder_session`、`D9speed.Recording.AlphaCaptureRecorder`を含みます。
- Editor: `D9speed.NvencGpu.Editor`。Playerには含まれません。
- ネイティブプラグインはWindows x64のEditor / Standaloneに限定しています。Standalone Player、IL2CPP、Unity 6、長時間録画の公開パッケージ検証は未実施です。VRChatクライアント内の録画機能ではありません。
- `native~/build_native.ps1`はVisual StudioのC++ x64ビルドツールとWindows SDKでDLLを再ビルドします。生成物は一時フォルダーへ置き、DLLとインポート設定を`Plugins/x86_64`へ出力します。再配布時は`Runtime/native_build.cs`が参照するDLLのみを残してください。
- 本ツールは[MIT](LICENSE.md)。第三者ヘッダーの著作権表示・条件は[third_party.md](third_party.md)と同梱ヘッダーを参照してください。

連絡先: d09pseed@gmail.com
