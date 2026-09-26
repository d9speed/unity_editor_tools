# D9speed Editor Core 0.1.8

Inspectorのロック／解除をCtrl+Lで切り替えられるようにしました。macOSの初期割り当てはCmd+Lです。

`Edit > Shortcuts` の `Custom/ShortCutEX/InspectorLock` からキーを変更できます。既存の割り当てと競合する場合も、この画面で調整できます。

複数のInspectorがある場合は、フォーカス中、マウス下、開いているInspectorの先頭の順に1つだけ切り替えます。HierarchyやSceneからも操作でき、Inspectorが閉じている場合は何もしません。

Unity 2022.3.22f1でコンパイルと5項目のバッチ検証が成功しました。ショートカットの登録、初期割り当て、ロック中の表示対象の固定、解除後の選択追従、複数Inspectorのうち1つだけの切り替え、未選択時・Inspectorなしの場合を確認しています。物理キー入力とマウス・フォーカスによる対象の優先順位はGUI上では未検証です。

VCC／ALCOMでリポジトリを更新し、D9speed Editor Coreを0.1.8へ更新してください。

[使い方](https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.editor_core#unity-utility)
