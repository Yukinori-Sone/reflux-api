# 設定ファイルメモ（Custom: 独自版の差分）

`MyCustom` ブランチで、本家から**追加・変更した部分だけ**をまとめたもの。
ここに書いていない設定・ファイルは本家と同じ → [設定ファイルメモ_Original.md](設定ファイルメモ_Original.md) を参照。

- **基準**: 2026-06-18 時点の `MyCustom`（本家 master `0326038` ＋ 独自の変更）
- **メンテ方針**: 独自の変更で設定項目やファイル出力が変わったら、このファイルを更新する

## 差分の早見表

**この独自版（MyCustom ブランチ）は API サーバーとしての運用が前提**なので、同梱の `config.ini` と `coresettings.json` の初期値を、クライアントが API から読める内容のファイル出力をしない設定にしている（下の「API 運用向けの初期値」）。本家と同じ出力にしたいときは各項目を戻す。

| 区分 | 対象 | 概要 |
|---|---|---|
| 【追加】設定 | `config.ini` の `[Api]` セクション | 別PCクライアント用の HTTP API。初期値は無効なので、そのままなら本家と同じ動き |
| 【追加】ファイル | `bestdetail.json` | 自己ベストを出したプレーの判定内訳（PG/GR、BAD/POOR） |
| 【追加】ファイル | `plays.jsonl` | 全プレー記録（INFINITAS ID 付き）。`[Api]` 有効時だけ。`/api/plays` で返す |
| 【追加】ファイル | `masters-cache.json` | `/api/masters` の表の最後に読めた内容（INFINITAS が起動していないときに返す）。中身が変わったときだけ書く。消してよい |
| 【追加】フォルダ | `screenshots/` | リザルト画面のスクリーンショット（モニターが撮る。1枚 PNG 1.8MB 前後）と `capture.log`。`/api/screenshots` で返す。`.png` / `.jpg` / `.jpeg` のどれでもよく（この順に優先）、外部で変換してもファイル名の拡張子より前を変えなければ結びつく。消すのは手で |
| 【変更】設定 | セッション TSV の単位 | `coresettings.json` の `sessionFile` で、起動ごと（本家と同じ）／日ごと／月ごと（初期値）／1ファイル／出力しないを選べる |
| 【変更】初期値 | 同梱の `config.ini` | `[Api] enabled = true`、`savelatestjson`・`savelatesttxt`・`[Livestream]` の3項目を false（本家は true） |
| 【追加】動作 | Windows サービス | `Reflux.exe --service` でサービスとして動く。登録・解除は `Service-Install.bat` / `Service-UnInstall.bat`。同じフォルダの Reflux は1つしか動かない |
| 【変更】動作 | `tracker.db` / `log.txt` | INF 再起動（再アタッチ）時の読み込みを修正 |
| 【変更】動作 | セッション TSV/JSON | INF 再起動で記録が消える本家の不具合（Original の ※1）を修正 |
| 【変更】動作 | `offsets.txt` / `archive/` | API からオフセットを書き換えられる。`[Api]` 有効時はバージョン不一致でも対話式探索にすぐ入らず待機する |
| 【追加】ファイル | `coresettings.json` | コアの動作設定（監視間隔・解禁データの確認頻度・`tracker.tsv` の書き出し・優先度・セッション TSV の単位）。無ければ初期値（`tracker.tsv` を書かない・セッション TSV は月ごと。ほかは本家と同じ）。API／クライアントの「Reflux設定」タブで書き換える |
| 【追加】ファイル | `layout.json` | 構造体の配置（曲リスト・解禁データ・自己ベスト・プレーデータ・判定・オプション・選曲中の譜面・状態判定の目印の、サイズや各項目の位置）。無ければ組み込みの初期値。API で書き換える |
| 【変更】動作 | 曲リストの読み込み | 読み込んだ曲リストがおかしければ（曲数・ID の並び・レベル・ノーツ数）先へ進まず待つ。本家は先へ進んで落ちることがある |

## 共通の注意（差分）

- 【追加】`[Api]` セクションだけは、無くてもメッセージを出さず既定値で動く（＝API 無効）。
  他のセクションは本家どおり、キーが無いと `Couldn't read key ...` が表示される

## config.ini（差分）

### 【追加】[Api] 別PCクライアント用 API

| キー | 初期値 | 意味 |
|---|---|---|
| `enabled` | `false` | HTTP API を起動するか。INF にアタッチしていなくても起動する |
| `host` | `localhost` | 待ち受けアドレス。`localhost` は同じPCからのみ。別PCから使うなら `+`（全インターフェース） |
| `port` | `8790` | 待ち受けポート |
| `apikey` | 空 | 設定すると、クライアントはヘッダー `X-Api-Key` に同じ値を付けないと拒否される。LAN 公開時は設定推奨。**`[RemoteRecord]` の `apikey` とは別物** |

- API はリクエストでゲームのメモリを読まない（メインループが2秒ごとに読んだ結果を返すだけ）。
  使えるエンドポイントは `GET /api/status|songs|charts|scores|latest|session`、`POST /api/attach/retry`
- 例外として、ゲームのアップデート対応用に次のエンドポイントがある（リクエストのたびにメモリを読む／ファイルを書く）。各エンドポイントの詳細は [API仕様.md](API仕様.md)
  - `GET /api/offsets`: `offsets.txt` の内容と使用中の値、ゲームのバージョン、bm2dx のベースアドレス
  - `PUT /api/offsets`: `offsets.txt` を書き換えて再アタッチ。本文は `{"version":"P2D:J:B:A:…","offsets":{"songList":"0x…"},"reattach":true}`（どれも省略可。省略したオフセットは今の値のまま、`version` の省略時は検出したゲームのバージョン）
  - `GET /api/memory/info`: bm2dx のベースアドレス・サイズ
  - `GET /api/memory?address=…&length=…&format=hex|bin&encoding=sjis`: メモリダンプ（最大 16MB、初期値は 16進表示で 0x100 バイト）。`address` は `songList+0x630`、`base+0x1000` のように、オフセット名・`base`・16進数を `+` `-` でつないで書ける
  - `GET /api/memory/search?start=…&length=…&hex=…|text=…&encoding=…|int32=…|int64=…&max=…`: バイト列・文字列（既定 Shift-JIS）・数値の検索（最大 1GB、ヒットは最大 1000 件）
  - `GET|PUT /api/layout`、`GET|POST /api/layout/preview`: 構造体の配置（`layout.json`）の確認・試し読み・書き換え。形式と初期値は [API仕様.md](API仕様.md)
- `apikey` を設定していなければ、LAN 内の誰でもオフセットを書き換え・メモリを読めるので、`host = +` で公開するときは `apikey` の設定を推奨

`host = +` にする場合の事前準備（ゲームPCで1回だけ、管理者権限で）:

```powershell
netsh http add urlacl url=http://+:8790/ user=<ゲームPCのユーザー名>
# ファイアウォールでポート 8790（TCP 受信）を許可
New-NetFirewallRule -DisplayName "Reflux API" -Direction Inbound -Protocol TCP -LocalPort 8790 -Action Allow -Profile Private
```

## 生成・更新ファイル一覧（差分）

| 【変更】動作 | 作業フォルダ | 起動時に作業フォルダを Reflux.exe のあるフォルダに切り替える。本家は起動したときの作業フォルダ（ショートカットの「作業フォルダー」、bat のカレント）にすべてのファイルを読み書きする |

タイミングの用語（起動時・アタッチ時・リザルト時 など）は Original と同じ意味。

### 【追加】記録データ

| ファイル | 内容 | 作成 | 読込 | 更新 | 削除 | 関連設定 |
|---|---|---|---|---|---|---|
| `bestdetail.json` | 自己ベストを出したプレーの判定内訳（PG/GR、BAD/POOR）。ゲームは EX スコアとミスカウントの合計しか保存していないため、Reflux が記録したプレー分だけ独自に保存する。**消さないこと** | リザルト時（初回） | 起動時 | リザルト時、EX スコアかミスカウントが自己ベストと一致したとき | ― | 設定に関係なく保存（`[Api]` が無効でも保存される） |
| `plays.jsonl` | 全プレー記録。1行1プレーの JSON（`/api/latest` と同じ項目＋ `infinitasId`・`source`・`misscount`）。セッション TSV の設定とは関係なく1ファイルに追記し続ける。**消さないこと**（クライアントは差分しか取らない） | 最初のアタッチ時（無ければ。そのとき `sessions/Session_*.tsv` の記録を1回だけ取り込む） | `/api/plays` のたび | リザルト時（プレーデータが読めた記録だけ） | ― | `[Api] enabled = true` のときだけ |

### 【変更】INF 再起動（再アタッチ）時の動作

| ファイル | 本家の動き | 独自版の動き |
|---|---|---|
| `tracker.db` | 2回目以降のアタッチ時、`tracker.db` の読み込みが重複エラーで途中で止まる。ゲーム内の自己ベストの読み直しも前回分で打ち切られるため、Reflux を通さずに更新された自己ベスト（Reflux 停止中のプレー等）が反映されないまま書き直されることがある | アタッチのたびに `tracker.db` とゲーム内の自己ベストを読み直す |
| `log.txt` | 上記の重複エラーが再アタッチのたびに記録される | このエラーは出なくなる |
| `sessions/Session_*.tsv` / `.json` | アタッチのたびに作り直されるため、Reflux を落とさずに INF を再起動すると、それまでのプレー記録が消える（Original の ※1） | ファイルが無いときだけ作成する。Reflux 1回の起動につき1ファイルで、再アタッチ後のプレーも同じファイルに追記される |

### 【変更】オフセット関連（`[Api]` 有効時のみ）

| ファイル | 本家の動き | 独自版の動き |
|---|---|---|
| `offsets.txt` | バージョン不一致のとき、サーバーから取得できなければすぐ対話式のオフセット探索に入る（コンソールでの入力待ち） | サーバーから取得できなければ、`PUT /api/offsets` を待つ（コンソールで Enter を押せば本家どおり対話式探索）。`PUT /api/offsets` が来るたびに書き換えて再アタッチする |
| `archive/` | 置き換え時に旧 `offsets.txt` を `archive/<バージョン>.txt` へ移動（同じ名前なら上書き） | `PUT /api/offsets` のときは `archive/<バージョン>_<日時>.txt` にコピーしてから書き換える（上書きしない）。`PUT /api/layout` のときも、旧 `layout.json` を `archive/layout_<日時>.json` にコピーする |
| `layout.json` | ―（構造体の配置はコードに固定） | 構造体の配置をここから読む（ファイルが変われば次に読むときに反映。曲リストはアタッチ時にだけ読むので、`PUT /api/layout` は再アタッチまで行う）。無ければ組み込みの初期値（2026101400 用）。`[Api]` が無効でも、ファイルがあれば使われる |

### 【追加】コアの動作設定（`coresettings.json`）

| 設定 | 本家の動き | 初期値 | 変えられる内容 |
|---|---|---|---|
| 監視間隔（選曲画面） | 2秒ごと | 本家と同じ | 0.5 / 1 / 2 / 3 / 5 秒 |
| 監視間隔（プレー中） | 2秒ごと（画面によらず同じ） | 本家と同じ | 0.25 / 0.5 / 1 / 2 / 3 / 5 秒。リザルト画面に早く気づくようにする |
| 監視間隔（リザルト画面） | 2秒ごと（画面によらず同じ） | **0.5秒** | 0.1 / 0.25 / 0.5 / 1 / 2 / 3 / 5 秒。画面を抜けたことに早く気づく（モニターの連写を止める）。読むのは状態の目印だけ |
| 解禁データの確認 | 選曲画面で2秒ごとに読み直す | 本家と同じ | 10・30・60秒ごと、選曲画面に戻ったときだけ |
| `tracker.tsv` | 選曲画面で2秒ごとに書き直す（約 480KB） | **書き出さない** | 毎回（本家と同じ）、変化があったときだけ（プレー後・解禁時・アタッチ後） |
| Reflux の優先度 | 通常 | 本家と同じ | 通常以下、低 |
| セッション TSV の単位 | Reflux の起動ごとに1ファイル（`Session_yyyy_MM_dd_HH_mm_ss.tsv`） | **月ごと** | 起動ごと（本家と同じ）、日ごと（`Session_yyyy_MM_dd.tsv`）、月ごと（`Session_yyyy_MM.tsv`）、1ファイル（`Session_all.tsv`）、出力しない（プレー履歴は `plays.jsonl` に残る）。無ければヘッダー付きで作り、あれば追記する。日付は `[Record] uselocaltime` に従う。セッション JSON は起動ごとのまま |

形式は [API仕様.md](API仕様.md) の「コアの動作設定」。`[Api]` が無効でも、ファイルがあれば使われる。

### 【変更】API 運用向けの初期値（同梱の `config.ini`）

| 項目 | 本家 | 独自版 | 止まる出力 |
|---|---|---|---|
| `[Record] savelatestjson` | true | false | `latest.json` |
| `[Record] savelatesttxt` | true | false | `latest.txt`・`latest-*.txt` |
| `[Livestream] showplaystate` | true | false | `playstate.txt` |
| `[Livestream] enablemarquee` | true | false | `marquee.txt` |
| `[Livestream] enablefullsonginfo` | true | false | `title.txt`・`artist.txt`・`englishtitle.txt`・`genre.txt`・`level.txt`・`folder.txt` |
| `[Api] enabled` | （項目なし） | true | ―（`host = localhost` なので同じ PC からだけ。LAN の別 PC からは `host = +` と urlacl が必要） |

残る出力: `tracker.db`（自己ベストの保存。PERFECT などゲームのメモリに無い情報）、`bestdetail.json`（判定の内訳）、`plays.jsonl`（プレー履歴）、`sessions/Session_*.tsv`（月ごと）、`log.txt`（エラー時）。配信で使う場合は `[Livestream]` などを true に戻す。上の5項目は API（`GET|PUT /api/outputs`、2026-11-23）でも変えられ、その場で効く（`config.ini` にも書く。コアの起動し直しは要らない）。

### 【追加】Windows サービス

- `Service-Install.bat`（管理者で実行）: 同じフォルダの `Reflux.exe` をサービス「Reflux API Server」（`RefluxAPIServer`）として登録して起動する。Windows の起動時に自動で開始（遅延開始）、アカウントは LocalSystem、止まったら1分後に再起動（3回まで、1日でリセット）
- `Service-UnInstall.bat`（管理者で実行）: 停止して登録を消す。フォルダの中身はそのまま
- サービスのときは画面に窓が出ない。状態は API（`/api/status`）かクライアントで見る。エラーは `log.txt`
- 同じフォルダの Reflux は1つしか動かない（あとから起動した方はメッセージを出して終了する）。サービスにしたら、スタートアップのショートカットは消す
- オフセット不一致のときの対話式探索（コンソールで Enter）はサービスでは使えない。API（`PUT /api/offsets`）で直す
- 状態の表示・設定の変更・サービスの開始停止・リザルト画面のスクリーンショットは、タスクトレイの管理ツール Reflux API Manager（`RefluxAPIManager.exe`、Reflux.exe と同じフォルダ。仕様は `RefluxAPIManager仕様.md`）
- `Service-Install.bat` は、ログオン中のユーザー（IU）にもこのサービスの開始・停止を許可する（`sc sdset`）。許可されていない環境では、Reflux API Manager が UAC で管理者の確認を出す。登録の最後に Reflux API Manager を（管理者ではなく）起動する

本家の不具合として直したものは [本家不具合修正一覧.md](本家不具合修正一覧.md) にも記録している。

### 変更なし（念のため）

- `tracker.tsv` の2秒ごとの書き直しなど、API 運用時は不要になるファイル出力の削減は**未着手**（実機確認後の候補）
