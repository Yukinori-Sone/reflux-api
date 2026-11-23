# Reflux（カスタム版）

[本家 olji/Reflux](https://github.com/olji/Reflux) に、**別のPCから記録を見るための API サーバー運用**を中心とした機能を足したものです（ブランチ `MyCustom`）。
ゲームのメモリは読み取るだけで、書き換えは一切しません。本家の README は[下](#reflux)にそのまま残しています。

## 本家からの主な拡張

| 分類 | 内容 |
|---|---|
| HTTP API | ゲームPCで動くコアが、曲・譜面・自己ベスト（クリア回数・プレー回数付き）・全国クリアレート・ライバル（SP/DP 各5人）・プレー履歴（INFINITAS ID 付き）・マスター（譜面の種類・ランプ・段位・都道府県・フォルダなど）を返す。LAN 公開・APIキー対応。一覧は [docs/API仕様.md](docs/API仕様.md) |
| Beat Ledger | 別PC用のクライアント **Beat Ledger**（`BeatLedger/`、旧 Reflux INFINITAS Beat Ledger）。曲スコア一覧・自己ベスト更新・統計（セルから譜面一覧）・ライバル比較・プレー履歴・AC比較・マスター・Reflux設定のタブ。表のコピーと Excel への書き出し。データは手元の SQLite にためる |
| 自己ベストの Before / After | ゲームは「今の自己ベスト」しか持たないが、コアがプレーごとに**プレー直前のゲームの自己ベスト**（ランプ・EX スコア・ミスカウント）を一緒に記録するので、1件のプレー記録が更新前と更新後を両方持つ。Beat Ledger はそこから「いつ・何が・どれだけ」更新されたか（ランプ上昇・EX 増加・ミス減少）を年・月・日ごとに出し、リザルト画面の画像も開ける。記録を始める前の分は、以前のスコアを基準として取り込むか、過去のリザルト画像のベストスコア欄から同じ形で作れる |
| ゲーム更新への対応 | 構造体の配置を `layout.json` に出し、API でメモリのダンプ・検索、オフセットと配置の書き換えができる（再ビルド不要）。INFINITAS `P2D:J:B:A:2026101400` に対応済み |
| リザルト画面の保存 | タスクトレイの管理ツール Reflux API Manager がリザルト画面を自動で撮り（フルスクリーン対応、演出が終わった1枚を選ぶ）、プレー記録と結びつけて保存する。Beat Ledger のプレー履歴をダブルクリックするとその画像が開く |
| 運用 | Windows サービス（`Service-Install.bat`）とタスクトレイの管理ツール Reflux API Manager（`RefluxAPIManager.exe`。状態・設定・サービスの開始停止・リザルト画面の撮影。Reflux.exe と同じフォルダ）。同じフォルダの Reflux は1つだけ動く。作業フォルダは exe のフォルダ |
| 出力の初期値 | API で読める内容のファイル出力（`tracker.tsv` の書き直し、latest・配信用ファイル）を初期値で止めている。セッション TSV は月ごと。各項目は戻せる |
| 本家の不具合修正 | 再アタッチ時の読み込み、セッション記録の消失、プレーデータの読み込みタイミング、未プレー譜面の DJ LEVEL など。一覧は [docs/本家不具合修正一覧.md](docs/本家不具合修正一覧.md) |
| 配布物 | `build-dist-self-contained.bat` / `build-dist-framework-dependent.bat` で、コアと Beat Ledger の配布用フォルダと zip を作る |

## 資料

- 設定とファイル（本家との違い）: [docs/設定ファイルメモ_Custom.md](docs/設定ファイルメモ_Custom.md)（本家の分は [設定ファイルメモ_Original.md](docs/設定ファイルメモ_Original.md)）
- API: [docs/API仕様.md](docs/API仕様.md)
- ゲームのメモリ上の持ち方の調査: [docs/INFINITAS動作.md](docs/INFINITAS動作.md)
- ビルド環境の作り方: [docs/環境構築-Tools.md](docs/環境構築-Tools.md)、Git の運用: [docs/Git管理メモ.md](docs/Git管理メモ.md)

---

# Reflux

Probes INFINITAS memory and outputs all top scores into a tsv file.

It also saves plays in sessions files with the following data:
- Settings (RAN,LIFT,Autoscratch etc.)
- Play results (Score, lamp, DJ level)
- Judge data (Pgreat, misses, fast/slow)
- Metadata (Gauge percent, note progress)

It also dumps the database for chart unlocks.

Some live streaming utilities are available as well, such as outputting current playing song and the play state tracking info to separate files for displaying on screen or automating behaviour.

Play data is, depending on configuration, appended to a .tsv session file, a json file and/or sent to a URL running a [Reflux Server](https://github.com/olji/Reflux_Server) (Or homebrew with compatible API). 

Memory offsets used to find the relevant information is stored in a file along with the build version it works with. On start the binary will probe the executable for build version by brute force and compare with what's listed in the offset file.
If no match it will check for an applicable offset file on a configurable server.
This behaviour can be turned off if unwanted.

Full compatibility with Mina's Unlock Tracker Spreadsheet (make your own copy): https://docs.google.com/spreadsheets/d/1oCcBVNUSRKA5NE9d6lYEyUNHPBosMZchtwsy0yL_nSM/edit?usp=sharing
