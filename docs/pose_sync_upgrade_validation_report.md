# Animationsメニュー統一・Pose Syncセットアップ更新の検証

2026-09-21、Unity 2022.3.22f1 / Windowsの専用プロジェクトで確認しました。

## 修正内容

- Animation Tools 0.1.1: Animator Playback Preview、Random Hand Muscle Generatorを`D9speed/Animations`へ移動。
- Unity Blender Pose Sync 0.1.2: Manager、Snapshot、Setupを同じ`D9speed/Animations`へ統一。SetupからManagerを開くボタンや案内も更新。
- MessagePack 3.1.7などの導入済み環境で、セットアップが版の不一致を理由に止まる挙動を修正。
- セットアップを実行すると、3.1.xの旧パッチ版の本体・Annotations・Analyzer・Unity Supportを3.1.9へ更新。手動導入済みのNuGetパッケージも対象とし、そのフラグを維持。
- 新しい版・別のメジャー／マイナー版・プレリリース版は自動変更しない。更新前に対象全体の版を確認。
- NuGetの依存解決とUnity Package Managerの更新APIを使用。外部DLLの直接配布やmanifestの手動書き換えは行わない。

## 再現と結果

| 確認 | 結果 |
|---|---|
| 旧Pose Sync 0.1.1に、NuGet本体・Annotations・Analyzer・Unity Support 3.1.7を導入 | 「3.1.9へ揃えてから再試行」で停止することを再現。旧版のまま残ることも確認 |
| 同じプロジェクトをPose Sync 0.1.2へ更新してSetupを実行 | すべて3.1.9へ更新し、Runtimeの読み込みとDone到達を確認 |
| 3種類のNuGetパッケージをそれぞれ手動導入済みとした状態 | 更新成功。`manuallyInstalled=true`を維持 |
| NuGetもMessagePackもない新規プロジェクト | 初回セットアップ成功。コンパイルをまたいで続行 |
| 完了表示 | 緑のチェック3個、緑の進捗ボックス4個、「閉じる」を維持 |
| 完了後の再セットアップ | 既に揃っている依存をそのまま使って再度完了 |
| メニュー | `Animations`配下の5画面を開けること、D9speedアセンブリに旧`Animation/`メニューがないことを確認 |
| 版の判定 | 3.1.0・3.1.7・3.1.8を更新可能と判定。3.1.10・3.2.0・3.0.0・2.5.0・プレリリース・不明な版は拒否 |
| 更新後のPose Sync Edit Mode検証5項目 | 成功。Runtime/Editor分離、全14通信DTOのFormatter、メニュー、初期設定、合成ボーン・カメラの送信データ、受信コマンド検証 |

依存ファイルの入れ替え中、Unityのビルド処理が旧パスを参照して一時的に再実行されるログがありました。自動で再解決・コンパイルを完了し、更新後の別起動での検証も終了コード0です。

## 再実行

`tools/pose_sync_upgrade_checks.cs`を専用プロジェクトの`Assets/Editor`へ配置します。通常の作業プロジェクトでは実行しないでください。

1. 旧Pose Sync 0.1.1を入れた新規プロジェクトで`D9speed.PackageValidation.pose_sync_upgrade_checks.Baseline`を実行します。公式APIで3.1.7を導入し、旧版の停止を再現します。
2. Pose Syncを0.1.2に置き換え、Animation Tools 0.1.1とEditor Coreを導入して`D9speed.PackageValidation.pose_sync_upgrade_checks.Upgrade`を実行します。
3. 結果は`Logs/pose_sync_baseline_results.json`と`Logs/pose_sync_upgrade_results.json`です。新規導入は`tools/pose_sync_setup_checks.cs`で確認します。

いずれもUnityを`-batchmode -executeMethod <対象メソッド>`で起動し、非同期更新を待つため`-quit`を付けません。検証コードが完了時の終了を制御します。

今回の更新では、Blenderとの実TCP通信、Playerビルド、IL2CPPの再検証は行っていません。通信処理のコードは変更していません。
