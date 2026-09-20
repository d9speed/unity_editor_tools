# D9speed Humanoid Alias Copy

ヒューマノイドのボーン名の違いを辞書で対応付け、シーン上のオブジェクト間でコンポーネントをコピーします。

## 導入と使用

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMに登録し、`D9speed Humanoid Alias Copy`を導入してください。Editor Core 0.1.1以降が必要です。

1. Unityの`D9speed > Tools > Humanoid Alias Component Copier`を開きます。
2. コピー元とコピー先のシーンオブジェクトを指定します。
3. 対応先とコピー対象のプレビューを確認します。曖昧な対応は手動指定できます。
4. コピーを実行します。変更はUndoで戻せます。

既存コンポーネントの更新・追加、常に追加、既存をスキップの各モードに対応します。コピー対象からの除外と、コピー先に合わせた参照の置き換えも行います。PrefabアセットやPrefab編集モード内を直接コピー先にはできません。

## ボーン辞書

`Editor/Data/humanoid_bone_dictionary.json`を同梱しています。パッケージの配置先から自動で読み込むため、Blenderのアドオンや外部ファイルのパス設定は不要です。初版は54項目の辞書です。

この辞書は配布データです。パッケージ更新で置き換わるため、直接編集した内容の保存は保証されません。ユーザー独自辞書の指定は初版には含めません。

## 動作環境

Unity 2022.3.22f1。Editor専用で、VRChat SDKは必須ではありません。SDK固有コンポーネントの全組み合わせは未検証です。旧Assets版との同時導入は型やGUIDが重複するため避けてください。

MIT License。連絡先: d09pseed@gmail.com
