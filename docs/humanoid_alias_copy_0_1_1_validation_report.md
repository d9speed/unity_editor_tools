# Humanoid Alias Copy 0.1.1 検証記録

2026-09-27、Unity 2022.3.22f1で検証しました。

## 修正

- 有効なHumanoid設定を最優先で使用し、指の標準キーの空白も吸収します。
- SkinnedMeshRendererの参照ボーンとその親階層を使い、実ボーンとコライダー用オブジェクトを区別します。
- 補助オブジェクトを階層パスで対応付け、コピー先の同名の実ボーンへ誤って割り当てることを防ぎます。
- リグ情報がない場合の従来の名前照合と、複数の実ボーンが競合する場合の停止・手動指定を維持します。

## 検証

`tools/humanoid_alias_validation/Editor/humanoid_alias_copy_checks.cs` の `D9speed.HumanoidAliasValidation.humanoid_alias_copy_checks.run` を検証専用プロジェクトから実行しました。

- SDKなし (`tools_validation`): 10/10成功。
- VRChat SDK 3.10.5あり (`vrc_tools_validation`): 12/12成功。
- 両環境のEditor Coreは0.1.6。結果は `humanoid_alias_copy_0_1_1_validation_results.json` に保存しています。

主な確認項目:

- 左右の太もも・すね・足の実ボーンと、同名に正規化される6個のコライダーが競合しないこと。
- SphereColliderとParentConstraintを専用階層にコピーし、追従先をコピー先の脚へ付け替えること。
- 再コピー時の更新・重複防止、コピー操作の一括Undo。
- 名前照合のみのリグ、未使用の親ボーン、複数リグの曖昧さ、手動対応、重複する補助オブジェクト名。
- コピー元の外側への参照を維持すること。
- 独自名のHumanoidボーンと指のマッピング、プレビュー後にボーン参照を変更した場合の再確認。
- VRCParentConstraintのSourceTransform、VRCPhysBoneのcolliders、VRCPhysBoneColliderのrootTransformの参照置換。

作業中のLime/Ruruneから読み取った階層情報（241/229 Transform、197/217スキンボーン）も検証プロジェクトで再構成しました。元のコピー対象32か所に検証用コンポーネントを配置し、曖昧な対応なしでコピーでき、コライダー用階層が実ボーンと分離されることを確認しました。この検証は階層とボーン参照を対象とし、実アセット全体のコピーや表示確認ではありません。元のメッシュ・マテリアル・アバター資産は複製・公開していません。ローカルの構造情報は配布物に含めません。

`importtest_2`のパッケージ・シーンは変更せず、パッケージは0.1.0のまま維持しています。更新はALCOMから行います。

## 配布物

- ZIP: `io.github.d9speed.humanoid_alias_copy-0.1.1.zip`
- サイズ: 26,764 bytes
- SHA-256: `a8de0c218b5e294054edbbaddf4547e67739b45fa171da2ac144f868b21905ae`
- タグ: `humanoid_alias_copy_v0.1.1`

マニフェスト、メタファイル、GUIDの一意性、Editor専用アセンブリを配布スクリプトで検査しました。既存0.1.0の配布物を保持し、VPM一覧に0.1.1を追加します。
