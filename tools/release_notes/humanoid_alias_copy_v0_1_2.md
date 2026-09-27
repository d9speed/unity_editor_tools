コピー元・コピー先の一覧を、VRC PhysBone（Colliderを含む）、Unity / VRC Constraint、MAの3タブに分割しました。

- コピー実行は表示中タブでチェックした項目だけが対象です。タブ切り替え時も選択状態を保持します。
- Unity Collider、VRC Contact、Particle、マテリアルは「その他・共通」の専用ボタンからコピーできます。
- 他のツールと共通のFluent 2デザインに統一しました。ライト／ダーク表示、列幅調整、コピー範囲を明示した固定ボタンに対応します。

Editor Core 0.1.5以降が必要です。ALCOM / VCCから更新してください。

Unity 2022.3.22f1のSDKなし／SDK 3.10.5・MA 1.18.7ありの環境で、ボーン対応とコピーの回帰テスト23件、UIとコピー範囲のテスト22件を確認しました。

[検証記録・画面例](https://github.com/d9speed/unity_editor_tools/blob/main/docs/humanoid_alias_copy_0_1_2_validation_report.md)
