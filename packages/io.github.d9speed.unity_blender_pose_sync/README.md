# D9speed Unity Blender Pose Sync

UnityのポーズとカメラをBlenderへ送るツールです。Humanoid、任意の追加ボーン、PhysBoneを追跡し、複数のアバター・衣装を1接続で送信できます。

## 導入

1. [VPM案内ページ](https://d9speed.github.io/Unity_Tools/)からVCC / ALCOMへリポジトリを追加し、**D9speed Unity Blender Pose Sync**を導入します。
2. Unityで`D9speed / Animations / PoseSync Setup`を開き、「セットアップ」を押します。依存パッケージの導入とコンパイルが終わると、チェックと進捗ボックスが緑になります。
3. 「Blender用アドオンの場所を開く」を押し、表示された`blender_pose_receiver_world.py`をBlenderのプリファレンス → アドオン →「ディスクからインストール」で選び、有効化します。
4. Blenderの3D Viewサイドバー → Pose Syncで対象Armatureを選び、`Start Pose Receiver (World)`を押します。
5. Unityの`D9speed / Animation / Pose Sync Manager`でAnimatorを追加し、Play Modeを開始します。

Unity 2022.3.22f1以降、Blender 4.5以降向けです。セットアップにはインターネット接続とGitが必要です。NuGetForUnity 4.5.0、公式NuGet版MessagePack 3.1.9（Annotations・Analyzerと推移的依存を含む）、MessagePack.Unity 3.1.9を導入します。DLLはVPMパッケージに同梱せず、公式配布元から取得します。Blender側のPython msgpackは任意です。

導入状態はコンパイルをまたいで引き継ぎます。途中で失敗した場合は画面の説明とConsoleを確認して「再試行」を押してください。依存関係が揃うまではSetup画面のみが有効で、完了するとRuntime・Managerも使えるようになります。`POSESYNC_HAS_MESSAGEPACK`の設定は自動です。

既定の接続先は同じPCの`127.0.0.1:39541`です。TCP接続は認証・暗号化を備えないため、別PCへ接続する場合は信頼できるネットワーク内で使ってください。

## 収録内容

- `Runtime/`: PoseSyncManager、互換用BlenderPoseSenderWorld、公式MessagePackによるシリアライズ。
- `Editor/`: Manager、1フレームSnapshot、Play Mode用の一時Manager生成。
- `Editor/Setup/`: 依存関係の導入画面と、NuGetForUnity導入後に有効になる連携部分。
- `Editor/NDMF/`: NDMF 1.11以上・2.0未満が導入済みの場合だけ有効になるボーン参照の焼き込み連携。
- `Blender~/`: 対応するBlender受信アドオン。Unityはこのフォルダをアセットとして取り込みません。

RuntimeとEditorは別アセンブリとして、**1つのVPMパッケージで同時に導入・更新**されます。通常の使い方ではアバターへコンポーネントを追加せず、Play Mode中だけ一時Managerを生成します。停止すると削除されます。

Runtimeは通常のUnityプレイヤーからも参照できます。明示的にPoseSyncManagerを配置して設定する使い方を想定し、Editorの画面・自動生成・Play Mode操作コマンドはプレイヤーに含めません。VRChat内で任意のC#スクリプトを実行する機能ではありません。VRChat向けアバターには送信コンポーネントを保存せず、Manager画面から使ってください。

## Snapshot・カメラ・衣装

`D9speed / Animation / Send Pose Snapshot to Blender`では、Play Modeに入らず1フレームを送れます。AnimationウィンドウのPreviewを停止して使用してください。

Managerのカメラ同期はCamera Object / Scene Viewに対応します。Blender側ではCamera Objectと3D Viewへの適用を選択できます。

Modular Avatarで統合する衣装は、PhysBone設定オブジェクトを含む衣装ルートを`Merge Armature Tracking (for Clothes)`へ登録します。PhysBoneの検出はリフレクションを使うため、VRChat SDKは必須依存ではありません。NDMFも任意で、自動インストールしません。

通常はUnityとBlenderのボーン名を直接照合するため、辞書JSONは不要です。名前が異なる場合のみ、Blender側でHumanoid名とボーン名の対応辞書を指定します。

## 旧Assets版からの移行

旧版のEditor / Runtimeと本パッケージを同時に入れないでください。型とGUIDが重複します。バックアップ後、Unityを閉じて旧UnityBlenderPoseSyncのEditorとRuntimeをプロジェクト外へ退避し、VPM版を導入します。ツールの`.meta` GUIDは維持しています。元ファイルの自動削除は行いません。

他のツールが使っているMessagePackやNuGetForUnityを削除する必要はありません。MessagePack本体・Annotations・Analyzer・Unity Supportに異なる版がある場合は、既存版を自動変更せず案内を表示します。他ツールの対応状況を確認し、3.1.9へ揃えて再試行してください。NuGetForUnityは4.5系に対応します。VPM 0.1.0から更新した場合もSetupを実行してください。プロジェクト別のManager設定は継続して参照しますが、古い全プロジェクト共通設定は自動取り込みしません。

## 通信互換性と制限

schema v2 / v3、MessagePack map、TCPの長さ・種別ヘッダーを維持しています。通信DTOのシリアライズ処理はMessagePackのSource Generatorで生成します。シリアライズ設定はPose Sync専用で、他ツールの既定設定を変更しません。

ボーンの位置適用はBlender側で未実装です（回転・ルート位置・カメラを同期）。同じボーンにBlender側の物理も適用すると二重になるため、必要に応じて止めてください。WebGL、IL2CPP、モバイル、すべてのアバター構成は未検証です。

MIT License / d9speed / d09pseed@gmail.com
