# D9speed Skinned Mesh Tools

SkinnedMeshRendererのInspectorに、BlendShapeの検索・操作、AnimationClipの読み書き、ウェイトを持つボーンの表示などを追加します。

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへ登録し、本パッケージを導入してください。Editor Core 0.1.1以降が必要です。

導入後、SkinnedMeshRendererを持つオブジェクトを選択すると拡張Inspectorが表示されます。専用のメニューウィンドウはありません。BlendShapeのリセット、現在値のAnimationClip保存、AnimationClipの先頭フレームの適用ができます。元のInspectorも表示します。

0.1.2では、リセットボタンの下のタブでBlendShapeの分類を切り替えます。タブが収まらない場合は左右ボタン・横スクロールバー・ホイールで移動できます。「すべて」では全項目を表示し、検索と非0フィルターは選択中のタブに適用します。リセットボタンはタブに関係なく全BlendShapeを0にします。

タブの色やフォントはUnity標準のToolbar表示を使い、EditorのLight/Darkテーマに従います。

0.1.1では、未編集のBlendShapeの保存値がないモデルでも、分類内のスライダーを0として表示します。Inspectorを開くだけでは値を保存せず、初めてスライダーを編集したときに必要な保存領域を補います。

Unity 2022.3.22f1、Editor専用。VRChat SDKは不要です。同じSkinnedMeshRenderer用のカスタムInspectorを提供する別ツールとは表示が競合する場合があります。旧Assets版との同時導入は避けてください。

MIT License。連絡先: d09pseed@gmail.com
