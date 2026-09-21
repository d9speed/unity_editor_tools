# Transform Mirror 0.1.6 検証

実施日: 2026-09-21。Unity 2022.3.22f1 / Windows。

対象パッケージ: D9speed Editor Core 0.1.6。

## 結果

SDKなし8項目、VRChat SDK 3.10.5入り13項目、UI 8項目の計29項目が成功しました。SDK入りの項目にはSDKなしと共通の回帰確認も含みます。

- Blender形式の左右末尾名、小文字、.001形式の連番。
- 子階層のDFS順、重複・親子選択の整理、ワールド位置・回転、Undo／Redo。
- 最寄りPrefabルートを探索範囲にすること、左右パスの優先、一意な左右名、曖昧な候補の警告。
- Unity Constraintの外部参照・null参照・複数選択間参照・オフセット、対称位置にない元参照を維持した場合の位置補正。
- 既存の同名オブジェクトを上書きしないこと。
- Box／Sphere／Capsule Colliderの中心・寸法、回転反転を外した場合の挙動。
- シーン内Prefabのリンクと設定オーバーライド、追加した子・削除した子・削除したコンポーネントの保持。
- VRC Parent Constraintの18ソース（16枠を超える分を含む）、ロック済み位置・回転オフセット、元コンポーネントの不変性。
- VRCのローカル空間計算、Freeze To World、disabled状態の維持。
- PhysBoneのRoot／Ignore／Collider参照、Endpoint／Limit、PhysBone Colliderの位置・回転。
- 複製範囲外のTargetTransformを持つVRC Constraintについて、警告して複製側を無効にすること。
- Editorの後続更新後にConstraintの対称化した配置とオフセットが保持されること。
- UIの2ペイン表示、ドロップ、対象数、設定の保持、実行ボタン、ライト／ダーク、狭いウィンドウでのスクロール。

検証には専用プロジェクト内の合成データを使用しています。ユーザーのアバターやPrefabアセットは変更しません。

## 再実行

専用検証プロジェクトにパッケージと次の検証コードを配置して実行します。

- tools/validation_project/Editor/transform_mirror_checks.cs: D9speed.PackageValidation.TransformMirrorChecks.Run
- tools/validation_project/Editor/transform_mirror_ui_checks.cs: D9speed.PackageValidation.TransformMirrorUiChecks.Run

バッチモードで起動し、後続Editor更新の確認が終わるまで -quit は付けません。UI検証はグラフィックスデバイスが必要なので -nographics を付けません。

結果は検証プロジェクトの Logs/transform_mirror/checks.json と Logs/transform_mirror_ui/checks.json、画面画像は Logs/transform_mirror_ui に出力します。

## 範囲

MeshColliderの頂点形状、描画メッシュ、AnimationClipのパス・値の反転は対象外です。任意の実アバター構成、すべてのSDKバージョン、実クライアントの物理シミュレーションまでは網羅していません。

VRC設定変更後の通知とプロパティ名は[VRChat公式Constraints API](https://creators.vrchat.com/common-components/constraints/constraints-api/)を確認しています。
