# D9speed Editor Core

D9speedのEditor拡張が共有する補助処理と、普段使いの右クリックメニューです。オブジェクト検索、ヒューマノイド対応付け、階層パス、コンポーネントコピー、ファイル名の処理に加えて、Unity Utilityの各機能を収録します。

- 対応基準: Unity 2022.3.22f1。
- Editor専用。VRChat SDK、外部DLL、専用Runtimeは不要です。
- アセンブリ名: `D9speed.EditorUtils`。
- 名前空間: `D9speed_BaseEditorUtils`。

説明板プレハブ作成は任意のTextMeshPro連携アセンブリ `D9speed.EditorUtils.GuideBoard` を使用します。作成時はTextMeshPro 3.0.xとTMP Essential Resourcesが必要です。TMP未導入でもCoreの既存機能は利用できます。

`FindSceneObjects<T>()`は有効なシーンオブジェクトを取得します。`true`を渡すと非アクティブも含めます。`GetObjectId()`はEditorセッション内の識別子を返します。

## Unity Utility

| 操作場所 | 機能 |
|---|---|
| Projectの右クリック → `CopyFullPath` | ファイル・フォルダのフルパスをコピー。複数選択とPackages内の実体パスに対応 |
| Projectの右クリック → `CopyAssetsWithDependency` | 選択したアセット群／フォルダを複製し、複製対象どうしの参照をコピー先へ変更 |
| Hierarchyの右クリック → `Copy Animation Property Path` | 最も近いAnimatorを基準にしたパスをコピー。なければPrefabルート、シーンルートを使用 |
| Hierarchyの右クリック → `選択オブジェクト名をコピー` | 選択したオブジェクト名を `Body,Skirt,Glasses` のようにカンマ区切りでコピー。親子の同時選択・非アクティブ・同名オブジェクトにも対応 |
| Hierarchyの右クリック → `このヒエラルキー配下を検索` | 選択階層のパスをUnity Searchで検索 |
| Componentの右クリック → `コンポーネント名をコピー` | コンポーネントの型名をコピー |
| Componentの右クリック → `ここから下のコンポーネントをコピー` | 選択コンポーネント以降をまとめてコピー |
| Component／Hierarchyの右クリック → `コピーしたコンポーネントを新規貼り付け` | シーンオブジェクトへ追加。Undo対応 |
| Hierarchyの右クリック → メインカメラ関連の2項目 | 対象へカメラを向ける／対象の+Z方向から正対。Undo対応 |
| `D9speed > Transform Mirror Tool` | X軸方向のミラー複製。左右名・Constraint／PhysBone／Colliderの対称化と作成ペアの事前表示 |
| `D9speed > Tools > 説明画像つき板ポリプレハブ作成(EditorOnly)` | 文章を画像にし、EditorOnlyのQuadプレハブとPNG・Unlitマテリアルを保存 |
| `Alt + R` | 選択したシーンオブジェクトのローカルTransformをリセット。Undo対応 |
| `Ctrl + L`（macOSは`Cmd + L`） | Inspectorのロック／解除を切り替え |
| Hierarchy | コンポーネントに変更があるPrefabインスタンスのルートへ変更マークを表示 |

Transform Resetの割り当てはUnityのShortcutsにある`Custom/ShortCutEX/TransformReset`で変更できます。

Inspectorのロックは `Edit > Shortcuts` の `Custom/ShortCutEX/InspectorLock` で割り当てを変更できます。初期設定はCtrl+L（macOSはCmd+L）です。同じキーを使う別の操作がある場合は、Shortcutsで割り当てを調整してください。複数のInspectorがある場合は、フォーカス中のInspector、マウス下のInspector、開いているInspectorの先頭の順に1つだけ切り替えます。HierarchyやSceneからも操作でき、Inspectorが開いていない場合は何もしません。

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

## 説明画像つき板ポリプレハブ

Unity 2022.3 / Built-in Render Pipeline用のEditor専用ツールです。TextMeshPro 3.0.xをPackage Managerから導入し、初回は `Window > TextMeshPro > Import TMP Essential Resources` を実行してください。

1. `D9speed/Tools/説明画像つき板ポリプレハブ作成(EditorOnly)` を開きます。
2. TMPフォントを選び、文章・文字サイズ・余白・揃え方・文字色・背景色を調整します。
3. プレビューを確認して「プレハブを保存…」を押し、Assets内の保存先を選びます。
4. 作成したプレハブを必要な場所に配置します。

同じフォルダーへ `.prefab`、`_image.png`、`_material.mat` を保存します。既存のアセットは上書きせず、同名の場合は連番を付けます。プレハブは `EditorOnly` タグ付き、ColliderなしのQuadで、高さ1 m・幅は画像の縦横比に合わせます。Unity標準のQuadと `Unlit/Texture` を使うため、正面はローカル-Z側です。シーンへ自動配置はしません。ビルドではEditorOnlyの説明板が除外されます。PNG単体の保存も可能です。

日本語を含む場合は、対応する `.ttf` / `.otf` から作成したTMPフォントを選んでください。標準のLiberationSans SDFには日本語がありません。フォントの使用条件に従ってください。作成されたプレハブはPNGとマテリアルを参照し、TMP・フォント・専用スクリプトを参照しません。

- 解像度は各辺64〜2048 px。自動折り返し・改行・左／中央／右揃えに対応します。リッチテキストは解釈しません。
- 文字が枠に収まらない場合は警告し、保存時に確認します。フォントにない文字がある場合は保存を無効にします。
- 変更から100 ms後に描画し、同じ解像度のRenderTextureを再利用します。ウィンドウを閉じるとプレビュー用シーン・描画リソースを解放します。
- 共通UIのライト／ダーク・フォント設定を引き継ぎます。入力はスクリプトの再コンパイル時に保持しますが、文書やプリセットの永続保存はありません。
- Assets内のPNGは無圧縮・sRGB・Mip Mapなし・Clampで保存します。透過PNGとURP/HDRPは対象外です。

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
