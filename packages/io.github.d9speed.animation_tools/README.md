# D9speed Animation Tools

Animator Playback PreviewとRandom Hand Muscle GeneratorをまとめたEditor専用パッケージです。Unity 2022.3.22f1を対象にしています。

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへ登録し、本パッケージを導入してください。Editor Core 0.1.1以降が自動導入されます。

## Animator Playback Preview

`D9speed > Animations > Animator Playback Preview`から開きます。対象Animatorと検索フォルダを指定し、Play ModeでController・State・AnimationClipを選んで再生します。ランダム再生や手指のマスクに対応します。

## Random Hand Muscle Generator

`D9speed > Animations > Random Hand Muscle Generator`から開き、有効なHumanoid Avatarを持つAnimatorを指定します。左右の手、指・親指・開きの範囲、変化の間隔を設定し、1回適用または定期的なランダム化を行えます。編集モードとPlay Modeに対応します。

自動処理には`HumanoidRandomHandPoseApi`を利用できます。これはEditor用APIです。ゲームのビルドに組み込むRuntime機能は含めません。2つのウィンドウは同じパッケージから導入され、操作画面は個別です。

旧Assets版との同時導入は型とGUIDが重複するため避けてください。VRChat SDKは不要です。

MIT License。連絡先: d09pseed@gmail.com
