# Changelog

## 0.1.2 — 2026-09-21

- Manager・Snapshot・Setupのメニューをすべて`D9speed/Animations`へ統一。
- 既存のMessagePack 3.1.7などでセットアップが停止する問題を修正。3.1.xの旧パッチ版は3.1.9へ更新し、Annotations・Analyzer・Unity Supportも揃えます。
- 手動導入済みの関連NuGetパッケージも更新対象に含め、元の手動導入フラグを保持。
- 新しい版のダウングレードや別のメジャー・マイナー版の置き換えは行いません。
- 完了時の緑チェック・進捗ボックスを継承。

## 0.1.1 - 2026-09-20

- 独自MessagePack実装を公式NuGet版3.1.9へ置き換え、Source Generatorで通信DTOを処理。
- `D9speed / Animations / PoseSync Setup`へ導入画面を統一し、従来の緑チェックと進捗ボックスを継承。
- NuGetForUnity・MessagePackとその依存関係・MessagePack.Unityのセットアップを追加。
- コンパイル後も導入を継続し、依存関係が揃ってからRuntime・Editorを有効化。

## 0.1.0 - 2026-09-20

- RuntimeとEditorを分離したVPMパッケージとして初公開。
- schema v2 / v3互換の専用MessagePack処理を内蔵し、外部DLL・NuGet・Define設定を不要化。
- Blender受信アドオンと導入案内を同梱。
- NDMF連携を任意アセンブリとして分離し、対応バージョンがあるときに有効化。
- 受信コマンドの長さ・ネスト・種別・フレーム数を検証。
- Blender受信停止時に古い待機フレームが再適用される競合を修正。
