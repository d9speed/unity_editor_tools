# D9speed Editor Core

D9speedのEditor拡張が共有する補助処理と、普段使いの右クリックメニューです。オブジェクト検索、ヒューマノイド対応付け、階層パス、コンポーネントコピー、ファイル名の処理に加えて、Unity Utilityの各機能を収録します。

- 対応基準: Unity 2022.3.22f1。
- Editor専用。VRChat SDK、外部DLL、専用Runtimeは不要です。
- アセンブリ名: `D9speed.EditorUtils`。
- 名前空間: `D9speed_BaseEditorUtils`。

`FindSceneObjects<T>()`は有効なシーンオブジェクトを取得します。`true`を渡すと非アクティブも含めます。`GetObjectId()`はEditorセッション内の識別子を返します。

## Unity Utility

| 操作場所 | 機能 |
|---|---|
| Projectの右クリック → `CopyFullPath` | ファイル・フォルダのフルパスをコピー。複数選択とPackages内の実体パスに対応 |
| Projectの右クリック → `CopyAssetsWithDependency` | 選択したアセット群／フォルダを複製し、複製対象どうしの参照をコピー先へ変更 |
| Hierarchyの右クリック → `Copy Animation Property Path` | 最も近いAnimatorを基準にしたパスをコピー。なければPrefabルート、シーンルートを使用 |
| Hierarchyの右クリック → `このヒエラルキー配下を検索` | 選択階層のパスをUnity Searchで検索 |
| Componentの右クリック → `コンポーネント名をコピー` | コンポーネントの型名をコピー |
| Componentの右クリック → `ここから下のコンポーネントをコピー` | 選択コンポーネント以降をまとめてコピー |
| Component／Hierarchyの右クリック → `コピーしたコンポーネントを新規貼り付け` | シーンオブジェクトへ追加。Undo対応 |
| Hierarchyの右クリック → メインカメラ関連の2項目 | 対象へカメラを向ける／対象の+Z方向から正対。Undo対応 |
| `D9speed > Transform Mirror Tool` | X軸方向のミラー複製。基準点・回転・PrefabのBlendShapeウェイトを指定 |
| `Alt + R` | 選択したシーンオブジェクトのローカルTransformをリセット。Undo対応 |
| Hierarchy | コンポーネントに変更があるPrefabインスタンスのルートへ変更マークを表示 |

Transform Resetの割り当てはUnityのShortcutsにある`Custom/ShortCutEX/TransformReset`で変更できます。

アセット複製は既存のコピーを上書きせず、別名で作成します。参照置換はUnityのYAMLテキスト形式が対象です。選択範囲の外にある依存アセットは追加コピーせず、元の参照を維持します。バイナリ形式のアセット内の参照置換には対応しません。

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
