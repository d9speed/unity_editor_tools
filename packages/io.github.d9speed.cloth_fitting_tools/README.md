# D9speed Cloth Fitting Tools

VRChatアバター向けのPhysBoneコライダーを作成するEditor専用パッケージです。

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへ登録し、本パッケージを導入してください。Unity 2022.3.22f1、VRChat Avatars SDK 3.10.3、Editor Core 0.1.1を基準にしています。SDKとCoreは依存として導入されます。SDKの対応範囲は3.10.3以上・3.11.0未満です。

## PhysBone Weight Collider Generator

`D9speed > Tools > PhysBone Weight Collider Generator`を開き、SkinnedMeshRendererと対象ボーンを指定します。ウェイトがある頂点からカプセル型のVRC PhysBone Colliderを作成します。閾値・外れ値の除外・余白を設定でき、作成や調整はUndoに対応します。

調整欄の「回転の中心」で、従来の「コライダー中心」と「ヒューマノイドボーン」を切り替えられます。「ヒューマノイドボーン」では、Root Transform（未指定時はコライダー自身）から親方向に最初に見つかるHumanoidボーンのピボットを中心に、コライダーの位置と向きを一緒に回転します。回転中心のボーン名は欄内に表示されます。対応するボーンがない場合は回転操作が無効になるため、Humanoid AnimatorとRoot Transformを確認してください。

切り替えだけではコライダーは動きません。ツール内の「回転 XYZ (度)」と「ボーンの軸に回転を合わせる」に適用され、位置と回転をまとめてUndo／Redoできます。角度の数値はどちらのモードもRoot Transform基準です。ボーン自体のTransformは変更しません。Unity標準の回転ツールやSDKのInspectorの動作は変更しません。

Humanoid Alias Copy、Rename Tool、Prefab Color Variantsは個別パッケージです。廃止されたParent Constraint Batch SetupとCloth Penetration Detectorは含めません。旧Assets版との同時導入は型やGUIDが重複するため避けてください。

MIT License（本ツール）。VRChat SDK本体は同梱せず、公式リポジトリから依存として取得します。連絡先: d09pseed@gmail.com
