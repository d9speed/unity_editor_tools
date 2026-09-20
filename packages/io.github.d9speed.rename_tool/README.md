# D9speed Rename Tool

変更前・変更後をプレビューしながら、アセット、フォルダ、Hierarchy上の名前、Animatorの各種名称を一括置換します。

## 導入と使用

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMに登録し、`D9speed Rename Tool`を導入してください。Editor Core 0.1.5以降が必要です。

1. Unityの`D9speed > Rename Tool (UI Toolkit)`を開きます。
2. 検索文字列と置換文字列を入力し、対象のフォルダ・Hierarchy選択・Animatorを指定します。
3. 一覧で変更前後を確認し、適用する行のチェックを選びます。
4. `選択した N 件を適用`で実行します。検索条件を変えるとプレビューは自動で更新されます。

UIは日本語表示で、Fluent 2の共通スタイルを使用します。Unityのライト／ダークテーマに連動します。無効な正規表現は画面内に表示され、適用操作は無効になります。

正規表現、大文字小文字の区別、サブフォルダの検索に対応します。Hierarchyの名前変更はUndoに対応します。アセット・フォルダの移動や名前変更は適用前にプレビューで確認してください。

Animatorの対象はParameter、State、State Machine、Layerです。外部スクリプトなどに文字列として書かれた名前までは自動更新しません。

## 動作環境

Unity 2022.3.22f1、Editor専用。VRChat SDKは不要です。旧Assets版との同時導入は型やGUIDが重複するため避けてください。

MIT License。連絡先: d09pseed@gmail.com
