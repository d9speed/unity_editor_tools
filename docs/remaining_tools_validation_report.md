# 追加5パッケージの検証記録

2026-09-20、Windows上のUnity 2022.3.22f1で確認しました。

## 対象

すべて初版0.1.0です。

| パッケージ | 収録内容 | 依存 |
|---|---|---|
| Animation Tools | Animator Playback Preview、Random Hand Muscle GeneratorとEditor用API | Editor Core 0.1.1以降 |
| Skinned Mesh Tools | SkinnedMeshRendererEditorExtra | Editor Core 0.1.1以降 |
| Prefab Color Variants | PrefabColorVariantMaker | Editor Core 0.1.1以降 |
| Screen Texture Capture | キャプチャ画面とFFmpegセッション処理 | Windows Editor、利用者が別途指定するFFmpeg |
| Cloth Fitting Tools | PhysBone Weight Collider Generator、ProxVRCphysboneSettings | Editor Core 0.1.1以降、VRChat Avatars SDK 3.10.3以上・3.11.0未満 |

Unityプロジェクト本体から必要なソースとメタファイルをコピーしています。元ファイルは変更していません。キャプチャの個人用FFmpegパスを未指定に変更し、Windows以外ではアセンブリを読み込まない設定にしました。PhysBoneツールの特定アバターに依存する開発用テスト処理は配布から除外しました。

## SDKなしの検証

`tools_validation`で既存16項目と追加7項目、計23項目が成功し、Unityは終了コード0で終了しました。追加項目は以下のとおりです。

- 新規4パッケージの登録、ゲームのビルド対象に含まれないこと、FFmpeg実行ファイルが同梱されていないこと。
- アニメーション2画面、色違いVariant、キャプチャの各メニューから起動できること。
- 合成したHumanoidで手指のマッスルが変化し、停止後に元のポーズへ戻ること。不正な対象が拒否されること。
- Animatorプレビュー用グラフと手指マスクを生成・破棄できること。
- 合成したBlendShapeを拡張Inspectorで編集し、Undo・リセットできること。
- 検証用に新規作成したPrefabからVariantを保存でき、指定マテリアルだけが置き換わり、元の検証用Prefabのファイル内容が変わらないこと。
- キャプチャでFFmpeg未指定が拒否され、一時割り当てしたマテリアルのTextureが復元されること。

実行処理は`tools/validation_project/Editor`、結果は検証プロジェクトの`Logs/package_smoke_results.json`です。

## VRChat SDK入りの検証

別の新規プロジェクト`vrc_tools_validation`へ、vrc-get 1.9.2で公式VRChat Avatars SDK 3.10.3を取得しました。公開対象10パッケージを同時に入れ、次の4項目が成功しました。

- 10パッケージとSDK 3.10.3が共存し、D9speedのコードがEditor専用であること。
- 衣装調整の両メニューを開けること。
- 合成したウェイト付きメッシュから、対象ボーン・寸法が正しいカプセルコライダーを作成し、Undoで戻せること。
- ProxyにRotation / Parent Constraintを作成し、参照先・正規化されたウェイト・元の位置が維持されること。

実行処理は`tools/vrc_validation_project/Editor`、結果は検証プロジェクトの`Logs/cloth_tools_smoke_results.json`です。

## 配布と確認範囲

配布ZIPは`artifacts/remaining_tools_20260920`に生成し、内容とハッシュを照合します。SDKとFFmpegの本体、既存プロジェクトの個人設定、利用者のアバターは含めません。依存の宣言は[VPM公式仕様](https://vcc.docs.vrchat.com/vpm/packages/)に従い、SDKの対応範囲を指定します。

実アバターごとの動作、VRChatへのアップロード、Play Modeでの長時間利用、Unity 6は未検証です。Screen Texture Captureの実画面取得・範囲選択・速度は今回の自動確認に含みません。FFmpegの[gdigrab](https://ffmpeg.org/ffmpeg-devices.html#gdigrab)と[ddagrab](https://ffmpeg.org/ffmpeg-filters.html#ddagrab)の対応状況は利用するビルドと環境に依存します。
