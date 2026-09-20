# Changelog

## 0.1.0 - 2026-09-20

- RuntimeとEditorを分離したVPMパッケージとして初公開。
- schema v2 / v3互換の専用MessagePack処理を内蔵し、外部DLL・NuGet・Define設定を不要化。
- Blender受信アドオンと導入案内を同梱。
- NDMF連携を任意アセンブリとして分離し、対応バージョンがあるときに有効化。
- 受信コマンドの長さ・ネスト・種別・フレーム数を検証。
- Blender受信停止時に古い待機フレームが再適用される競合を修正。
