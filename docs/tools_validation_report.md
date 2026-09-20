# 追加3ツールとEditor Core更新の検証

検証日: 2026-09-20。Unity 2022.3.22f1の3Dテンプレートから作成した専用プロジェクトで確認しました。VRChat SDK、Modular Avatarは未導入です。

対象はEditor Core 0.1.1、Scene Tools 0.1.0、Humanoid Alias Copy 0.1.0、Package Exporter 0.1.0、Rename Tool 0.1.0です。Package Exporterの依存するUnity公式Newtonsoft Json 3.2.1も解決されました。

## Unityでの確認

コンパイルと14項目の自動確認がすべて成功し、Unityは終了コード0で終了しました。

| 確認 | 結果 |
|---|---|
| 5パッケージとNewtonsoft Jsonの登録・バージョン | 成功 |
| 5アセンブリがEditor専用でPlayerに入らない | 成功 |
| 既存Scene Tools・CoreのGUID解決 | 成功 |
| 非アクティブなオブジェクトを含む検索 | 成功 |
| Scene Toolsの2つのパネル生成 | 成功 |
| SDKなしでのパッケージ情報取得 | 成功 |
| CPUから読み取り不可のMeshの三角形数取得 | 成功 |
| 同梱辞書の54項目の読み込みとAlias CopyのUI初期化 | 成功 |
| 異なるボーン名へのConstraint・Colliderコピー、参照の置き換えとUndo | 成功 |
| Rename ToolのUI、文字列・大文字小文字・正規表現の置換 | 成功 |
| Hierarchyの選択対象だけをリネームしUndo | 成功 |
| ExporterのUIとパッケージ内スタイルの読み込み | 成功 |
| 日本語や追加文字列を含むJSON設定の保存と読み直し | 成功 |
| 検証用アセットのunitypackage書き出しと情報JSON生成 | 成功 |

JSON設定の再読み込みで既存リストに値が重複追加される問題を検出し、配布版の読み込み設定を修正しました。個人用Discord通知への依存と固定ドライブの初期パスも配布版から除いています。

## 配布構成

辞書はHumanoid Alias Copyの`Editor/Data/humanoid_bone_dictionary.json`に同梱し、インストール先から自動解決します。外部アドオンへの参照はありません。旧Assets版のコピー元22ファイルは記録したSHA-256と一致し、変更されていません。

新規3ツールはEditor Core `>=0.1.1 <0.2.0`に依存します。Scene Toolsは0.1.0をそのまま使用し、既存の配布ZIPを維持します。全パッケージでMITライセンス、メタファイル、GUID重複、Editor専用アセンブリ、配布URLと依存関係を検査しています。

公開用ZIPは`artifacts/tools_release_20260920`、サイズとSHA-256は同ディレクトリの`package_artifacts.json`に記録します。案内ページは既存のデザインに3項目を追加し、1200px・390px幅で横はみ出しがないことを確認しました。

## 公開URLからの導入・更新確認

GitHub Releasesへ4つのZIPを公開し、認証なしでダウンロードしたファイルのSHA-256が配布元と一致しました。公開VPM一覧のハッシュも一致し、GitHub Pagesへの反映を確認しました。

vrc-get 1.9.2をWSL上の独立した設定フォルダで実行し、Scene Tools 0.1.0とEditor Core 0.1.0がある検証プロジェクトに、追加3ツールを公開URLから導入しました。Editor Coreは依存関係により0.1.1へ自動更新され、Scene Tools 0.1.0は維持されました。導入後の5パッケージ、計88ファイルが作業用リポジトリと一致しました。

普段のALCOM/VCC設定は変更していません。これはVPMクライアント経由の導入・更新確認であり、ALCOMの画面操作を自動検証したものではありません。

## Package Exporter 0.1.1の追加確認

同日、Package Component Report Compareを同梱し、`D9speed > ExportBatch`の下に本体と比較ツールのメニューを配置しました。比較ツールは元ファイルのGUIDを維持し、変更はメニューの配置と表示順のみです。

Unity 2022.3.22f1で既存14項目に次の2項目を加え、16項目すべて成功しました。

- 両メニューからそれぞれのウィンドウを実際に開けること。同じパッケージのアセンブリに入り、元のGUIDで比較ツールを解決できること。
- 基準JSONと複数の比較JSONを読み込み、一致・欠落・追加・個数差・配置差を判別できること。差分のみ／配置文字列のフィルターが動き、入力JSONを変更しないこと。

比較用レコードは専用プロジェクトに生成したJSONです。ユーザーのPrefabやアバターは使用していません。公開用ZIPは`artifacts/package_exporter_0_1_1`、Unityログは親作業フォルダの`reports/exporter_0_1_1_validation.log`に保存します。

## 検証範囲

操作対象は専用プロジェクトに作った検証用オブジェクトとアセットです。利用者のアバターやPrefabは変更していません。SDK固有コンポーネントの全組み合わせ、各画面の手動操作、Unity 6、旧Assets版からの自動移行は未検証です。

検証処理は`tools/validation_project/Editor`にあります。生の結果は専用プロジェクトの`Logs/package_smoke_results.json`に保存します。
