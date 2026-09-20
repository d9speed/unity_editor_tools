# 初回パッケージの検証記録

検証日: 2026-09-20（日本時間）

Unity 2022.3.22f1の3Dテンプレートから新規プロジェクトを作成し、生成したZIPを`Packages`内に展開しました。VRChat SDKとnadena.dev系パッケージは未導入です。既存のCodexAutoMationプロジェクトは変更していません。

## 結果

コンパイル成功。`D9speed.PackageValidation.PackageSmokeChecks.Run`による以下の7項目がすべて成功し、Unityは終了コード0で終了しました。

| 確認 | 結果 |
|---|---|
| Editor CoreとScene Toolsが0.1.0として登録される | 成功 |
| 両アセンブリがEditor専用で、Playerのコンパイル対象に入らない | 成功 |
| 元の3つのC#ファイルのGUIDからパッケージ内のファイルが解決される | 成功 |
| 共通ヘルパーがアクティブ／非アクティブのオブジェクトを検索できる | 成功 |
| 両Scene Viewオーバーレイのパネルを生成できる | 成功 |
| SDK等の任意連携パッケージがなくてもバージョン情報を取得できる | 成功 |
| CPUから読み取り不可のMeshでも三角形数を取得できる | 成功 |

コピー元のC#・asmdef・metaの計8ファイルは、作業前のSHA-256と一致しました。コピー先も同じ内容です。配布対象24ファイルは、ZIPから展開してUnityに取り込んだ後も作業用リポジトリと一致しました。

## 初回検証時のZIP

| パッケージ | サイズ | SHA-256 |
|---|---:|---|
| `io.github.d9speed.editor_core-0.1.0.zip` | 4,271 bytes | `0c100c4e048b497436d2de0bd9c756cb9c950b81a05539ab3cc10b64b8cecc8f` |
| `io.github.d9speed.scene_tools-0.1.0.zip` | 33,968 bytes | `f10c5ac0f1983ffb6d03901f4cb9150d8c272f13d74df671fa9623e95b32540f` |

ZIP直下の`package.json`、メタファイルの有無・GUID重複、Editor専用のアセンブリ設定、ローカル依存パッケージの存在も確認しました。この初回ZIPはリポジトリ内の`artifacts`に保存しています。

## 公開用情報の確定

2026-09-20、作成者からALCOMへの登録と正常動作の確認報告がありました。登録方法の詳細は記録していないため、公開URL経由での新規導入確認とは分けて扱います。

同日、MITライセンスと公開用メールアドレス`d09pseed@gmail.com`を確定しました。LICENSE.mdとメタ情報、READMEを整え、ZIPを`artifacts/release_0_1_0`へ再生成しました。C#・asmdefと元のGUIDに変更はありません。

| 公開用ZIP | サイズ | SHA-256 |
|---|---:|---|
| `io.github.d9speed.editor_core-0.1.0.zip` | 5,276 bytes | `753eaa91cd18f0dcc0046543cf53d5e57ecf74dd0ad4242f36411d8306a5d8e4` |
| `io.github.d9speed.scene_tools-0.1.0.zip` | 35,088 bytes | `809d05b7643e4ebac2c5e931b25b236289e1adad85eb5cc938d7e3911dc60385` |

VPM一覧は公開用ZIPのマニフェストから生成し、各ZIPのSHA-256を付与しています。案内ページは390・768・1440px幅で横はみ出しがないこと、登録URLの一致、URLコピー処理の成功・失敗・応答待ちからの復帰を確認しました。クリップボード処理の分岐はブラウザー内でAPIを差し替えて確認しています。

## 公開URLからの導入確認

2026-09-20、GitHub Releasesへ2つのZIPを公開し、GitHub Pagesの案内ページと`index.json`がHTTP 200で取得できることを確認しました。公開ZIPを認証なしで取得し、一覧のSHA-256と一致しました。

さらに、[vrc-get](https://github.com/vrc-get/vrc-get) 1.9.2を使用し、公開リポジトリから`io.github.d9speed.scene_tools` 0.1.0のみを指定して新しい検証先へ導入しました。Editor Core 0.1.0も依存として自動導入され、インストール先の28ファイルすべてが配布元と一致しました。

検証クライアントはWSL Ubuntu上で`XDG_DATA_HOME`を専用フォルダに設定し、普段のALCOM/VCC設定とは独立して実行しています。ALCOMの画面操作を自動検証したものではありません。

## 確認範囲と残作業

これはパッケージ構造とUnityへの取り込み、初期化の自動確認です。VCC/ALCOMでの依存関係の自動解決、インターネット経由の導入・更新、Sceneビューの実描画・マウス操作、SDK連携、Unity 6対応を検証したものではありません。

[VPMの公式仕様](https://vcc.docs.vrchat.com/vpm/packages/#vpm-manifest-additions)に沿って著者メールアドレスをマニフェストに記載しています。

既存Assets版と同時に導入するとGUIDや型・アセンブリ名が重複するため、初回確認には専用の新規プロジェクトを使います。既存プロジェクト用の自動削除・移行設定は未設定です。

生の検証結果は検証プロジェクトの`Logs/package_smoke_results.json`、Unityログは親作業フォルダの`reports/scene_tools_validation.log`に保存しています。
