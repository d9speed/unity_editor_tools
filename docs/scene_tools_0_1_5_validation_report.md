# Scene Tools 0.1.5 validation

Unity 2022.3.22f1でコンパイルと32項目の検証が成功しました。
テスト: `tools/scene_tools_validation/capsule_selection_checks.cs`
結果: `docs/scene_tools_0_1_5_validation_results.json`

- カプセルの胴体・丸い先端・軸方向から見た円形範囲を選択でき、範囲外は選択しない。
- カプセルがメッシュの既定コントロールとスフィア相当のコントロールより優先され、距離0の標準Transformハンドルは引き続き優先される。
- MouseDownからMouseUpまでの入力で始点ボーンを選択。末端カプセルでは末端ボーンを選択。
- 右・中・Alt＋左、Viewツール、他のウィンドウ、Transformパネル上の入力を奪わない。
- ドラッグ・Escape・途中のAlt操作・カプセル非表示化では選択を確定せず、hotControlを解放する。
- 非表示・非アクティブ・選択禁止・最大深度外は選択対象外。重なった軸では近いボーンを選択。
- 平行投影・透視投影、角度、太さの18通りの組み合わせで輪郭内外を確認。
- 設定の初期値と保存・復元を確認。既存の視点操作用MouseDown保護を維持。

入力検証は、隔離したプロジェクトのEditorWindowに合成イベントを送り、実際のIMGUIのcontrol ID・hotControl・HandleUtilityの優先順位を用いて実施しました。Sceneビューのポインター有無は入力引数で指定しています。実マウスによるウィンドウ間操作、各種アバター・他社ツールとの組み合わせは自動検証の対象外です。
