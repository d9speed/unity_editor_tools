# Unity Blender Pose Sync 0.1.0 検証記録

2026-09-20、Windows / Unity 2022.3.22f1 / Blender 4.5.5 LTSで確認しました。利用者のアバターやPrefabを変更せず、専用プロジェクトの合成ボーン・カメラを使用しています。

## パッケージ構成

`io.github.d9speed.unity_blender_pose_sync`にRuntime、Editor、Blender受信アドオンを収録しています。RuntimeとEditorは別のasmdefです。MessagePack-CSharp、NuGetForUnity、Source Generator、プロジェクト共通Defineへの依存を取り除き、既存の通信形式を扱う専用実装に置き換えました。外部MessagePackライブラリは同梱しません。

NDMF連携は別のEditor専用asmdefへ移し、NDMF 1.11以上・2.0未満が存在する場合のみ有効にします。NDMFのAnimatorServicesContextを必要な拡張として宣言します。通常のManager利用では、アバターに送信コンポーネントを保存しません。

## 実施した検証

| 確認 | 結果 |
|---|---|
| 新規のSDKなしプロジェクトに本パッケージだけを追加 | コンパイル成功。追加DLL・Define設定なし |
| Unity Edit Modeの5項目 | 成功。Runtimeのビルド対象、3メニュー、設定分離、合成ボーン・カメラ、受信コマンド検証 |
| Blenderの通信形式・適用9項目 | 成功。日本語、v2 / v3、64bit整数、float / double、null、長い文字列・配列・バイナリ、回転・カメラ適用 |
| Unity Play Mode → Blenderの実TCP接続 | 成功。30度の回転とカメラ位置を受信、停止通知・Rest復元を確認 |
| Play Mode終了後の一時Manager削除 | 成功 |
| Windows x64 / Monoのプレイヤービルド | 成功。Editorアセンブリをプレイヤーへ含めないことを確認 |
| ビルド済みプレイヤー → Blenderの実TCP接続 | 成功。回転・カメラ・停止通知・Rest復元を確認 |
| SDK 3.10.5 + NDMF 1.13.1との共存 | 成功。任意連携が有効になり、PhysBoneのIgnoreとリネーム前の名前を保持 |

Blenderの検証ではPythonのmsgpackを使わず、同梱デコーダーで受信しています。停止通知後に古い待機フレームが適用される競合を見つけ、停止時の破棄と再登録防止を追加して再検証しました。

## 再実行

Unity用の処理は`tools/pose_sync_validation_project`、Blender用は`tools/validate_pose_sync_blender.py`、NDMF用は`tools/pose_sync_ndmf_checks.cs`です。検証用アセットとビルドを生成するため、普段の作業プロジェクトでは実行しないでください。

1. 専用Unityプロジェクトへパッケージを導入し、`tools/pose_sync_validation_project`の内容を`Assets`へコピーします。
2. Python検証処理に`--prepare --addon <受信アドオン> --output <Unityプロジェクト>/Logs/pose_sync`を渡し、Blender側コマンドのテストデータを作ります。
3. Unityをバッチ起動して`D9speed.PackageValidation.PoseSyncChecks.Run`を実行します。
4. Blenderを`--background --factory-startup --python-exit-code 1 --python <検証処理> -- --addon <受信アドオン> --output <同じ出力先>`で起動します。
5. 実通信はBlender検証処理の`--listen`でループバックの39549番ポートを待ち受け、Unity側で`PoseSyncChecks.PreparePlay`を実行します。この処理はPlay Mode遷移を待つため、Unityの`-quit`を付けずに実行します。
6. `PoseSyncChecks.BuildPlayer`でWindowsのMonoプレイヤーをビルドし、Blenderの待ち受けを再開してから起動します。結果の保存先はプレイヤーの`-poseSyncOutput <JSONパス>`で指定します。

Unityの結果は`Logs/pose_sync`、NDMFの結果は`Logs/pose_sync_ndmf_results.json`へ保存します。Play Modeの確認で作成したManager設定は確認後に空へ戻します。

## 未検証・制限

IL2CPP、WebGL、モバイル、macOS / Linux、Unity 6、個々の実アバター、Modular Avatarによる全種類の階層再構成、長時間の負荷・遅延は未検証です。Blenderへのボーン位置適用は元の実装と同様に未対応です。今回のNDMF確認は合成した階層でのリネームとPhysBoneの抽出です。

VCC / ALCOMの画面操作による導入確認は、今回の自動確認には含みません。

## 仕様の参照先

- [UnityのRuntime / Editorアセンブリ分割](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-asmdef.html)
- [VPMパッケージ形式](https://vcc.docs.vrchat.com/vpm/packages/)
- [MessagePack公式仕様](https://github.com/msgpack/msgpack/blob/master/spec.md)
