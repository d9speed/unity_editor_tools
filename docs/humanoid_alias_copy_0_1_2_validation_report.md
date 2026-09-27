# Humanoid Alias Copy 0.1.2 検証記録

2026-09-27、Unity 2022.3.22f1で検証しました。

## 変更内容

- コピー一覧をVRC PhysBone（Colliderを含む）、Unity / VRC Constraint、MAの3タブに分割。
- メインボタンは表示中タブのチェック済み項目だけをコピー。タブを切り替えても各行の選択状態を保持。
- Unity Collider、VRC Contact、Particle、マテリアルは「その他・共通」の専用ボタンからコピー。
- Editor CoreのFluent 2共通テーマを使用。ライト／ダーク、列幅変更、全文ツールチップ、固定フッター、空の分類表示に対応。
- Editor Core依存の下限を共通UIが利用可能な0.1.5に更新。

## 検証結果

検証専用プロジェクトで次のEditorスクリプトを実行しました。

- `tools/humanoid_alias_validation/Editor/humanoid_alias_copy_checks.cs`
- `tools/humanoid_alias_validation/Editor/humanoid_alias_ui_checks.cs`

| 環境 | ボーン対応・コピー回帰 | タブ・UI・コピー範囲 |
| --- | --- | --- |
| SDKなし / Editor Core 0.1.6 | 11/11成功 | 10/10成功 |
| VRChat SDK 3.10.5 / MA 1.18.7 / NDMF 1.14.8 / Editor Core 0.1.6 | 12/12成功 | 12/12成功 |

タブごとのコピー、チェック解除、選択状態の保持、全件解除時の無操作、他分類とマテリアルへの影響がないこと、選択対象に必要な階層だけの作成、Undoを確認しました。PhysBoneから同一タブのColliderへの参照置換、MA Merge Animatorの設定コピー、Unity/VRC Constraintの参照置換も確認しています。

0.1.1の実ボーンとコライダー補助階層の区別、複数リグの曖昧さ、手動対応、更新時の重複防止、プレビュー変更検出も回帰検証しています。Lime/Ruruneのローカル階層メタデータを使った再構成テストも成功しました。実アバター資産全体を複製したテストではありません。

UIは実際のUI Toolkitパネルを描画し、ライト／ダーク、1200×850と最小幅860×620、3タブの表示、列幅の維持、仮想化セル、フッターの表示範囲を検証しました。スクリーンショットは合成テストデータです。

- [ダーク表示](humanoid_alias_copy_0_1_2_dark.png)
- [ライト表示](humanoid_alias_copy_0_1_2_light.png)
- [検証結果JSON](humanoid_alias_copy_0_1_2_validation_results.json)

`importtest_2`のパッケージ・シーンは変更していません。ALCOMから更新する運用です。

## 配布物

- ZIP: `io.github.d9speed.humanoid_alias_copy-0.1.2.zip`
- サイズ: 30,941 bytes
- SHA-256: `c36a1950534b7fc73a9f45c904424035bd6addb7994696576138ebb2880d9085`
- タグ: `humanoid_alias_copy_v0.1.2`

パッケージのマニフェスト、メタファイル、GUIDの一意性、Editor専用アセンブリを検査し、ZIP内の24ファイルがソースとバイト単位で一致することを確認しました。VPM一覧の既存34バージョンを変更せず、0.1.2だけを追加しています。
