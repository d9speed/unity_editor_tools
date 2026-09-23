# D9speed Editor Core 0.1.7

Hierarchyの右クリックに「選択オブジェクト名をコピー」を追加しました。

オブジェクトを複数選択して実行すると、`Body,Skirt,Glasses` のように名前だけをカンマ区切りでクリップボードへコピーします。区切りに空白や改行は加えません。

- 単一選択、親子の同時選択、非アクティブ・同名オブジェクトにも対応。
- 対象が未選択の場合はメニューを無効化し、クリップボードの内容を維持。

Unity 2022.3.22f1のコンパイラー・参照アセンブリでEditor Core全体のコンパイルを確認しました。Unity画面での右クリック操作は未検証です。

VCC／ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.7へ更新してください。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#unity-utility)
