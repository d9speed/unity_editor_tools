# Fluent 2 / UI Toolkit 適合性評価

2026-09-21。以下はEditor Core 0.1.3時点の公開VPMの11パッケージとUnity 2022.3.22f1を対象とする評価です。その後、0.1.4でTransform Mirrorへ共通スタイルを試験導入しました。実装と表示確認の結果は[初回導入の検証記録](transform_mirror_fluent_validation_report.md)を参照してください。

## 判定

**採用を推奨します。Fluent 2のトークン設計をEditor Coreの共通USSとUI部品へ取り入れ、既存のUI Toolkit画面へ順次適用する方針が妥当です。** Unity 2022.3でもEditor拡張にはUI Toolkitが推奨されています。[Unity公式比較表](https://docs.unity3d.com/2022.3/Documentation/Manual/UI-system-compare.html)

Fluentの公式実装はReact、Web Components、各ネイティブ環境向けです。今回必要なのはUnityのVisualElement向けの実装です。React部品やWeb用CSSをそのまま取り込む構成にはしません。[Fluent開発環境](https://fluent2.microsoft.design/get-started/develop)

## 現状のUI

主要な独自画面・Overlay・Inspector・設定画面を分類しました。右クリックの単独コマンド、Unity標準Inspector、SceneView上のHandles描画は画面数に含めていません。

| パッケージ / 画面 | 実装 | 根拠となるファイル（packages配下） |
|---|---|---|
| Editor Core / Transform Mirror | UI Toolkit | `io.github.d9speed.editor_core/Editor/transform_mirror_window.cs` / CreateGUI |
| Editor Core / 共通設定 | IMGUI | `io.github.d9speed.editor_core/Editor/editor_ui_preferences.cs` / guiHandler |
| Scene Tools / Package・Prefab情報 | UI Toolkit | `io.github.d9speed.scene_tools/Editor/scene_package_info_overlay.cs` / CreatePanelContent |
| Scene Tools / Display Child Names設定 | 混在 | `io.github.d9speed.scene_tools/Editor/DisplayChildNamesInScene.cs` / IMGUIContainer |
| Humanoid Alias Copy | UI Toolkit | `io.github.d9speed.humanoid_alias_copy/Editor/HumanoidAliasComponentCopierWindow.cs` / rootVisualElementへ構築 |
| Package Exporter / 本体 | UI Toolkit | `io.github.d9speed.package_exporter/Editor/PackageExporter.cs` / CreateGUI |
| Package Exporter / 比較・検証画面 | IMGUI | `io.github.d9speed.package_exporter/Editor/PackageComponentReportCompareWindow.cs` / OnGUI |
| Rename Tool | UI Toolkit | `io.github.d9speed.rename_tool/Editor/RenameTool.cs` / CreateGUI |
| Animation / Animator Playback Preview | UI Toolkit | `io.github.d9speed.animation_tools/Editor/AnimatorPlaybackPreviewWindow.cs` / CreateGUI |
| Animation / Humanoid Random Hand Pose | UI Toolkit | `io.github.d9speed.animation_tools/Editor/HumanoidRandomHandPoseWindow.cs` / CreateGUI |
| Skinned Mesh Tools | 混在 | `io.github.d9speed.skinned_mesh_tools/Editor/SkinnedMeshRendererEditorExtra.cs` / CreateInspectorGUI内に標準InspectorのIMGUIContainer |
| Prefab Color Variants | UI Toolkit | `io.github.d9speed.prefab_color_variants/Editor/PrefabColorVariantMaker.cs` / CreateGUI |
| Cloth Fitting / PhysBone Collider生成 | UI Toolkit | `io.github.d9speed.cloth_fitting_tools/Editor/PhysBoneWeightColliderGenerator.cs` / CreateGUI |
| Pose Sync / Setup | UI Toolkit | `io.github.d9speed.unity_blender_pose_sync/Editor/Setup/pose_sync_setup_window.cs` / CreateGUI |
| Pose Sync / Manager | UI Toolkit | `io.github.d9speed.unity_blender_pose_sync/Editor/PoseSyncManagerWindow.cs` / CreateGUI |
| Pose Sync / Snapshot Sender | UI Toolkit | `io.github.d9speed.unity_blender_pose_sync/Editor/PoseSnapshotSenderWindow.cs` / CreateGUI |
| NVENC GPU Recorder | IMGUI | `io.github.d9speed.nvenc_gpu_recorder/Editor/gpu_recorder_window.cs` / OnGUI |

この範囲ではUI Toolkitが12画面、IMGUIが3画面、混在が2画面です。UXMLはなく、USSはPackage Exporterの1ファイルだけです。C#から構築していてもUI Toolkitであり、UXML化は必須ではありません。

多くの画面で`element.style`へ色・余白・サイズを直接指定しています。共通化済みなのは主に`D9speedEditorFontUtility`のフォント設定で、トークンや共通コントロールの仕組みはまだありません。インラインスタイルはUSSより優先されるため、テーマの追加と同時に固定の装飾値をUSSへ移す必要があります。[Unityの優先順位](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-uss-selector-precedence.html)

## 取り入れるものと制約

Fluent 2では値そのものを持つGlobal tokenと、用途を表すAlias tokenを分けます。この考え方は背景・文字・境界線・強調色・成功/警告/エラー、余白、角丸、文字サイズの共通化に適しています。[Fluent Design tokens](https://fluent2.microsoft.design/design-tokens)

| 対象 | Unity 2022.3での方針 |
|---|---|
| 色・余白・角丸・線幅 | USS変数へ対応付ける。用途別の意味を持つ変数を画面から使う |
| ライト / ダーク | EditorGUIUtility.isProSkinに応じて画面ルートのテーマを選ぶ |
| ボタン・入力欄・Foldout・一覧 | Unity標準部品を利用し、D9speedの画面内へ限定したUSSクラスで装飾 |
| hover / focus / disabled / selected | 状態ごとの色・枠を定義。キーボード操作とフォーカスの可視性を確認 |
| 短い色・透明度の遷移 | USS transitionsで可能。動きが不要な操作や重い一覧には増やさない |
| Fluentの影・背景ぼかし | Webのbox-shadow / backdrop-filterをUSSへ直接移植できない。まず背景色と境界線で階層を表す |
| 日本語フォント | 既存のフォント指定とUnity標準へのフォールバックを維持。文字欠けと表示密度を確認 |
| 標準Inspector・ネイティブメニュー | 独自UIの適用範囲から分離。Unity全体の完全な外観統一は対象外 |

USSは変数と`var()`を使えますが、変数への算術演算や他の関数内での`var()`には制限があります。複合トークンはUnityが扱える個別値へ展開します。[USS変数](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-USS-CustomProperties.html)

Unity 2022.3の対応プロパティを基準にし、Unity 6で追加されたAPIを前提にしません。文字の影は対応していますが、一般的な要素の影や背景ぼかしとは別です。[対応プロパティ](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-USS-Properties-Reference.html)、[遷移](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-Transitions.html)

Fluentの配色を使うだけでアクセシビリティ全体が保証されるわけではありません。色だけに依存しない状態表示、Tab移動、コントラスト、文字拡大・狭いドックでの読みやすさはUnity側で確認します。Pose Syncの完了を示す緑のチェックは意味と表示を保ちます。

## 推奨する共通基盤

評価時点では、Editor CoreのEditor配下へ、次の役割を分けて追加する案を提案しました。0.1.4ではこの構成で初回実装しています。

- `ui/design_tokens.uss`: 基本値と用途別の変数。例は`--d9_color_surface`、`--d9_color_text`、`--d9_color_success`、`--d9_space_m`。
- `ui/theme_dark.uss` / `ui/theme_light.uss`: 色の切り替え。
- `ui/controls.uss`: 見出し、セクション、主要/通常ボタン、入力列、状態表示、一覧の見た目。
- `ui/editor_ui_theme.cs`: 各画面のルートへスタイルを適用し、既存の共通フォント指定を反映。

スタイルは`d9_ui_root`などのルート配下に限定し、Unity本体やVRChat SDKの画面へ波及させません。選択・ドラッグ＆ドロップ・ObjectField・Undo・シリアライズなどの操作は既存機能を維持します。動的な寸法や表示制御はC#に残し、固定の装飾をUSSへ移します。

当面はUSSをトークンの管理元にすれば十分です。IMGUIの見た目も一時的に揃える必要が出た場合のみ、同じ定義からUSSとC#を生成する方式を検討します。色の定義を2か所で手修正する構成は避けます。

**依存関係に注意:** 現在、Pose SyncとNVENCはEditor Coreへ依存していません。共通UIを使う版でVPM依存を追加し、利用するEditor asmdefへ`D9speed.EditorUtils`参照を追加します。Pose SyncのSetup asmdefはMessagePack導入前にも動く独立構成を保ち、RuntimeへEditor Core参照を入れません。既にCoreへ依存するパッケージも、新しいUI APIを使う版ではCoreの最低バージョンを引き上げます。

## 移行順序

1. Editor Core内のTransform Mirrorで試作する。小さな既存UI Toolkit画面で色・余白・入力・ボタン・一覧の基準を決める。
2. 共通設定をUI Toolkitへ移行し、ライト/ダーク・日本語・カスタムフォントを確認する。設定キーを維持する。
3. Pose Sync Setupへ適用する。未導入・進行中・成功・失敗・再試行と緑のチェックを確認する。
4. 他の既存UI Toolkit画面へ段階的に適用する。Package Exporterの既存USSとインライン指定も整理する。
5. NVENC、比較画面、Display Child Namesの設定部分を個別にUI Toolkitへ移行する。録画処理・レポート計算・SceneViewのラベル描画は別の処理として維持する。Skinned Mesh ToolsはUnity標準Inspectorを包む箇所を残せる。

最初の完了条件は、1画面のライト/ダーク、狭いドック、高DPI、日本語・カスタムフォント、Tab操作、無効状態・エラー表示、再オープンとドメインリロードの確認です。USSの取り込みはUnity 2022.3で検証し、外観は実画面で確認します。その基準が固まってから全ツールへ展開するのが適切です。
