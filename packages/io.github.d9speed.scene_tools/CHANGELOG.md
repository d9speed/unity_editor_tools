# Changelog

## 0.1.1 - 2026-09-24

- Transformの親子を線で結ぶ「ボーン階層（親子線）」表示を追加。詳細設定から切り替え可能。
- カーソル下のSkinnedMeshRendererによる絞り込みを解除し、未使用・補助ボーンを含む現在の編集ステージ内のTransformを表示対象に変更。非表示・非アクティブは除外。
- 2カラムの名前ラベルから、選択を変えずにTransform参照をインスペクターへドラッグ可能に。クリック時は従来どおり選択。
- 最大深度の初期値と設定リセット値を0（無制限）に変更。既存の保存値は維持。
- スフィアサイズの設定・保存・復元の上限を1.0に統一。
- Transformの検索結果をキャッシュし、階層や編集ステージが変わった時に更新。

## 0.1.0

- DisplayChildNamesInSceneとscene_package_info_overlayをScene Toolsへ収録。
- Editor専用asmdefを追加し、Editor Coreへの依存を明示。
- 既存のツールソースと.metaを維持。
