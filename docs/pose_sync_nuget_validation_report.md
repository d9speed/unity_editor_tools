# Unity Blender Pose Sync 0.1.1 検証記録

2026-09-20、Windows / Unity 2022.3.22f1 / Blender 4.5.5 LTSで確認しました。利用者のアバター・Prefabには触れず、専用プロジェクトと合成ボーンを使用しています。

## 今回の変更

- Setupのメニューを`D9speed / Animations / PoseSync Setup`に統一。元の緑チェックと進捗ボックスを継承。
- 独自のMessagePackエンコーダー・デコーダーを削除。公式NuGet版3.1.9とSource Generatorを使用。
- NuGetForUnity 4.5.0とMessagePack.Unity 3.1.9はUnity Package Managerで導入。MessagePackと推移的依存はNuGetForUnityで解決。
- Setupを独立したEditorアセンブリに分離。依存導入前も画面を開けるようにし、準備完了後にRuntime・Manager・任意NDMF連携を有効化。
- 既存MessagePackに異なる版がある場合は案内を表示し、自動では置き換えない。

## 確認結果

| 確認 | 結果 |
|---|---|
| NuGetもMessagePackもない新規プロジェクトからSetupを実行 | 成功。コンパイルをまたいで全工程を継続 |
| メニューと完了表示 | 指定のメニューから開き、3つの緑チェック・4つの緑ボックス・閉じる表示を確認 |
| Unity Edit Modeの5項目 | 成功。RuntimeとEditorの分離、メニュー、初期設定、送信データ、コマンド検証 |
| 通信DTOのSource Generator | 全14型の生成Formatterを取得できることを確認 |
| Blenderの通信形式・適用9項目 | 成功。v2 / v3、日本語、整数・小数、null、長い配列などを検証 |
| Unity Play Mode → Blenderの実TCP通信7項目 | 成功。回転・カメラ・停止通知・Rest復元を確認 |
| Play Mode終了後の一時Manager削除 | 成功 |
| Windows x64 / Monoビルド | 成功。公式MessagePackのRuntime DLLを含めてビルド |
| ビルド済みプレイヤー → Blenderの実TCP通信7項目 | 成功。回転・カメラ・停止通知・Rest復元を確認 |
| SDK 3.10.5 + NDMF 1.13.1との連携 | 成功。任意アセンブリの有効化、PhysBoneのIgnore、リネーム前の名前保持を確認 |
| 配布ZIP | 全44ファイルが配布元と一致。外部DLLとPythonキャッシュを含まない |

新規プロジェクトで解決されたNuGet依存は、MessagePack / Annotations / Analyzer 3.1.9、Microsoft.NET.StringTools 17.11.4、System.Collections.Immutable 8.0.0、System.Runtime.CompilerServices.Unsafe 6.0.0です。この一覧をセットアップにハードコードせず、NuGetForUnityの依存解決を使用しています。

## 再実行

1. 新規の専用Unityプロジェクトにパッケージと`tools/pose_sync_setup_checks.cs`を導入します。チェック用ファイルは`Assets/Editor`へ配置します。
2. Unityを`-batchmode -executeMethod D9speed.PackageValidation.PoseSyncSetupChecks.Run`で起動します。非同期導入を待つため`-quit`は付けません。結果は`Logs/pose_sync_setup_results.json`です。
3. 導入完了後に`tools/pose_sync_validation_project`の内容を`Assets`へコピーします。通信・Play Mode・プレイヤーの確認手順は[0.1.0の記録](pose_sync_validation_report.md#再実行)と共通です。

## 検証範囲

IL2CPPモジュールはこの環境にないため、IL2CPPでのビルドは未検証です。Source Generatorの生成を確認しただけでIL2CPP対応を検証済みとはしていません。WebGL・モバイル・macOS / Linux・Unity 6・個々の実アバターは今回の対象外です。VCC / ALCOMの画面操作による導入確認も含みません。

## 参照先

- [MessagePack-CSharpのUnity対応](https://github.com/MessagePack-CSharp/MessagePack-CSharp#unity-support)
- [MessagePack-CSharp v3.1.9](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v3.1.9)
- [NuGetForUnity v4.5.0](https://github.com/GlitchEnzo/NuGetForUnity/tree/v4.5.0)
