# D9speed Unity Editor Tools

Package Exporter 0.1.2（比較画面を含む）とRename Tool 0.1.1に、Editor Core 0.1.5のFluent 2共通UIを適用しました。Rename Toolの画面は日本語です。[UI更新の検証記録](docs/export_rename_fluent_validation_report.md)を参照してください。

Unity Editor拡張を、機能ごとのVPMパッケージとして管理します。このリポジトリ自体はUnityプロジェクトではありません。

## 公開パッケージ

| パッケージ | 内容 |
|---|---|
| `io.github.d9speed.editor_core` | 各ツールに必要な共通ヘルパー |
| `io.github.d9speed.scene_tools` | Display Child Names、Package / Prefab Info |
| `io.github.d9speed.humanoid_alias_copy` | ボーン名の辞書を使ったコンポーネントコピー。辞書JSON同梱 |
| `io.github.d9speed.package_exporter` | unitypackageのバッチ書き出し、JSONプロファイルと情報出力・比較 |
| `io.github.d9speed.rename_tool` | アセット・Hierarchy・Animatorの名称の一括置換 |
| `io.github.d9speed.animation_tools` | Animator再生プレビュー、手指のランダムポーズ |
| `io.github.d9speed.skinned_mesh_tools` | SkinnedMeshRendererのInspector拡張 |
| `io.github.d9speed.prefab_color_variants` | マテリアルを置き換えたPrefab Variantの作成 |
| `io.github.d9speed.nvenc_gpu_recorder` | カメラ・RenderTextureのHEVC/MP4、透過ProRes/MOV・PNG録画。FFmpegは別途指定 |
| `io.github.d9speed.cloth_fitting_tools` | PhysBoneコライダー生成 |
| `io.github.d9speed.unity_blender_pose_sync` | UnityからBlenderへのポーズ・カメラ同期。RuntimeとBlenderアドオンを同梱 |

## 導入

[案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC/ALCOMにリポジトリを追加し、プロジェクト管理画面で使いたいツールを導入してください。Editor Coreを必要とするツールでは、依存パッケージとして自動導入されます。Package ExporterではUnity公式のNewtonsoft Json 3.2.1もUnity Package Manager経由で取得します。

登録用URL: `https://d9speed.github.io/Unity_Tools/index.json`

NVENC GPU RecorderはWindows x64向けです。HEVC録画はNVIDIA / Direct3D 11が必要で、透過ProResにも対応します。FFmpeg実行ファイルは同梱しません。詳細は[録画ツールの使い方](packages/io.github.d9speed.nvenc_gpu_recorder/README.md)を参照してください。Screen Texture Captureは公開対象から削除しました。既に導入済みの場合はVCC/ALCOMから個別に削除してください。

Cloth Fitting ToolsにはVRChat Avatars SDK 3.10.3以上・3.11.0未満が必要です。SDKは公式リポジトリから依存として導入されます。

Unity Blender Pose SyncはRuntimeとEditorを1パッケージで導入できます。Unityの`D9speed / Animations / PoseSync Setup`で「セットアップ」を押すと、NuGetForUnityと公式MessagePackを導入し、完了項目を緑のチェックで表示します。同じ画面からBlender用アドオンの場所を開き、Blender側へインストールしてください。詳細は[使い方](packages/io.github.d9speed.unity_blender_pose_sync/README.md)と[検証記録](docs/pose_sync_nuget_validation_report.md)を参照してください。

Animation Tools 0.1.1 / Pose Sync 0.1.2以降は、アニメーション系メニューを`D9speed / Animations`へ統一しています。Pose Syncのセットアップは、既存のMessagePack 3.1.7などから3.1.9への更新にも対応します。[更新検証](docs/pose_sync_upgrade_validation_report.md)を参照してください。

Unity 2022.3.22f1で検証しています。旧Assets版と同時に入れるとGUIDやアセンブリが重複するため、新規プロジェクトでの導入を推奨します。旧ファイルの自動削除は行いません。

## 作業フォルダ

- `packages/`: 各パッケージの配布対象。
- `tools/`: ローカルZIP作成・検証用処理。ZIPには入りません。
- `docs/`: 移行方針と検証記録。
- `artifacts/`: ZIPとハッシュ。Git対象外。

検証用Unityプロジェクトはリポジトリに含めません。Scene Toolsの初回検証は`scene_tools_validation`、追加3ツールとCore更新の検証は`tools_validation`で行います。

2026-09-20、Unity 2022.3.22f1の新規プロジェクトでZIPから導入し、7項目の自動確認が成功しました。詳細は[検証記録](docs/validation_report.md)を参照してください。

同日、公開URLからvrc-get 1.9.2でScene Toolsを指定して導入し、Editor Coreの自動導入と配布ファイルの一致も確認しました。

追加3ツールとEditor Core 0.1.1は、同じUnityバージョンで14項目の確認に成功しました。詳細は[追加ツールの検証記録](docs/tools_validation_report.md)を参照してください。

## ZIPの作成

PowerShellで`./tools/build_packages.ps1`を実行します。各ZIPの直下に`package.json`、`LICENSE.md`、`Editor`が入ります。既存の同名ZIPは上書きしません。出力先を変える場合は`-output_directory`、更新対象だけを作成する場合は`-package_ids`にIDの配列を指定します。更新しない公開済みバージョンを再作成しないでください。

0.1.0の公開用ZIPは`artifacts/release_0_1_0`に生成しました。今後の更新では、パッケージのバージョンとダウンロードURLを変更し、新しいタグ・ZIPを追加します。公開済みのZIPは置き換えず、旧バージョンも維持してください。

Windows版Unity Editor 2022.3.22f1で、SDK不要の9パッケージを導入して検証します。`tools/validation_project/Editor`のC#ファイルを専用の新規検証プロジェクトの`Assets/Editor`へコピーし、`D9speed.PackageValidation.PackageSmokeChecks.Run`をバッチ実行すると、プロジェクトの`Logs/package_smoke_results.json`へ結果を保存します。Cloth Fitting Toolsは、10パッケージとSDKを入れた別の検証プロジェクトで`tools/vrc_validation_project/Editor`を使い、`D9speed.PackageValidation.ClothToolsSmokeChecks.Run`を実行します。テストは検証用のシーンオブジェクトとアセット、書き出しファイルを作成するため、普段の作業プロジェクトでは実行しないでください。

追加5パッケージについて、SDKなし23項目・SDK入り4項目の確認が成功しました。詳細と確認範囲は[追加5パッケージの検証記録](docs/remaining_tools_validation_report.md)を参照してください。

## 検証範囲と今後の作業

NVENC GPU Recorder 0.1.0は専用の新規プロジェクトで7項目の確認に成功しました。HEVC動画・CPU/Vulkan ProRes動画・透過PNGについて、実ファイルのフレーム数・順序・色・透明度も確認しています。[検証記録](docs/nvenc_recorder_validation_report.md)を参照してください。

- SDK導入済み環境で任意連携を網羅的に確認。
- 公開URLからVCC/ALCOMで新規導入・将来のバージョン更新を確認。
- 既存Assets版からの移行手順を整備。

認証情報、個人設定、非公開ツールはこのリポジトリへ含めません。

## ライセンス・連絡先

[MIT License](LICENSE)。各パッケージにも同じライセンスを同梱しています。

不具合は[Issues](https://github.com/d9speed/unity_editor_tools/issues)、連絡は d09pseed@gmail.com へお願いします。
