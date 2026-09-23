# Changelog

## 0.1.2 - 2026-09-23

- 回転の中心を「コライダー中心」と「ヒューマノイドボーン」から選択するモードを追加。
- ヒューマノイドボーンのピボットを中心に、コライダーの位置と向きを連動して回転。位置・回転をまとめてUndo／Redo可能。
- 回転中心のボーン名を表示し、対応するHumanoidボーンが見つからない場合は回転操作を無効化。
- ツール内の回転入力とボーン軸合わせに適用。モード切り替えだけでは位置・向きを変更しない。

## 0.1.1 - 2026-09-21

- ProxVRCphysboneSettingsを廃止し、ソースとメニューを削除。
- 収録内容をPhysBone Weight Collider Generatorのみに変更。

## 0.1.0 - 2026-09-20

- PhysBone Weight Collider GeneratorとProxVRCphysboneSettingsをVPMパッケージとして公開。
- VRChat Avatars SDKとEditor Coreへの依存を宣言。
- 個別アバターに依存する開発用テスト処理を配布対象から除外。
