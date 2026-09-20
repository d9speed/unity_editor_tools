# D9speed Cloth Fitting Tools

VRChatアバター向けのPhysBoneコライダーを作成するEditor専用パッケージです。

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへ登録し、本パッケージを導入してください。Unity 2022.3.22f1、VRChat Avatars SDK 3.10.3、Editor Core 0.1.1を基準にしています。SDKとCoreは依存として導入されます。SDKの対応範囲は3.10.3以上・3.11.0未満です。

## PhysBone Weight Collider Generator

`D9speed > Tools > PhysBone Weight Collider Generator`を開き、SkinnedMeshRendererと対象ボーンを指定します。ウェイトがある頂点からカプセル型のVRC PhysBone Colliderを作成します。閾値・外れ値の除外・余白を設定でき、作成や調整はUndoに対応します。

Humanoid Alias Copy、Rename Tool、Prefab Color Variantsは個別パッケージです。廃止されたParent Constraint Batch SetupとCloth Penetration Detectorは含めません。旧Assets版との同時導入は型やGUIDが重複するため避けてください。

MIT License（本ツール）。VRChat SDK本体は同梱せず、公式リポジトリから依存として取得します。連絡先: d09pseed@gmail.com
