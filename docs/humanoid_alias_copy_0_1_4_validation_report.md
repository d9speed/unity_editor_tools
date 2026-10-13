# Humanoid Alias Copy 0.1.4 検証記録

2026-10-13、Unity 2022.3.22f1で検証しました。

## 変更内容

「SkinnedMeshペアがある場合、MAの同期一覧・素体参照を未設定でコピー」を標準で有効にしました。

- ペアになったメッシュ上のMA Blend Shape Syncの`Bindings`を空にします。
- MAのコピー元階層外・未解決の`AvatarObjectReference`は、`referencePath`と`targetObject`を両方解除します。
- 階層外への直接のシーン参照と、Bone Proxyの`boneReference`・`subPath`も解除します。
- 衣装内の参照はコピー先のオブジェクト・参照パスへ対応付けます。
- Bounds、動作モード、アセット、その他の設定を保持します。
- 編集時の古いシェイプ同期とBone Proxyのキャッシュも解除します。
- コピー設定から無効にできます。ペアがない場合は従来の動作です。

## 検証

検証専用プロジェクトの合成データで実行しました。制作中のプロジェクト・アバター・Prefabは変更していません。

| 環境 | ボーン対応・コピー回帰 | MA参照・設定保持 | UI・コピー範囲 |
| --- | --- | --- | --- |
| SDKなし / Editor Core 0.1.6 | 11/11成功 | 1/1成功（既定設定と通常Constraintへの非適用） | 13/13成功 |
| SDK 3.10.5 / MA 1.18.7 / NDMF 1.14.8 / Editor Core 0.1.6 | 12/12成功 | 11/11成功 | 15/15成功 |

新しいMA検証は`tools/humanoid_alias_validation/Editor/humanoid_alias_ma_checks.cs`です。

- 異なる名前のメッシュをペアにし、空のBlend Shape Syncを追加。コピー元と有効状態を保持。
- Mesh Settingsの直接参照・パスだけの素体参照を解除し、Bounds・継承モードを保持。
- 衣装内部への直接参照・パスだけの参照を、コピー先のメッシュ名と階層へ対応付け。
- Bone Proxy、別階層のMerge Armature、Outfit Rootの素体参照を解除し、その他の設定を保持。
- 未解決パス・アバタールート参照を解除し、Merge Animatorのアセットと設定を保持。
- 無効設定・ペアなし・既存スキップ・個別除外の動作を確認。
- 既存更新・再コピーの重複防止・Undoを確認。
- 実際のBlendShapeを持つ合成Meshを使い、コピー直後に旧素体からの編集時同期が停止することを確認。
- JSONで保存・再読み込みして、素体への参照と古いパスが復活しないことを確認。

[検証結果JSON](humanoid_alias_copy_0_1_4_validation_results.json)

アバター全体のNDMFビルド・VRChat実機確認は今回のコンポーネントコピー検証の対象外です。コピー後の素体参照は、利用するアバターに合わせて設定してください。

## 配布物

- ZIP: `io.github.d9speed.humanoid_alias_copy-0.1.4.zip`
- サイズ: 35,008 bytes
- SHA-256: `1b17597680432b3ebceb6ab7317f8ec3df8bf5a992e336388163da0b0932992f`
- タグ: `humanoid_alias_copy_v0.1.4`

配布スクリプトでマニフェスト・GUID・Editor専用アセンブリを検査しています。VPM一覧の既存43バージョンを保持し、0.1.4だけを追加します。
