# D9speed Scene Tools

UnityのSceneビューに、オブジェクト名・コンポーネント情報と、パッケージ・プレハブ情報を表示します。

## 収録ツール

### Display Child Names

- オブジェクト名とコンポーネントをSceneビューへ表示。
- カーソル付近の表示、カラム表示、ページ切替、ラベル固定、ボーン表示に対応。
- SkinnedMeshRendererで絞り込まず、未使用・補助ボーンを含む現在の編集ステージ内のTransform全体が対象です。非表示・非アクティブは除外します。
- 詳細設定の「ボーン階層（親子線）」で、Transformの親子を結ぶ線を切り替えます。スフィアとは個別にON／OFFできます。
- 2カラムのTransformアイコン付き名前ラベルを、インスペクターのTransform参照欄へ直接ドラッグできます。ドラッグ中は選択を維持し、クリックで対象を選択します。
- 最大深度の初期値は0（無制限）。更新前の保存値が残る場合は、最大深度を0に変更してください。スフィアサイズは最大1.0です。
- `Tools > Display Child Names > Toggle Enabled`で有効・無効を切り替えます。
- SceneビューのOverlaysメニューで`Display Child Names`の設定パネルを表示します。
- `Toggle Label Lock`と`Toggle Enabled`はUnityのShortcuts設定からキーを割り当てられます。

### Package / Prefab Info

- SceneビューのOverlaysメニューから`Package / Prefab Info`を表示します。
- 導入されている対応パッケージのバージョンを表示します。
- 選択プレハブのRenderer数、三角形換算のポリゴン数、ユニークマテリアル数を表示します。
- lilAvatarUtils導入済みのときだけ連携ボタンを表示します。

## 必要環境

- 対応基準: Unity 2022.3.22f1。
- 必須: `io.github.d9speed.editor_core` 0.1.x。
- VRChat SDK、lilToon、Modular Avatar、Poiyomi、lilAvatarUtilsは任意です。表示対象がない場合は該当表示が省略されます。
- Editor専用。専用Runtime、FFmpeg、外部DLLは不要です。

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMへリポジトリを追加し、プロジェクトのパッケージ管理画面で`D9speed Scene Tools`を導入します。必要な`D9speed Editor Core`も一緒に導入されます。

手動でローカル検証する場合は、新規プロジェクトの`Packages`へEditor CoreとScene Toolsの2フォルダを配置します。Unity Package ManagerからScene Toolsだけを直接追加しても`vpmDependencies`は解決されません。

既存のAssets版と併用する移行手順は未確定です。ソースの.metaを維持しているため、同一プロジェクトへの重複導入は避けてください。元ファイルの自動削除は設定していません。

Unity 2022.3.22f1でコンパイルと初期化を自動確認し、作成者がALCOMへの登録と動作を確認しています。Unity 6と各SDK連携の網羅的な確認は未実施です。

## ライセンス・連絡先

MIT License。詳細は同梱の`LICENSE.md`を参照してください。

連絡先: d09pseed@gmail.com
