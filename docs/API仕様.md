# Reflux コア API 仕様

独自に追加した Reflux コアの HTTP API（`Reflux/Api/`）の一覧。クライアント（Reflux INFINITAS Score Manager、`RefluxScoreManager/`）や curl から使う。
エンドポイントを追加・変更したら、このファイルも更新すること。

## 接続

- `config.ini` の `[Api]` で有効にする（`enabled = true`）。設定項目は [設定ファイルメモ_Custom.md](設定ファイルメモ_Custom.md) を参照
- URL は `http://<host>:<port>/api/...`（既定ポート 8790）
- `apikey` を設定したときは、全リクエストにヘッダー `X-Api-Key: <値>` が必要（無い・違うと `401`）
- LAN に公開せずに別PCから使うなら、SSH のポート転送が手軽（urlacl・ファイアウォールの設定が要らない）
  ```sh
  ssh -N -L 18790:localhost:8790 <ゲームPC>   # 以降 http://localhost:18790/api/... で使える
  ```
- API はアタッチ前から動く。INFINITAS が起動していなくても `status` などは返る
- `host = localhost` で待ち受けているときは、接続先も `localhost` と書く（`127.0.0.1` だと Host ヘッダーが合わず 400 になる）
- ゲームPC（192.168.1.10）は 2026-10-15 から `host = +`・`apikey` ありで LAN に公開している。クライアントの接続先は `192.168.1.10`・`8790`・API キー

## 共通

- 応答は JSON（UTF-8、キーは camelCase）。メモリダンプだけはテキストかバイナリ
- エラーは `{"error":"<コード>","message":"<説明>"}` で、HTTP ステータスは 400（パラメータ誤り）、401（API キー）、404（無いパス）、409（INFINITAS 未アタッチ）、500（内部エラー）
- 数値パラメータは 16進（`0x` 付き・無しのどちらでも）。10進で書くときは末尾に `d`（例: `100d`）。`layout.json` の数値だけは `"0x730"` / `"-0x18"` / 10進の数値のどれでもよい
- アドレス式（`address` / `start`）は、16進数・オフセット名・`base`（bm2dx のベースアドレス）を `+` `-` でつないで書ける。
  オフセット名は `songList` `unlockData` `playSettings` `playData` `currentSong` `judgeData` `dataMap`（大文字小文字は問わない）。
  **URL の中の `+` は空白扱いになるので `%2B` と書く**（例: `songList%2B0x730`）

## 通常のエンドポイント（ゲームのメモリは読まない）

メインループが約2秒ごとに読んだ結果を返すだけなので、何度呼んでもゲームPCの負荷にならない。

| メソッド・パス | 内容 |
|---|---|
| `GET /api` | エンドポイントの一覧 |
| `GET /api/status` | 状態。下表 |
| `GET /api/songs` | 曲の一覧 |
| `GET /api/charts` | 譜面の一覧 |
| `GET /api/scores` | 自己ベストの一覧（全譜面） |
| `GET /api/latest` | 直前のプレー記録（無ければ `null`） |
| `GET /api/session` | 今回の Reflux 起動以降のプレー記録（`latest` と同じ形の配列） |
| `GET /api/plays` | これまでの全プレー記録（`plays.jsonl`）。下記 |
| `GET /api/screenshots` | リザルト画面のスクリーンショットの一覧。下記 |
| `GET /api/screenshot?key=...` | スクリーンショット1枚（`image/png` か `image/jpeg`）。下記 |
| `GET /api/clearrates` | 全国のクリアレート（譜面ごと）。下記 |
| `GET /api/rivals` | ゲームに登録したライバル（SP・DP 別に最大5人）。下記 |
| `GET /api/rivals/scores` | ライバル全員の譜面ごとの EX スコア・ランプ。下記 |
| `GET /api/settings` | コアの動作設定（下記）の今の値・初期値・選択肢 |
| `GET /api/masters?tables=...` | マスター・定数の表（曲・譜面・シリーズ・楽曲パックも選べる）。下記 |
| `PUT /api/settings` | 動作設定を変える。本文に書いた項目だけ変わり、その場で反映して `coresettings.json` に保存する |
| `POST /api/attach/retry` | 再アタッチを要求する（`202`）。オフセット不一致の待機中や、曲リストの読み込みが進まないときにも効く |

### `GET /api/status`

| キー | 内容 |
|---|---|
| `apiVersion` | API の版（今は 1） |
| `instanceId` | Reflux を起動するたびに変わる ID。下の revision はこの ID が同じ間だけ比べられる |
| `refluxVersion` | Reflux のバージョン。本家の版＋独自版の印（`1.16.6+custom.20261107`。日付はビルドした日） |
| `attachState` | `waitingForProcess`（INFINITAS 待ち）、`checkingVersion`、`offsetMismatch`（オフセット不一致で待機中）、`offsetSearch`（コンソールで対話式探索中）、`loading`（曲データ読み込み中）、`layoutMismatch`（読み込みを始めて 30 秒たっても曲リストが `Layout.CheckSongs` に通らない。INFINITAS の更新で構造体の配置が変わった可能性。`PUT /api/layout` で直すと再アタッチする）、`ready`（監視中） |
| `attachStateSince` | その状態になった時刻（UTC） |
| `message` | 状態の補足（不一致のときの案内、曲リストのチェック結果など）。無ければ `null` |
| `processId` | bm2dx のプロセス ID |
| `gameVersion` / `offsetsVersion` | ゲームのバージョン / `offsets.txt` のバージョン（`P2D:J:B:A:yyyymmddnn`） |
| `infinitasId` | ログイン中のプレイヤーの INFINITAS ID（`C` ＋ 12 桁。画面表示の `C-1234-5678-9012` のハイフン無し）。アタッチのたびに読む（e-amusement パスを切り替えると変わる）。読めなければ `null` |
| `djName` | ログイン中のプレイヤーの DJ NAME（`layout.json` の `player.profile`。プロフィールの ID がログイン中の ID と一致するときだけ。ログイン直後はまだ無いことがあり、選曲画面に戻るたびに読み直す）。読めなければ `null` |
| `playStateDetection` | プレー中の判定用マーカーが見つかったか |
| `gameState` | `starting` / `songSelect` / `playing` / `resultScreen`（アタッチするまでは `null`）。`starting` はアタッチ直後、ゲームがサーバーからデータを受け取り終わるまで（一番遅いライバルがそろうまで、最長2分）。`songSelect` は本家の名前のままで、タイトル・モード選択・選曲画面などのメニュー画面すべて（表示は「メニュー画面」） |
| `currentChart` | `{songId, title, difficulty}`。選曲中・プレー中の譜面 |
| `songsRevision` / `scoresRevision` | 曲データ / 自己ベストを読み直すたびに増える。クライアントはこれが変わったときだけ取り直す |
| `clearRatesRevision` | クリアレートの値が変わったときだけ増える（0 のうちは未取得） |
| `rivalsRevision` | ライバルの一覧・スコアが変わったときだけ増える（0 のうちは未取得） |
| `downloadsRevision` | ダウンロード済みの曲データ（`/api/downloads`）が変わったときだけ増える（0 のうちは未取得） |
| `sessionPlays` | 今回の起動以降に記録したプレー数 |
| `dataWarnings` | アタッチ中に読めないデータの種類の配列（`rivals` / `clearRates` / `playCounts` / `downloads`）。INFINITAS の更新で静的ポインタや構造体が動いた可能性。読めれば消える。ライバルはアタッチから2分たつまで出さない（ゲームが受け取り中のことがある） |
| `serverTime` | 応答した時刻（UTC） |

### `GET /api/songs` / `charts` / `scores`

`songs` / `charts` / `scores` / `clearrates` / `rivals` / `rivals/scores` / `downloads` は配列のまま返し、ヘッダーに `X-Read-At`（コアがそのデータをゲームから読んで内容が変わった時刻、UTC の ISO 8601）と `X-Game-Version`（そのときのゲームのバージョン）を付ける。まだ読んでいなければ付かない。songs と charts は同じ時刻

- songs: `{id, title, title2, artist, genre, folder, unlockType, label, packItemId, titleReading, artistReading, listed}`。`titleReading` / `artistReading` は曲名・アーティストの読み（カタカナ。ゲームの並び替え用の表 `Api/SongReadings.cs`。選曲画面のフォルダの中はこの順。表に無い4曲と、読めなかったときは null。読めなかったときは選曲画面に戻るたびに数回読み直し、`status.dataWarnings` に `readings` が出る）。`packItemId` は楽曲パックの商品ID（解禁データの +0xC、`I1800001`〜。パックで売っている曲＝解禁種別 Sub だけ、ほかは null）。`label` は本家の `customtypes.txt` のラベル（2026-03 以降のパックは無い）。`listed` が false の曲はゲームの解禁データに無く、選曲画面に表示されない（BIT 等で解禁できる「未解禁」とは別。2026101400 では IIDX RED Ending (Ryu☆Remix)・Seven Times Four の2曲。クライアントでは NOT DISPLAYED）。`id` は5桁ゼロ埋め、`folder` はバージョンフォルダ番号（0 = 1st style、1 = substream、80 = INFINITAS）
- charts: `{songId, difficulty, level, notes, bpm}`。`difficulty` は `SPB` `SPN` `SPH` `SPA` `SPL` `DPB` `DPN` `DPH` `DPA` `DPL`
- scores: `{songId, difficulty, unlocked, lamp, grade, exScore, missCount, pGreat, great, bad, poor, clearCount, playCount}`。`clearCount` / `playCount` はゲームが持っているクリア回数・プレー回数（自己ベストのハッシュマップの +0x28 / +0x2C。選曲画面に戻るたびに読み直し、変わっていれば `scoresRevision` が増える）。
  `lamp` は `NP` `F` `AC` `EC` `NC` `HC` `EX` `FC` `PFC`。`pGreat` 〜 `poor` は Reflux が記録したプレーで自己ベストと一致したときだけ値が入る（ゲームは保存していない）

### `GET /api/clearrates`

- `[{songId, difficulty, rate}]`。`rate` は全プレイヤーのクリアレート（%、小数第2位まで。ゲーム画面は小数第1位に四捨五入して表示）。値の無い譜面は含まない
- サーバーの集計値をゲームが持っているもの（`layout.json` の `clearRate`）。コアは ready になったときと、選曲画面に戻るたびに読み直す

### `GET /api/downloads`

- `[{songId, sound, movie, graphic, downloadedAt}]`。ゲームがダウンロード済みの曲だけ（曲ID順）。`sound` / `movie` / `graphic` はバイト数（`movie`・`graphic` は無い曲が多く、無ければ null）。`downloadedAt` は音のファイルの更新日時（UTC）で、ダウンロードした日（＝その曲を初めてプレーした日）
- ゲームは曲を初めてプレーするときにデータをダウンロードし、`<INFINITAS のフォルダ>\Resource\dlcache\` の `sound\<曲ID>.ifs`（音・譜面。どの曲にもある）・`movie\<曲ID>.bin`（背景動画）・`graphic\<曲ID>.ifs`（一部の曲の画像）に残す（2026111100 で確認。ファイルは先頭から暗号化されている）。`sound` があればダウンロード済みとする（実機ではプレー回数が 1 以上の曲と完全に一致した）。`movie` には曲ID以外の名前のファイル（イベントの動画など）もあるが返さない
- INFINITAS のフォルダは bm2dx.exe の場所（`<フォルダ>\gameppm2dx.exe`）から決める。コアは選曲画面に戻るたびにフォルダの一覧（名前・サイズ・日時だけ。ファイルは開かない、約3000件）を読み直す。見つからなければ `status.dataWarnings` に `downloads`

### `GET /api/plays`

- クエリ: `since`（ISO 8601。この時刻より後のプレーだけ）、`limit`（既定 10000、最大 100000）、`infinitasId`（そのプレイヤーだけ）
- `[{infinitasId, source, timestamp, songid, title, title2, diff, level, notecount, lamp, grade, exscore, misscount, pgreat, great, good, bad, poor, combobreak, fast, slow, gaugepercent, prematureend, playtype, style, style2, gauge, assist, range, ...}]`。古い順。値は `/api/latest` と同じく文字列
- `source`: `reflux`（コアが記録したプレー）／`sessionTsv`（`plays.jsonl` が無かったときに `sessions/Session_*.tsv` から1回だけ取り込んだもの。曲は曲名で ID に対応づけ、`infinitasId` は取り込んだときのプレイヤー、`misscount` が `-` だったものは `null`）
- `misscount` はミスカウントが有効なプレーだけ（途中終了などは `null`）。曲情報だけで判定の無い記録（プレーデータが読めなかったとき）は入れない
- `prevLamp` / `prevExScore` / `prevMissCount`: そのプレーの直前の、ゲームが保存していた自己ベスト（ランプは `lamp` と同じ略号、ミスは無ければ `null`）。選曲画面に戻るたびにゲームの自己ベストのハッシュマップ（`PlayCounts`）から読み直した値に、それ以降に記録したプレー（クイックリトライ）を足したもの。`tracker.db` は使わない（ランプがゲームとの最大値で、プレイヤー別でもないため）。ログイン中のプレイヤーの値。ゲームはランプ PERFECT を保存しないので、PFC の後も `FC`。ハッシュマップが読めないときと、この項目を入れる前（2026-10-31 より前）の記録・`sessionTsv` には無い
- `unsaved` / `battle` / `hran`（2026-11-04 から）: ゲームがプレーデータを書かず自己ベストにも残さないプレー（DOUBLE BATTLE・H-RANDOM）にだけ付く（`unsaved` = `true`、`battle` / `hran` = `true` / `false`）。Reflux が選曲中の譜面・判定・ゲージ・オプションから組み立てたもの（`Api/UnsavedPlay.cs`）: 譜面は選曲中の難易度の段＋左右で判定があれば DP（DOUBLE BATTLE は SP の譜面）、ランプはゲージの種類と残り（ASSIST EASY 60%・EASY/ノーマル 80%・HARD/EX HARD 生存、アシスト付きは ASSIST CLEAR、コンボが切れなければ FC）、`notecount` は DOUBLE BATTLE なら SP の2倍（AUTO SCRATCH でも）、DJ LEVEL はそのノーツ数で計算。`prevLamp` 等は無い。`tracker.db`・`bestdetail.json`・サーバー送信・JSON のセッションには入れない
- クライアントは `sessionPlays` が変わるたびに、手元の最新の `timestamp` を `since` にして差分を取る

### `GET /api/screenshots` / `GET /api/screenshot?key=...`

リザルト画面のスクリーンショット。撮るのはコアではなくタスクトレイのモニター（`RefluxAPIServerMonitor.exe`、ユーザーのセッションで動く。サービスのコアには画面が無い）で、実行フォルダの `screenshots/` に保存する。コアはそのフォルダを返すだけ。

- ファイル名（拡張子を除いた部分が `key`）: `<プレー日時 UTC yyyyMMddTHHmmssZ>_<曲ID 5桁>_<譜面>`（例 `20261015T155011Z_23083_DPA`）。日時・曲・譜面は `/api/plays` の `timestamp`・`songid`・`diff` と同じなので、プレーと1対1で結びつく。プレーが記録されなかったときは `<撮った日時>_unrecorded`
- `GET /api/screenshots`: `[{key, file, size, savedAt}]`
- `GET /api/screenshot?key=...`: 画像そのもの。`key` の形が違えば `400`、無ければ `404`
- 拡張子は `.png` / `.jpg` / `.jpeg` で、同じ `key` が複数あればこの順に先のものを使う（クライアントの手元の `screenshots/` も同じ順）。`Content-Type` は拡張子ではなく中身（先頭のシグネチャ）で決めるので、外部で PNG ⇔ JPEG に変換して拡張子が合わなくても返せる。チェックサムなどは持たず、ファイル名だけで結びつける

### `GET /api/rivals` / `GET /api/rivals/scores`

- rivals: `[{side, index, infinitasId, djName, prefId, gradeSp, gradeDp, charts}]`。`side` は `SP` / `DP`（ゲームでは SP と DP のライバルを別々に5人ずつ登録する。同じ人のこともある）、`index` は登録順（0〜）。`prefId`（都道府県、[INFINITAS動作.md](INFINITAS動作.md) の表）と `gradeSp` / `gradeDp`（段位、-1 = なし）は推定。`charts` はスコアのある譜面数
- scores: `[{side, index, songId, difficulty, exScore, lamp}]`。プレーしていない譜面は含まない。`lamp` は `NP` 〜 `FC`（ゲームは PERFECT を保存しないので `PFC` は無い）。ミスカウントや判定の内訳は無い
- ゲームは起動時にだけライバルを取得するので、コアは ready になったときに読む（両方読めるまで、選曲画面に戻るたびに読み直す）。ライバルを変えたら INFINITAS の再起動が必要

### コアの動作設定（`/api/settings`、`coresettings.json`）

初期値は API 運用向け（`tracker.tsv` を書かない・セッション TSV は月ごと。ほかは本家と同じ）。クライアントの「コア設定」タブから変えられる。ファイルが無ければ初期値。

| キー | 値 | 初期値 | 内容 |
|---|---|---|---|
| `pollIntervalMs` | 500 / 1000 / 2000 / 3000 / 5000 | 2000 | メインループの間隔（ゲームの状態を見る間隔）。選曲画面など。プレー中は `playPollIntervalMs`、リザルト画面は `resultPollIntervalMs` を使う |
| `playPollIntervalMs` | 250 / 500 / 1000 / 2000 / 3000 / 5000 | 2000 | プレー中のメインループの間隔。リザルト画面になったことに気づくまでの遅れ（リザルトの前に 2〜3 秒の読み込み画面があるので、2 秒でもスクリーンショットは間に合う） |
| `resultPollIntervalMs` | 100 / 250 / 500 / 1000 / 2000 / 3000 / 5000 | 500（本家は 2000） | リザルト画面でのメインループの間隔。読むのは状態の目印の数バイトだけ。画面を抜けたことに早く気づく（モニターの連写を早く止める） |
| `unlockCheckSeconds` | -1 / 0 / 1〜3600 | 0 | 選曲画面で解禁状態を読み直す頻度。0 = 毎回、-1 = 選曲画面に戻ったときだけ、N = 最短 N 秒ごと（選曲画面に戻ったときは必ず読む） |
| `trackerTsv` | `always` / `onChange` / `never` | `never`（本家は `always`） | 選曲画面での `tracker.tsv` の書き出し。`onChange` はプレーの記録後・解禁状態の変化時・アタッチ後だけ書く |
| `processPriority` | `normal` / `belowNormal` / `idle` | `normal` | Reflux 自身のプロセス優先度 |
| `sessionFile` | `perStart` / `daily` / `monthly` / `single` / `none` | `monthly`（本家は `perStart`） | `sessions/` のセッション TSV の分け方（`none` = 出力しない）。`perStart` = Reflux の起動ごと（本家と同じ `Session_yyyy_MM_dd_HH_mm_ss.tsv`）、`daily` = `Session_yyyy_MM_dd.tsv`、`monthly` = `Session_yyyy_MM.tsv`、`single` = `Session_all.tsv`。セッション JSON は変わらない |

```sh
curl -X PUT "$A/settings" -d '{"unlockCheckSeconds":-1,"trackerTsv":"onChange","processPriority":"belowNormal"}'
```

### `GET /api/masters`（例外: リクエストのたびにゲームの exe の表を読む）

マスター・定数の表。INFINITAS の更新で番号や並びが変わることがあるので、毎回ゲームから読み直す（読むのは exe の中の定数表だけで、数 KB）。

- クエリ `tables`: 返す表の key をカンマ区切りで（例 `?tables=song,chart`）。`all` で全部。省略すると下表の定数の表だけ（曲・譜面・シリーズ・楽曲パックは含めない。クライアントの「マスター」タブはこれを使い、曲などは手元の DB から出す）。知らない key は `400`（`unknown_table`）
- ゲームの exe を読むのは、`source` が `exe` の表を1つでも頼んだときだけ。`?tables=song,chart,series,pack` ならメモリは読まない

- `{gameVersion, readAt, masters: [{key, name, source, address, note, warning, readAt, gameVersion, cached, columns: [{key, header, numeric}], rows: [{<列のkey>: 値}]}]}`
- 一番外の `gameVersion` / `readAt` は、今つながっているゲームのバージョン（未接続なら null）とレスポンスを作った時刻。表ごとの `readAt` / `gameVersion` は、その表の中身をゲームから読んだ時刻（UTC）とそのときのゲームのバージョン（Reflux 側の定義の表は null）
- `cached`: `true` なら今のゲームから読んだものではなく、最後に読めた表（実行フォルダの `masters-cache.json`）を返している。コアが `ready` でないとき（INFINITAS を閉じた後・起動前・更新でずれているとき）はいつもこれ。キャッシュの `readAt` は、その中身を最初に読んだ時刻（中身が変わらない間は書き直さないので、最後に確かめた時刻ではない）。キャッシュが無ければ `cached` は `false` で、その時点で読めた内容（行が空なら `warning` 付き）
- `masters-cache.json` には、ゲームから警告なしに読めた表（exe の表と、曲・譜面・シリーズ・楽曲パック）を、中身が変わったときだけ書く（毎回は書かない）。ほとんどは INFINITAS の更新でしか変わらない。譜面の表は全国クリアレートを含むので、クリアレートが変わると書き直す（2MB 前後）
- 消された・外から書き換えられた（壊れた）ときは、INFINITAS が起動していれば次の `/api/masters` で作り直す（ファイルの大きさと更新時刻で気づく。読めない表は読み込み時に捨てる）
- 前回の正常なキャッシュを壊さないよう、次のときは書かない（表はそのとき読めた内容で返し、キャッシュは前のまま）
  - コアが `ready` でない（オフセット不一致 `offsetMismatch`、曲リストの形が合わない `layoutMismatch`、読み込み中など。INFINITAS の更新直後）
  - その表に `warning` がある（exe の表に知らない画像名がある＝表が動いたか中身が変わった）
  - 曲の表: 曲の読みが読めない（`dataWarnings` に `readings`）。キャッシュより曲数が1割以上少ない（読み方がおかしい可能性。コンソールに出す）
  - 譜面の表: 全国クリアレートがまだ無い・読めない（`clearRates`）
- `source`: `exe`（ゲームの exe の中の表から今読んだもの）／`reflux`（Reflux 側の定義。ゲームに表が無いか、まだ見つけていないもの）
- `warning`: ゲームに未接続で読めないとき、または知らない画像名があるとき（表の位置や並びが変わった可能性）
- 表示名を文字で持っているのは都道府県だけで、ほかは画像名しか無い。名前は画像名から Reflux が付けている（`Api/Masters.cs`）

| key | 表 | source | 主な列 |
|---|---|---|---|
| `mode` | SP / DP | reflux | ID・名前 |
| `playSide` | プレー側 | exe | ID・名前・Reflux の PlayType・画像名 |
| `difficulty` | 譜面の種類 | exe | ID・名前（BEGINNER〜LEGGENDARIA）・Reflux の難易度・画像名 |
| `lamp` | クリアランプ | exe | ID・名前・Reflux の Lamp・DJ POINT 補正（%）・画像名。PERFECT（Reflux のみ）の行を足してある |
| `djLevel` | DJ LEVEL | reflux | ID・名前・条件・DJ POINT 補正（%） |
| `grade` | 段位 | exe | ID（0 = 七級 … 18 = 皆伝）・名前・画像名 |
| `pref` | 都道府県・エリア | exe | ID・名前（ゲームの中の文字列）・画像名・表の番号 |
| `graphTarget` | スコアグラフのターゲット | exe | ID・名前・画像名 |
| `folder` | 選曲画面のフォルダ | exe | No・名前（推定）・種別・画像名・ボイス番号（170件） |
| `unlockType` | 解禁種別 | reflux | ID・名前・内容 |
| `option` | オプション | reflux | 種類・layout の項目・値・名前 |

`tables` で頼んだときだけ返す表（`source` は `reflux`。ゲームのメモリは読まず、コアが持っている曲・譜面・クリアレートのスナップショットから組み立てる。`Api/MasterTables.cs`）:

| key | 表 | 主な列 |
|---|---|---|
| `song` | 曲 | ID・曲名・読み・英字タイトル・アーティスト・読み・ジャンル・シリーズ番号・登場作品・解禁種別・ラベル・楽曲パック（商品ID・名前）・表示（`listed`）・譜面のレベル（列の key は `spb`〜`dpl`、譜面が無ければ null） |
| `chart` | 譜面 | 曲ID・曲名・難易度・譜面の種類・レベル・ノーツ数・BPM・全国クリアレート（%、無ければ null） |
| `series` | シリーズ（登場作品） | ID（バージョンフォルダ番号）・名前・曲数 |
| `pack` | 楽曲パック | No（選曲画面の並び）・商品ID・名前・フォルダの画像名・曲数・ラベル。名前の無い商品ID は Reflux がまだ知らない新しいパック |

- クライアントのマスタータブの同名の表と同じ内容（クライアントは DB の JOIN で作っている）。違いは、シリーズ名がクライアントでは DB で変えられること、クライアントの曲の表には楽曲パックの名前の列が無いこと
- コアが `ready` のときだけ今の内容を組み立てて返す。それ以外はキャッシュ（`cached: true`）。キャッシュも無く曲データも無ければ行が空で `warning` が付く
- 負荷: ゲームのメモリは読まない。曲・クリアレートが変わったとき（`songsRevision` / `clearRatesRevision`）の最初のリクエストで表を組み立てて JSON にし、以降はそれを返すだけ。大きさは 2026111100（1899 曲・11719 譜面）で曲 約 0.9MB・譜面 約 1.8MB・シリーズと楽曲パック 10KB、`all` で約 2.4MB。ゲームPCで最初の1回が約 0.5 秒、以降は 0.1〜0.3 秒（ほとんど LAN の転送）。プレー中・リザルト画面を避けたいなら、`/api/status` の `gameState` が `songSelect` のときに呼び、`songsRevision` / `clearRatesRevision` が変わったときだけ取り直す

## ゲーム更新対応用のエンドポイント（リクエストのたびにメモリを読む／ファイルを書く）

`[Api]` が有効ならいつでも使える。メモリは読み取り専用で開く（書き込みは一切しない）。

（API 全体）リクエストはスレッドプールで並行して処理する（大きな応答を受け取るのが遅いクライアントがいても、ほかのリクエストは待たされない）。PUT / POST は1つずつ。
**API キーを設定していないと、LAN 内の誰でもオフセットや構造体の配置を書き換え、メモリを読める**ので、LAN 公開するなら `apikey` を設定すること。

### オフセット

| メソッド・パス | 内容 |
|---|---|
| `GET /api/offsets` | `offsets.txt` の値（`file`）、使用中の値（`inUse`）、ゲームのバージョン、bm2dx のベースアドレス・サイズ（`module`） |
| `PUT /api/offsets` | `offsets.txt` を書き換えて再アタッチ |

`PUT /api/offsets` の本文（どれも省略可。省略したオフセットは今の値のまま）:

```json
{ "version": "P2D:J:B:A:2026101400", "offsets": { "songList": "0x1431D5880" }, "reattach": true }
```

- `version` を省略すると、検出したゲームのバージョンが入る
- 書き換える前に、元のファイルを `archive/<バージョン>_<日時>.txt` にコピーする
- バージョン不一致で `offsetMismatch` になっているときは、これで書き換えると再アタッチして先へ進む（コンソールで Enter を押すと本家の対話式探索）

### メモリ

| メソッド・パス | 内容 |
|---|---|
| `GET /api/memory/info` | bm2dx のベースアドレス・サイズと、使用中のオフセット |
| `GET /api/memory?address=…&length=…&format=hex\|bin&encoding=sjis` | ダンプ。`length` の既定は 0x100、最大 16MB。既定は16進＋ASCII のテキスト、`encoding=sjis` で文字欄を Shift-JIS 表示、`format=bin` で生バイト。ヘッダー `X-Address`（実際のアドレス）、`X-Unreadable-Bytes`（読めずに 0 で埋めたバイト数） |
| `GET /api/memory/search?start=…&length=…&<パターン>&max=…` | 検索。`start` の既定は `base`、`length` の既定は 0x5000000（最大 1GB）、`max` は既定 100・最大 1000。パターンは `hex=0662000003000000`、`text=5.1.1.`（`encoding=sjis`（既定）/`utf16`/`utf8`/`ascii`）、`int32=25094`、`int64=…` のどれか1つ。見つかったアドレスを `matches` に返す |

### 構造体の配置（`layout.json`）

| メソッド・パス | 内容 |
|---|---|
| `GET /api/layout` | 使用中の配置（`layout`）、`layout.json` があるか（`file`）、組み込みの初期値（`defaults`） |
| `PUT /api/layout` | `layout.json` を書き換えて再アタッチ。本文に書いた項目だけ変わる。`"reset": true` で初期値から、`"reattach": false` で再アタッチしない。元のファイルは `archive/layout_<日時>.json` にコピーする |
| `GET /api/layout/preview?struct=…&start=…&count=…` | 使用中の配置で、その構造体をメモリから読んだ結果 |
| `POST /api/layout/preview?struct=…` | 本文（`PUT` と同じ形式）の配置で試し読みする。**保存しない** |

- `struct` は `song`（既定）、`unlock`、`scoreMap`、`playData`、`judge`、`settings`、`currentSong`、`gameState`、`clearRate`、`rival`、`reading`（曲の読み。`songs=02000,32044` でその曲だけ）
- `start` は読み始めるアドレス式。既定は構造体に対応するオフセット（`song` なら `songList`）
- `count` は先頭から返す件数（`song` / `unlock` / `scoreMap`。既定 5、最大 500）
- `song` / `unlock` / `scoreMap` は全件を読んで `summary` にチェック結果を返す（`unlock` は楽曲パックの数 `packItemIds`（2026101400 で 46）と、Sub 以外なのに商品IDがある曲の数 `packSongsNotSub`（0 のはず）も返す）。`summary.looksValid` が `true` なら配置はたぶん正しい。
  `song` のチェックはメインループも使っていて、合格しないと曲データの読み込みを先へ進めない（`status.message` にチェック結果が出る）
- `playData` / `judge` / `currentSong` は各項目の今の値、`settings` は P1 側と P2 側の値、`gameState` はプレー中マーカーと選曲画面マーカーの位置と値を返す
- `clearRate` は SP・DP の木をたどった結果。`songs=12023,25094` でその曲の値だけ返す
- `rival` は SP・DP のライバル一覧（`songs=25094` でその曲のライバルのスコアも）

`layout.json` の形式と初期値（INFINITAS `P2D:J:B:A:2026101400` 用）。どの構造体も、オフセット（`offsets.txt`）が指す位置からの相対位置:

```json
{
  "song": {
    "entrySize": "0x730",
    "title":  { "offset": "0x0",   "length": "0x100", "encoding": "utf16" },
    "title2": { "offset": "0x100", "length": "0x40",  "encoding": "sjis" },
    "genre":  { "offset": "0x140", "length": "0x80",  "encoding": "utf16" },
    "artist": { "offset": "0x1C0", "length": "0x100", "encoding": "utf16" },
    "folder": "0x2D8", "levels": "0x2E0", "bpm": "0x2F0", "notes": "0x370", "id": "0x5B0"
  },
  "unlock": { "entrySize": "0x20", "id": "0x0", "type": "0x4", "unlocks": "0x8", "packItemId": { "offset": "0xC", "length": "0x14", "encoding": "ascii" } },
  "scoreMap": {
    "nullNode": "-0x10", "bucketsStart": "0x0", "bucketsEnd": "0x8",
    "nodeSize": "0x40", "next": "0x0", "prev": "0x8", "difficulty": "0x10", "song": "0x14",
    "playType": "0x18", "score": "0x20", "missCount": "0x24", "clearCount": "0x28", "playCount": "0x2C", "lamp": "0x30"
  },
  "playData": { "song": "0x0", "difficulty": "0x4", "exScore": "0x8", "lamp": "0x18" },
  "judge": {
    "p1Pgreat": "0x0", "p1Great": "0x4", "p1Good": "0x8", "p1Bad": "0xC", "p1Poor": "0x10",
    "p2Pgreat": "0x14", "p2Great": "0x18", "p2Good": "0x1C", "p2Bad": "0x20", "p2Poor": "0x24",
    "p1ComboBreak": "0x28", "p2ComboBreak": "0x2C", "p1Fast": "0x30", "p2Fast": "0x34",
    "p1Slow": "0x38", "p2Slow": "0x3C", "p1MeasureEnd": "0x40", "p2MeasureEnd": "0x44",
    "p1Gauge": "0x134", "p2Gauge": "0x138"
  },
  "settings": {
    "style": "0x0", "gauge": "0x4", "assist": "0x8", "flip": "0xC", "range": "0x10",
    "style2": "0x14", "battle": "0x28", "hRan": "0x2C", "p2Offset": "0x48"
  },
  "currentSong": { "song": "0x0", "difficulty": "0x4" },
  "gameState": { "markerSearchStart": "0x90", "markerSearchLength": "0x60", "markerFromMagic": "0x18", "songSelectMarker": "-0x18" },
  "clearRate": {
    "spMap": "0x25D1FA0", "dpMap": "0x25D1FA8", "objectHead": "0x8", "objectSize": "0x10",
    "nodeSize": "0x30", "left": "0x0", "parent": "0x8", "right": "0x10", "isNil": "0x19", "song": "0x1C", "difficulty": "0x20", "rate": "0x24"
  },
  "rival": { "spData": "0x25D1FC0", "dpData": "0x25D1FC8", "begin": "0x0", "end": "0x8" },
  "player": { "infinitasId": "0x25D1ED4", "profile": "0x699D40", "profileDjName": "0xE" },
  "reading": { "vector": "0x2909728", "entrySize": "0x88", "id": "0x0", "artistReading": "0x8", "titleReading": "0x28", "searchKey": "0x48", "artistKey": "0x68",
    "stringLength": "0x10", "stringCapacity": "0x18" },
  "masters": { "difficulty": "0x527AB8", "difficultyCount": "0x5", "lamp": "0x5A9FB0", "lampCount": "0x8", "grade": "0x52E7F0", "gradeCount": "0x13",
    "playSide": "0x52E888", "playSideCount": "0x3", "graphTarget": "0x52E8A0", "graphTargetCount": "0x14",
    "pref": "0x6EB968", "prefCount": "0x36", "prefEntrySize": "0x18", "prefName": "0x0", "prefImage": "0x8", "prefId": "0x10",
    "folder": "0x6954C0", "folderCount": "0xAA", "folderEntrySize": "0x38", "folderKind": "0x0", "folderGroupImage": "0x10",
    "folderImage": "0x18", "folderImage2": "0x20", "folderImage3": "0x28", "folderVoice": "0x30" }
}
```

| 構造体 | 補足 |
|---|---|
| `song` | 曲リストの1曲分。`entrySize` ごとに次の曲が並び、先頭4バイトが 0 の曲で終わる。文字列は最初の NUL まで、`encoding` は `utf16` / `sjis` / `utf8`。`folder` は1バイト、`levels` は10バイト、`bpm` は (最大, 最小) の int32 が10組、`notes` は int32 が10個（どれも SPB〜DPL の順） |
| `unlock` | 解禁データの1曲分。曲ID が 0 の要素で終わる。`unlocks` は譜面ごとのビット（bit0 = SPB 〜 bit9 = DPL） |
| `reading` | 曲の読みの表。bm2dx ベース + `vector` に `{begin, end}`、その間に `entrySize` ごとに曲リストと同じ順で並ぶ（読みの無い曲は曲ID 0）。文字列は MSVC の `std::string`（`stringCapacity` が 15 を超えると先頭8バイトがヒープへのポインタ）。`searchKey` / `artistKey` は返さない |
| `scoreMap` | 自己ベストのハッシュマップ。`nullNode` / `bucketsStart` / `bucketsEnd` は `dataMap` からの位置（8バイトのポインタ）、残りはノード内の位置。`difficulty` は 0〜4（B〜L）、`playType` は 0 SP / 1 DP |
| `playData` | リザルトのプレーデータ。`difficulty` は 0〜9（SPB〜DPL） |
| `judge` | 判定数。P1 側と P2 側が別々で、Reflux が合算する |
| `settings` | オプション。P2 側の値は `p2Offset` 先にある |
| `clearRate` | 全国クリアレート。`spMap` / `dpMap` は **bm2dx のベースアドレスからの位置**で、そこにオブジェクト（仮想関数表 `0x1404E5D70`, head ノード, 件数）へのポインタがある。中身は MSVC の `std::map`（赤黒木）で、head の parent が根。ノードは left / parent / right、`isNil`（head なら 0 以外）、曲ID、譜面 0〜4（B〜L）、クリアレート × 100（無ければ -1） |
| `rival` | ライバル。`spData` / `dpData` は **bm2dx のベースアドレスからの位置**で、そこに `GetRivalInfo` の応答（展開した KBin）を入れたバッファ `{begin, end}` へのポインタがある。バッファの中から `[長さ][人数]` に続く ID（`C` + 12桁）を探し、そこからビッグエンディアンで読む（`Api/Rivals.cs` の説明を参照）。1人分は ID・DJ NAME・店舗名（長さ付き文字列）、県・SP段位・DP段位、曲ごとに 52 バイト（曲ID、`00000014`、EX ×5、`00000014`、ランプ ×5） |
| `player` | ログイン中のプレイヤー。`infinitasId` は **bm2dx のベースアドレスからの位置**で、`C` + 12桁の INFINITAS ID がある。直後はログイントークンなので、13文字より先は読まない。`profile` は bm2dx 内のプロフィール（INFINITAS ID の写し 13 文字 + NUL、`profileDjName` の位置に DJ NAME（ASCII、0 埋め））。2026111100 で見つけた（ゲームのバージョン文字列の後ろ） |
| `masters` | `/api/masters` の exe の表。位置は **bm2dx のベースアドレスからの位置**。`difficulty` など画像名の表は8バイトのポインタが件数分並ぶ。`pref` / `folder` は1件 `*EntrySize` バイトで、各項目の位置を指定する。探し方は `docs/INFINITAS動作.md` の「定数」。`GET /api/layout/preview?struct=masters` で試せる |
| `gameState` | `judgeData + markerSearchStart` から `markerSearchLength` バイトの中で、10000 を超える値が8バイト間隔で3つ並ぶ所を探し、2つ目から `markerFromMagic` 先をプレー中マーカー（0 以外でプレー中）にする。選曲画面マーカーは `playSettings + songSelectMarker`（1 で選曲画面。プレー後にだけ見る） |

## ゲーム更新時の手順（例）

1. `GET /api/status` で `attachState` が `offsetMismatch` になっていることと、`gameVersion` を確認する
2. `GET /api/memory/search` で各オフセットを探す。目印は本家の `Reflux/docs/offset finding.txt` と `OffsetSearcher.cs` と同じ:
   - songList: 先頭の曲 `5.1.1.`（2026101400 以降は英字タイトル欄で見つかるので、そこから 0x100 引く）
   - unlockData: int32 の 1000, 1, 462 の並び
   - dataMap: `FF7F0000 00000000` の 0x18 手前
   - playData / currentSong / judgeData: Sleepless Days SPA（曲ID 25094, 譜面 3）をプレーして、曲ID・譜面・EX スコアや判定数で探す
   - playSettings: オプションを2通りに変えて、値の並びで探す
3. `PUT /api/offsets` で書き込む（自動で再アタッチ）
4. `status.message` に曲リストのチェック結果が出て `loading` のまま進まなければ、構造体が変わっている。
   `GET /api/memory?address=songList&length=0x1000` などで中身を見て、`POST /api/layout/preview` で配置を試し、`looksValid: true` になったら同じ本文で `PUT /api/layout`
5. ほかの構造体も `GET /api/layout/preview?struct=…` で値を確かめる（プレーしてリザルト画面で `playData` / `judge` / `settings` を見るなど）
6. 決まった値は、リポジトリの `Reflux/offsets.txt` と `Api/Layout.cs` の初期値にも反映してコミットする

```sh
A=http://localhost:18790/api
curl "$A/status"
curl "$A/memory/search?text=5.1.1.&start=songList-0x2000000&length=0x4000000"
curl -X PUT "$A/offsets" -d '{"offsets":{"songList":"0x1431D5880"}}'
curl "$A/memory?address=songList%2B0x730&length=0x100&encoding=sjis"
curl -X POST "$A/layout/preview?count=3" -d '{"song":{"entrySize":"0x730","id":"0x5B0"}}'
curl -X PUT "$A/layout" -d '{"song":{"entrySize":"0x730","id":"0x5B0"}}'
curl "$A/layout/preview?struct=judge"
```
