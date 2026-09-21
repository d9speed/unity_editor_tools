# D9speed Editor Core 0.1.6

Transform Mirrorに、左右名・Constraint・PhysBone・Colliderの対称化と作成ペアの事前表示を追加しました。

- _L／_R、.L／.Rを交換。小文字と.001形式の連番にも対応。
- 最寄りPrefabルートからDFSで対応候補を探索し、作成元／作成予定先を2ペインに表示。
- Unity／VRC Constraintの参照とオフセットを対称化。VRCは配置・参照・オフセットを設定してからロック／有効状態を復元。
- PhysBoneのRoot・Ignore・Collider参照とEndpoint・Limit、PhysBone Colliderと標準Colliderの設定を対称化。
- 親子の二重複製と既存同名オブジェクトの上書きを防止。Prefabオーバーライド保持、まとめてUndo／Redoに対応。

Unity 2022.3.22f1、VRChat SDKなし／3.10.5ありの環境とUIで、計29項目を確認しました。VRChat SDKは必須依存ではありません。

曖昧な参照は警告して元参照を維持します。複製範囲外のTargetTransformを駆動するVRC Constraintは複製側を無効にします。MeshColliderの頂点形状・描画メッシュ・AnimationClipは反転対象外です。

VCC／ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.6へ更新してください。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#transform-mirrorのui) · [検証記録](https://github.com/d9speed/unity_editor_tools/blob/main/docs/transform_mirror_validation_report.md)
