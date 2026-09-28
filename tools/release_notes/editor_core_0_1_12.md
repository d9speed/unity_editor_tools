Unity Searchから、読み込み中のシーンにあるSkinnedMeshRendererのブレンドシェイプ名を検索できるようにしました。

- `bs:smile` のように入力すると、名前の一部に一致するブレンドシェイプを持つGameObjectを表示します。大文字小文字は区別しません。
- `bs:"happy face"` のように空白を含む名前も検索できます。
- 検索結果には一致した名前を表示し、EnterまたはダブルクリックでGameObjectを選択できます。

Unity 2022.3.22f1でコンパイルと4種類の検索を確認しました。読み込み中のシーンが対象です。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#ブレンドシェイプ名の検索) · [検証記録](https://github.com/d9speed/unity_editor_tools/blob/main/docs/editor_core_0_1_12_validation_report.md)

VCC / ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.12へ更新してください。
