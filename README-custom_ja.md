# Reflux API（Reflux のカスタム版）

[olji/Reflux](https://github.com/olji/Reflux) のフォークです。beatmania IIDX INFINITAS のプレー記録・自己ベストを読み取る Reflux に、
**別の PC から記録を見るための HTTP API サーバー**（以下 Reflux API Server）と、ゲーム PC で使うタスクトレイの管理ツールを足しています。
ゲームのメモリは読み取るだけで、書き換えは一切しません。Reflux本家の README は [README.md](README.md) にそのまま残しています。

株式会社コナミデジタルエンタテインメント（KONAMI）とは関係のない非公式のツールです。使う場合は自己責任でお願いします。
beatmania IIDX は株式会社コナミデジタルエンタテインメントの登録商標です。

## 構成

| もの | 内容 |
|---|---|
| `Reflux.exe`（Reflux API Server） | ゲーム PC で動くコア。Reflux本家の記録機能に加えて、曲・譜面・自己ベスト（クリア回数・プレー回数付き）・全国クリアレート・ライバル（SP/DP 各5人）・プレー履歴（プレー直前の自己ベスト付き）・マスターを HTTP API で返す。LAN 公開・APIキー対応。Windows サービスとしても動く |
| `RefluxAPIManager.exe`（Reflux API Manager） | Reflux.exe と同じフォルダに置くタスクトレイの管理ツール。状態の表示、設定の変更、サービスの開始・停止、リザルト画面のスクリーンショット（フルスクリーン対応。プレー記録と同じ名前で保存） |
| クライアント | 記録は API を使うクライアントで見る。クライアントの例: 同じ作者の [Beat Ledger](https://github.com/kun-9821/beat-ledger) |

## Reflux本家からの主な拡張

| 分類 | 内容 |
|---|---|
| HTTP API | 一覧は [docs/API仕様.md](docs/API仕様.md)。リクエストではゲームのメモリを読まない（メインループが読んだ内容を返す） |
| 自己ベストの Before / After | プレーごとに、プレー直前のゲームの自己ベスト（ランプ・EX スコア・ミスカウント）を一緒に記録する |
| ゲームの更新への対応 | 構造体の配置を `layout.json` に出し、API でメモリのダンプ・検索、オフセットと配置の書き換えができる（再ビルド不要）。INFINITAS `P2D:J:B:A:2026111100` に対応 |
| プラグイン | `plugins\*.dll` を読み込み、プレー記録の項目・API のエンドポイント・`layout.json` の節を足せる（[docs/API仕様.md](docs/API仕様.md) の「プラグイン」） |
| 運用 | Windows サービス（`Service-Install.bat`）と Reflux API Manager。同じフォルダの Reflux は1つだけ動く。作業フォルダは exe のフォルダ |
| 出力の初期値 | API で読める内容のファイル出力（`tracker.tsv` の書き直し、latest・配信用ファイル）を初期値で止めている。セッション TSV は月ごと。どれも設定で戻せる |
| Reflux本家の不具合修正 | 再アタッチ時の読み込み、セッション記録の消失、プレーデータの読み込みタイミング、未プレー譜面の DJ LEVEL など。一覧は [docs/本家不具合修正一覧.md](docs/本家不具合修正一覧.md) |

## 使い方

配布物（`build-dist.bat` で作る `dist\RefluxAPIServer`）の「はじめにお読みください.txt」（[distribution/Reflux/](distribution/Reflux/)）を見てください。
設定とファイルの Reflux本家との違いは [docs/設定ファイルメモ_Custom.md](docs/設定ファイルメモ_Custom.md)、管理ツールは [docs/RefluxAPIManager仕様.md](docs/RefluxAPIManager仕様.md)。

## ビルド

- .NET SDK（9 で確認）。`dotnet build Reflux.sln`。コアは `netcoreapp3.1`（ランタイムはサポート切れなので配布物はランタイム同梱）、Reflux API Manager は .NET Framework 4.8
- `build-dist.bat`: `dist\RefluxAPIServer`（Reflux.exe・RefluxAPIManager.exe・説明書・サービス登録の bat）と zip を作る

## ライセンス

MIT（[LICENSE](LICENSE)）
