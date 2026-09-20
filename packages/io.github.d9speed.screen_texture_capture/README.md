# D9speed Screen Texture Capture

Windowsデスクトップの指定範囲を、Unity Editor内のライブTextureとして表示します。DXGI Desktop DuplicationとGDIに対応し、指定マテリアルのTextureプロパティへの一時割り当てもできます。

## 導入と使用

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへ登録し、本パッケージを導入してください。

1. 利用するFFmpegを別途用意します。本パッケージには実行ファイルを同梱しません。
2. `D9speed > Tools > Screen Texture Capture`を開き、`ffmpeg.exe`の場所を指定します。
3. 取得範囲・解像度・フレームレートを設定して開始します。範囲のドラッグ選択も利用できます。
4. 必要ならマテリアルとTextureプロパティを指定して割り当てます。停止・終了時は元のTextureを復元します。

FFmpegは[公式サイト](https://ffmpeg.org/download.html)で配布情報を確認できます。DXGIには`ddagrab`、GDIには`gdigrab`に対応したWindowsビルドが必要です。初期状態で実行ファイルのパスは未指定です。設定はローカルのEditorPrefsに保存します。

## 動作環境

Windows版Unity 2022.3.22f1専用です。macOS / Linuxではツールのアセンブリを読み込みません。Editorでのプレビュー用で、ゲームやVRChatのRuntimeには入りません。VRChat SDKとEditor Coreは不要です。

旧Assets版との同時導入は型やGUIDが重複するため避けてください。実際のキャプチャ速度や利用可能な方式はGPU・ドライバー・FFmpegの構成によって異なります。

MIT License（本ツール）。外部FFmpegにはその配布物のライセンスが適用されます。連絡先: d09pseed@gmail.com
