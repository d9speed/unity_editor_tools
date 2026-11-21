# D9speed Editor Core

D9speedのEditor拡張が共有する補助処理と、普段使いの右クリックメニューです。オブジェクト検索、ヒューマノイド対応付け、階層パス、コンポーネントコピー、ファイル名の処理に加えて、Unity Utilityの各機能を収録します。

- 対応基準: Unity 2022.3.22f1。
- Editor専用。VRChat SDKは不要です。説明画像の描画にはWindows用の同梱DLLを使用します。
- アセンブリ名: `D9speed.EditorUtils`。
- 名前空間: `D9speed_BaseEditorUtils`。

説明画像のSprite作成は `D9speed.EditorUtils.GuideBoard` と `Renderer~/guide_board_renderer.dll` を使用します。DLLはWindows標準のフォント描画機能でPNGを生成します。TextMeshPro・TMP Essential Resources・別途インストールするフォントアセットは不要です。

`FindSceneObjects<T>()`は有効なシーンオブジェクトを取得します。`true`を渡すと非アクティブも含めます。`GetObjectId()`はEditorセッション内の識別子を返します。

## Unity Utility

| 操作場所 | 機能 |
|---|---|
| Projectの右クリック → `CopyFullPath` | ファイル・フォルダのフルパスをコピー。複数選択とPackages内の実体パスに対応 |
| Projectの右クリック → `CopyAssetsWithDependency` | 選択したアセット群／フォルダを複製し、複製対象どうしの参照をコピー先へ変更 |
| Hierarchyの右クリック → `Copy Animation Property Path` | 最も近いAnimatorを基準にしたパスをコピー。なければPrefabルート、シーンルートを使用 |
| Hierarchyの右クリック → `選択オブジェクト名をコピー` | 選択したオブジェクト名を `Body,Skirt,Glasses` のようにカンマ区切りでコピー。親子の同時選択・非アクティブ・同名オブジェクトにも対応 |
| Hierarchyの右クリック → `このヒエラルキー配下を検索` | 選択階層のパスをUnity Searchで検索 |
| Unity Search → `bs:smile` / `h:t:SkinnedMeshRenderer bs:smile` | 読み込み中のシーンのSkinnedMeshRendererで、名前に`smile`を含むブレンドシェイプを検索。結果からGameObjectを選択 |
| Componentの右クリック → `コンポーネント名をコピー` | コンポーネントの型名をコピー |
| Componentの右クリック → `ここから下のコンポーネントをコピー` | 選択コンポーネント以降をまとめてコピー |
| Component／Hierarchyの右クリック → `コピーしたコンポーネントを新規貼り付け` | シーンオブジェクトへ追加。Undo対応 |
| Hierarchyの右クリック → メインカメラ関連の2項目 | 対象へカメラを向ける／対象の+Z方向から正対。Undo対応 |
| `D9speed > Transform Mirror Tool` | X軸方向のミラー複製。左右名・Constraint／PhysBone／Colliderの対称化と作成ペアの事前表示 |
| `D9speed > Tools > 説明画像をSpriteで作成` | 文章を画像にし、Sprite設定のPNGを保存 |
| `D9speed > Tools > liltoonマテリアルセットアップ` | FBXから抽出したマテリアルへ、名前が対応するテクスチャを一括設定 |
| `Alt + R` | 選択したシーンオブジェクトのローカルTransformをリセット。Undo対応 |
| `Ctrl + L`（macOSは`Cmd + L`） | Inspectorのロック／解除を切り替え |
| Hierarchy | コンポーネントに変更があるPrefabインスタンスのルートへ変更マークを表示 |

Transform Resetの割り当てはUnityのShortcutsにある`Custom/ShortCutEX/TransformReset`で変更できます。

Inspectorのロックは `Edit > Shortcuts` の `Custom/ShortCutEX/InspectorLock` で割り当てを変更できます。初期設定はCtrl+L（macOSはCmd+L）です。同じキーを使う別の操作がある場合は、Shortcutsで割り当てを調整してください。複数のInspectorがある場合は、フォーカス中のInspector、マウス下のInspector、開いているInspectorの先頭の順に1つだけ切り替えます。HierarchyやSceneからも操作でき、Inspectorが開いていない場合は何もしません。

### ブレンドシェイプ名の検索

Unity Searchで`bs:smile`のように入力します。`bs:`はEditor Coreが追加する専用プロバイダで、読み込み中のシーンにあるSkinnedMeshRendererの`sharedMesh`を調べます。名前の一部で検索でき、大文字小文字は区別しません。空白を含む名前は`bs:"happy smile"`と入力します。検索結果には該当GameObjectと一致したブレンドシェイプ名を表示し、EnterまたはダブルクリックでGameObjectを選択します。メッシュが未設定のRendererや、読み込まれていないシーン・Prefabアセットは対象外です。

Unity標準のHierarchy検索にも`bs:`フィルターを追加しています。`h:t:SkinnedMeshRenderer bs:smile`と入力すると、コンポーネント種別とブレンドシェイプ名を組み合わせて絞り込めます。空白を含む名前は`h:t:SkinnedMeshRenderer bs:"happy face"`と入力します。こちらの結果表示と選択操作はUnity標準のHierarchy検索に従います。

### Transform MirrorのUI

0.1.4で、[Fluent 2のトークン設計](https://fluent2.microsoft.design/design-tokens)を基にしたUI Toolkitの共通スタイルを試験導入しています。Unityのライト／ダークに合わせて色を切り替えます。対象一覧へGameObjectやPrefabをドロップして、設定後に「ミラーを作成」を押します。「一覧をクリア」は一覧だけを空にし、シーンやアセットは削除しません。

狭いウィンドウでは設定部分がスクロールし、実行ボタンは下部に表示されます。カスタム基準点を使わないときは座標欄を無効にし、ワールド原点を基準にします。対象と各オプションはスクリプトの再読み込みでも保持します。

対象を追加すると「これから作る対称化ペア」に、左側へ作成元／現在の参照、右側へ作成予定先／対称化後の参照を表示します。「選択中を追加」「選択行を外す」「候補を更新」も利用できます。親と子を同時に追加しても子を二重複製しません。既存の対称オブジェクトは上書きせず、同名なら作成名に `_mirror_1` などを付けます。

- 名前は末尾の `_L ↔ _R`、`.L ↔ .R` を交換します。小文字および `Hand_L.001 → Hand_R.001` にも対応します。左右名がない複製ルートには従来の `_Mirrored` を付け、左右名がない子の名前は維持します。
- 探索範囲は対象ごとの最寄りのPrefabインスタンスルートです。Prefab以外はHierarchyルート、Prefabアセットはそのルートを使い、非アクティブを含めてDFSで探索します。左右名を交換した階層パスを優先し、見つからなければルート内の一意な左右名を使います。複数候補・範囲外・対応コンポーネント不足は状態を表示し、元の参照を維持します。
- 子階層の位置・回転をワールドX方向に反転し、親にも一意な対称候補があればその親の下に作成します。探索ルートと反転平面は別で、反転平面は従来どおりワールド原点または指定した基準点です。
- Unityの6種類のConstraintとVRC ConstraintのSource／Up／Target参照を対応付け、位置・回転オフセットも対称化します。今回作るオブジェクト間の参照を優先します。VRCの複製側は評価を止め、配置→参照置換→オフセット設定→ロック・有効状態復元の順に処理し、最後にSDKへ設定変更を通知します。
- PhysBoneはRoot／Ignore Transforms／Collidersの参照、Endpoint Position、Limit Rotationを処理します。PhysBone ColliderはRoot参照、Position、Rotationを処理し、半径・高さ・形状などは引き継ぎます。標準のBox／Sphere／Capsule／CharacterController／Wheel Colliderは中心位置を反転し、寸法などを維持します。
- シーン内の複製元の設定・Prefabオーバーライド・BlendShapeウェイトを引き継ぎます。ウェイトの引き継ぎを外すとPrefabの元の値、非Prefabなら0を使います。1回のUndoで作成分全体を取り消せます。

VRChat SDKは必須依存にせず、導入されている場合に対応します。複製範囲外の `TargetTransform` を駆動するVRC Constraintは既存オブジェクトを動かさないよう複製側を無効化し、警告を表示します。MeshColliderの頂点形状・描画メッシュ・AnimationClipのパスやアニメーション値は反転しません。回転の反転を外した場合は形状全体の完全な鏡像にならない場合があります。

共通スタイルは`Editor/ui`にあります。独自のEditorWindowへ適用する場合は、`CreateGUI()`から`EditorUiTheme.Apply(rootVisualElement)`を呼びます。開いたままのテーマ変更には`EditorUiTheme.RefreshTheme(rootVisualElement)`を`OnInspectorUpdate()`から呼びます。クラス`d9_ui_root`の配下だけに作用し、共通フォント設定を引き継ぎます。現在の適用先はTransform Mirrorです。

アセット複製は既存のコピーを上書きせず、別名で作成します。参照置換はUnityのYAMLテキスト形式が対象です。選択範囲の外にある依存アセットは追加コピーせず、元の参照を維持します。バイナリ形式のアセット内の参照置換には対応しません。

## liltoonマテリアルセットアップ

Unity 2022.3用のEditor拡張です。FBXのMaterialsからExtractした既存 `.mat` をそのまま更新し、モデルやPrefabからの参照を維持します。lilToonは別途導入してください。Editor Core自体にはlilToonの必須依存を追加していません。

1. `D9speed/Tools/liltoonマテリアルセットアップ` を開きます。
2. Assets内のマテリアルフォルダとテクスチャフォルダを指定します。サブフォルダも検索します。
3. 必要なら雛形のlilToonマテリアル、ノーマルの形式、AOの割り当て方法を指定します。
4. 「割り当てを確認 / 更新」で対応と警告を確認し、対象行を選んで「一括セットアップ」を押します。

`jacket.mat` に対して `jacket_BaseColor.png` などを検索します。大文字・小文字を区別せず、マテリアルのファイル名と用途の末尾名を完全一致で照合します。部分一致や連番の除去は行いません。同じ用途の画像が複数ある場合、そのマテリアル全体を対象外にします。FBX内蔵のマテリアルやPackages内のアセットは変更しません。

| Painterでの末尾名 | lilToonの割り当て先 | インポート設定 |
|---|---|---|
| `_BaseColor` | メインカラー | sRGBオン |
| `_NormalMap` | ノーマルマップ | Normal map、sRGBオフ |
| `_MatCapMask` | MatCapのマスク | sRGBオフ |
| `_Emission` | 発光テクスチャ | sRGBオン |
| `_AOMap` | 下記の選択方式 | sRGBオフ |
| `_Roughness` | 反転してSmoothnessに設定 | 元・生成画像ともsRGBオフ |
| `_MatCap2` | MatCap 2ndのマスク | sRGBオフ |
| `_NormalMap2` | ノーマルマップ2nd（任意） | Normal map、sRGBオフ |
| `_NormalMap2Mask` | ノーマルマップ2ndの強度マスク | sRGBオフ |
| `_RimLightMask` | リムライトの色 / マスク | sRGBオフ |

Painter側はすべて `$textureSet_用途名` で書き出してください。例えば `textureSet_NormalMap2Mask` と固定文字列になっている場合は、先頭の `$` を補ってください。ノーマルは既定でOpenGLです。DirectXで書き出した場合は画面で選ぶと緑チャンネル反転を設定します。BaseColorにアルファがあっても透過方式は自動判定しません。

雛形ありではそのシェーダーと全プロパティをコピーしてから画像を割り当てます。雛形自身は対象から除外します。雛形なしでは既存の通常版lilToon系シェーダーと設定を維持し、Liteを含むその他のマテリアルは不透明の `lilToon` へ変更してメイン色・テクスチャ・UVスケール／オフセットを引き継ぎます。見つからないマップは雛形または既存の設定を維持します。

AOは「影設定のAO Map」（既定）、「メインカラー2ndに乗算」、「D9speed AO拡張」、「割り当てずsRGBのみオフ」から選べます。標準のAO Mapは影の付きやすさを制御し、RGBが影色1・2・3に対応します。メインカラー2ndへの乗算を選んだ際、別のテクスチャでそのレイヤーが使用中なら適用を止めます。D9speed AO拡張は対応シェーダーの既存材質または雛形が必要で、UV0・Rチャンネルで設定します。MatCapマスクからMatCap本体は生成しません。本体が未設定ならマスクだけを割り当て、機能を自動でオンにせず警告します。NormalMap2Maskのみの場合も同様です。

Roughnessの変換はPNG / JPEG対応です。元画像のRを `1 - Roughness` で反転し、テクスチャフォルダの `_liltoon_generated` に別のPNGを作ります。元画像のピクセルは変更せず、インポート時の圧縮・縮小・ガンマ変換より前のデータから変換します。生成済み画像は再実行時に同じGUIDで更新します。Smoothnessの強度などは雛形または既存の設定に従います。

適用前の `.mat` とテクスチャのインポート設定は `Library/d9_liltoon_material_setup` にバックアップします。「前回の設定を復元」で戻せますが、適用後に手動で加えた編集も戻るため、復元前に表示を確認してください。失敗時はその実行分を自動復元します。新規生成した画像は復元時に削除し、空になった生成フォルダは残します。Unityの通常のUndoとは別の復元機能です。Libraryを削除するとバックアップも失われます。

インポート設定の変更は同じ画像を参照するほかのマテリアルにも反映されます。変更対象は対応が見つかり、選択したマテリアルで利用する画像だけです。

## 説明画像をSpriteで作成

Unity 2022.3 / Windows用のEditor専用ツールです。画像生成にはWindowsのシステムフォントを使います。

1. `D9speed/Tools/説明画像をSpriteで作成` を開きます。
2. Windowsに入っているフォントを選び、文章を入力します。「自動」ではメイリオを優先し、文章量に合わせて文字サイズを調整します。
3. プレビューを確認して「Sprite を保存…」を押し、Assets内の保存先を選びます。
4. 保存されたSpriteをシーンへ配置できます。必要に応じて配置したGameObjectに`EditorOnly`タグを付けます。

出力はSpriteとしてインポートされた `.png` 1点です。マテリアル・Quad・プレハブは作成しません。既存のアセットは上書きせず、同名の場合は連番を付けます。Built-in Render PipelineのSpriteRendererは既定マテリアルでライトの影響を受けません。

日本語の描画にはWindowsのシステムフォントを使います。フォント欄にはインストール済みの一覧が表示されます。「自動」でメイリオがない場合は既定のシステムフォントに切り替わります。描画結果をプレビューで確認してください。フォントファイルは配布物に含めません。出力したSpriteはフォントや専用スクリプトを参照しません。

- PNGは1024×512 px、白背景・濃い文字・40 px余白です。自動折り返し・改行に対応し、文字サイズを12〜48 pxで自動調整します。リッチテキストは解釈しません。
- 最小サイズでも文字が収まらない場合は警告し、保存時に確認します。フォント不足時の代替字形はプレビューで確認してください。
- 変更から100 ms後に描画します。Editor CoreはDLLから受け取ったPNGをプレビューに表示し、ウィンドウ終了時にテクスチャを解放します。
- 共通UIのライト／ダーク・フォント設定を引き継ぎます。文章と選択フォントはスクリプトの再コンパイル時に保持しますが、文書やプリセットの永続保存はありません。
- Sprite設定はSingle・Full Rect・512 Pixels Per Unit・無圧縮・sRGB・Mip Mapなし・Clampです。1024×512 pxの画像は2×1 Unity単位になります。透過PNGは対象外です。

## 設定

`D9speed > Settings`または`Edit > Preferences > D9speed Tools`で設定できます。

- 共通フォントの指定・解除。未指定時はUnity標準フォント。
- Prefab変更マークの表示切り替え。

設定は利用者のローカルEditorPrefsへ保存します。フォントの既存キーを維持しているため、更新後も設定を引き継ぎます。未公開のカメラ拡張、HTTP制御、Discord通知・認証情報はCoreに含めません。

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMへリポジトリを追加します。

VCC/ALCOMで各ツールを導入すると、本パッケージも依存パッケージとして導入されます。

既存のEditorToolや`D9speed_UnityUtility.cs`がある場合は、バックアップしてから重複する機能を整理してください。旧UtilityとCore 0.1.2を同時に有効にするとメニューが重複します。個人用通知機能が同じファイルにある場合は、その部分を分離して残せます。旧`settingProvider.cs`の共通設定はCore側へまとめ、未公開ツールの設定は各ツール側へ分離してください。元ファイルを自動削除する`legacyFolders`・`legacyFiles`は設定していません。

## ライセンス・連絡先

独自部分はMIT Licenseです。詳細は同梱の`LICENSE.md`を参照してください。

アセット複製は[Narazaka氏のCopyAssetsWithDependency](https://gist.github.com/Narazaka/1ae51c8515e55ca3dbeec5a3eba313ed)（k7a氏の実装から派生）を改修しています。当該部分のzlib形式のライセンス表記と改修の明記を`Editor/copy_assets_with_dependency.cs`に保持しています。

連絡先: d09pseed@gmail.com
