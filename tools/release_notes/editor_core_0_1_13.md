Unity標準のHierarchy検索で、ブレンドシェイプ名を他の条件と組み合わせて検索できるようにしました。

- `h:t:SkinnedMeshRenderer bs:smile` でSkinnedMeshRendererの`Smile_Left`などを検索できます。
- 大文字小文字を区別せず、`bs:"happy face"`のような空白を含む名前も検索できます。
- 従来の単独検索`bs:smile`も引き続き利用できます。

Unity 2022.3.22f1でコンパイルと8種類の検索を確認しました。読み込み中のシーンが対象です。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#ブレンドシェイプ名の検索) · [検証記録](https://github.com/d9speed/unity_editor_tools/blob/main/docs/editor_core_0_1_13_validation_report.md)

VCC / ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.13へ更新してください。
