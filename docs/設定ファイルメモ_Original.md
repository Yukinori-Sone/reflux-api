# 設定ファイルメモ（Original: 本家版）

本家 olji/Reflux の `config.ini` と、Reflux が生成・更新するファイルのまとめ。

- **基準**: 本家 master `0326038`（2026-05-04、Reflux 1.16.5）。コードを読んで確認した内容
- **メンテ方針**: 本家の更新を取り込んで、設定項目やファイル出力が変わったときだけ更新する
- 独自版（`MyCustom` ブランチ）で追加・変更した部分は [設定ファイルメモ_Custom.md](設定ファイルメモ_Custom.md) を参照

目次: 共通の注意 / config.ini / 生成・更新ファイル一覧

## 共通の注意

- `config.ini` は **exe と同じフォルダ** に置く（実行フォルダ基準の相対パスで読む）
- **ビルドすると出力先の `config.ini` は `Reflux/config.ini`（リポジトリの原本）で上書きされる**
  （csproj で `CopyToOutputDirectory=Always`）。
  手元で試す設定は配布先フォルダ側で書き換えるか、原本を変えるならコミットに含めるか意識すること
- キーが無い／値が読めない場合
  - true/false の項目 → `false` 扱い（コンソールに `Couldn't read key ...` と表示される）
  - 文字列の項目 → 空文字扱い
- 値の true/false、キー名は大文字小文字を区別しない（`True` / `true` どちらも可）

## config.ini

「初期値」はリポジトリ同梱の `config.ini` の値。

### [Update] 更新チェック

| キー | 初期値 | 意味 |
|---|---|---|
| `updateFiles` | `true` | ゲームのバージョンと `offsets.txt` が合わないとき、`updateserver` から新しい `offsets.txt` を取りに行くか。`false` だと取りに行かず、すぐ対話式のオフセット探索モードに入る |
| `updateserver` | `https://raw.githubusercontent.com/olji/Reflux/master/Reflux/` | サポートファイル（`offsets.txt` 等）の取得元 URL。本家リポジトリの master を直接見ている |

補足（設定に関係なく行われる通信）:

- 起動時に毎回 GitHub の本家リリースページを見て、新しいバージョンがあれば赤字で表示する
- アタッチのたびに `encodingfixes.txt` / `customtypes.txt` を `updateserver` と比べ、
  サーバー側の日付（1行目）が新しければ置き換える。**`updateFiles = false` でもこちらは止まらない**

### [Record] 記録の保存先

| キー | 初期値 | 意味 |
|---|---|---|
| `saveremote` | `false` | プレー結果・解禁状況を Reflux Server（`[RemoteRecord]`）へ送信する |
| `savelocal` | `true` | ローカル保存。セッション TSV、プレーごとの `tracker.db` 更新、`tracker.tsv`、`unlockdb` をまとめて ON/OFF する（細かく分けられない点に注意） |
| `savejson` | `false` | セッションごとの JSON に1プレーずつ追記 |
| `savelatestjson` | `true` | 直近1プレーを `latest.json` に上書き保存 |
| `savelatesttxt` | `true` | 直近1プレーを配信向けの `latest*.txt` 群に上書き保存 |

各設定で出力されるファイルとタイミングは「生成・更新ファイル一覧」を参照。

### [RemoteRecord] Reflux Server への送信

`saveremote = true` のときだけ使う。

| キー | 初期値 | 意味 |
|---|---|---|
| `serverAddress` | `https://server.here`（ダミー） | Reflux Server（https://github.com/olji/Reflux_Server）の URL |
| `apikey` | `apikey`（ダミー） | Reflux Server 用の API キー |

### [LocalRecord] セッション TSV の列

セッション TSV に出す列を選ぶ。曲名・難易度・playtype・DJ LEVEL・ランプ・ミスカウント・日時は常に出力される。

| キー | 初期値 | true で追加される列 |
|---|---|---|
| `songinfo` | `true` | 英語曲名(title2)、BPM、アーティスト、ジャンル |
| `chartdetails` | `true` | ノート数、レベル |
| `resultdetails` | `true` | ゲージ%、EX スコア |
| `judge` | `true` | PG / GR / GD / BD / PR / コンボブレイク / FAST / SLOW |
| `settings` | `true` | 譜面オプション(style)、DP 2P側オプション(style2)、ゲージ、アシスト、レーンカバー(range) |
| `uselocaltime` | `false` | 日時列を UTC ではなくローカル時刻（日本時間）で出す |

### [Livestream] 配信向けファイル

| キー | 初期値 | 意味 |
|---|---|---|
| `showplaystate` | `true` | `playstate.txt` に状態（`menu` / `play` / `off`）を書く |
| `enablemarquee` | `true` | `marquee.txt` に表示用テキストを書く |
| `enablefullsonginfo` | `true` | プレー中の曲情報を `title.txt` などに書く |
| `marqueeidletext` | `Beatmania IIDX Infinitas` | 選曲画面中に `marquee.txt` に出す文字列 |

同梱の `SegmentDisplay 9 units / 16 units (browser source ...).html` は、`marquee.txt` を
OBS のブラウザソースで7セグ風に表示するためのもの。

### [Debug]

| キー | 初期値 | 意味 |
|---|---|---|
| `outputdb` | `false` | アタッチ時に曲リストを `songs.tsv` に出力（文字化け確認用） |

## 生成・更新ファイル一覧

### タイミングの用語

| 用語 | いつ | 回数 |
|---|---|---|
| 起動時 | Reflux を起動した直後（INF とは無関係） | Reflux 起動1回につき1回 |
| アタッチ時 | INF を見つけてから、曲リスト等の読み込みが終わるまで | INF を起動するたび（INF を再起動すると再度） |
| 選曲中 | 選曲画面にいる間 | 2秒ごと |
| プレー開始時 | 選曲画面 → プレー画面に変わったとき | 1プレーにつき1回 |
| リザルト時 | プレー → リザルト画面に変わったとき（記録できた場合のみ ※） | 1プレーにつき1回 |
| 選曲復帰時 | リザルト → 選曲画面に戻ったとき | 1プレーにつき1回 |
| INF終了時 | INF のプロセスが終了したとき | INF 終了1回につき1回 |

※ H-RAN・BATTLE 等でプレーデータが取れない場合と、クイックリトライの場合は「記録できない」扱い。
CRUD 列の「―」はその操作が無いことを表す。Reflux はファイルを削除しない（置き換え時は `archive/` へ移動）。

### 設定・サポートファイル（同梱）

| ファイル | 内容 | 作成 | 読込 | 更新 | 削除 | 関連設定 |
|---|---|---|---|---|---|---|
| `config.ini` | 設定 | 起動時に無ければ空で作成 | 起動時 | ― | ― | ― |
| `offsets.txt` | 1行目: 対応ゲームバージョン（`P2D:J:B:A:yyyymmddnn`）、以降: 各データのメモリアドレス | 同梱 | アタッチ時 | アタッチ時、バージョン不一致なら: サーバーから取得 → 合わなければ対話式探索の結果で書き換え | 置き換え時に旧版を `archive/` へ移動 | `updateFiles`、`updateserver` |
| `encodingfixes.txt` | 1行目: 日付。以降: `化けた文字列<TAB>正しい文字列` | 同梱 | アタッチ時 | アタッチ時、サーバーの日付が新しければ置き換え | 置き換え時に旧版を `archive/` へ移動 | `updateserver`（`updateFiles` は無関係） |
| `customtypes.txt` | 1行目: 日付。以降: `曲ID,ラベル`（楽曲パック `TRI3`、DJP 解禁 `800DJP` 等） | 同梱 | アタッチ時 | 同上 | 同上 | 同上 |
| `beginners.txt` | BEGINNER 譜面がある曲 ID の一覧 | 同梱 | ― | ― | ― | 現在のコードからは使われていない |

`encodingfixes.txt` / `customtypes.txt` が無いと、アタッチ時の更新チェックで落ちるので消さないこと。

### 記録データ

| ファイル | 内容 | 作成 | 読込 | 更新 | 削除 | 関連設定 |
|---|---|---|---|---|---|---|
| `sessions/`（フォルダ） | セッションファイルの置き場 | 起動時 | ― | ― | ― | `savelocal` |
| `sessions/Session_起動日時.tsv` | 1行1プレーの記録。列は `[LocalRecord]` で決まる | アタッチ時（ヘッダー行のみ） **※1** | ― | リザルト時に1行追記 | ― | `savelocal`、`[LocalRecord]` |
| `sessions/Session_起動日時.json` | プレー記録の JSON（`head` + `body` 配列） | アタッチ時（空の配列） **※1** | リザルト時 | リザルト時に全体を読み込んで1件足して書き直し | ― | `savejson` |
| `tracker.db` | 全譜面の自己ベスト（ランプ・DJ LEVEL・EX・ミスカウント）。ゲームのメモリに無い PERFECT CLEAR 等も保持。**消さないこと** | アタッチ時（無ければ） | アタッチ時 | アタッチ時: ゲーム内の自己ベストと統合して書き直し（設定に関係なく）／リザルト時: 自己ベストを更新して書き直し（`savelocal` 時のみ） | ― | `savelocal`（リザルト時の更新のみ） |
| `tracker.tsv` | 全譜面の自己ベスト・解禁状況・DJ POINT（Mina's Unlock Tracker 互換） | 初回の選曲中 | ― | **選曲中に2秒ごとに全件書き直し**、INF終了時 | ― | `savelocal` |
| `unlockdb` | 解禁状態のキャッシュ（Reflux Server との差分検出用） | 初回の選曲中 | 起動時 | 選曲中に2秒ごと、INF終了時 | ― | `saveremote` かつ `savelocal`（`savelocal = false` だと保存されない） |
| `songs.tsv` | 曲リスト（ID・曲名・英語名・アーティスト・ジャンル） | アタッチ時 | ― | アタッチ時に上書き | ― | `outputdb` |

**※1 本家の不具合**: セッションファイル名は Reflux の**起動時刻**で決まるが、ファイルの作成（空にする処理）は**アタッチのたび**に行われる。
Reflux を起動したまま INF を再起動すると、**それまでのプレー記録が消える**（同じファイル名で作り直されるため）。
INF を再起動する場合は Reflux も再起動すると安全。

### 直近プレー・配信用出力

すべて上書き保存（追記ではない）。

| ファイル | 内容 | 作成・更新タイミング | 関連設定 |
|---|---|---|---|
| `latest.json` | 直近1プレーの全項目。**`[RemoteRecord]` の `apikey` も含まれる**ので公開注意 | リザルト時 | `savelatestjson` |
| `latest.txt` | 英語曲名・DJ LEVEL・ランプ略号（`HC` 等）の3行 | リザルト時 | `savelatesttxt` |
| `latest-titleenglish.txt` | 英語曲名 | リザルト時 | `savelatesttxt` |
| `latest-grade.txt` | DJ LEVEL（`AAA` 等） | リザルト時 | `savelatesttxt` |
| `latest-lamp.txt` | ランプ（`HARD CLEAR` 等） | リザルト時 | `savelatesttxt` |
| `latest-difficulty.txt` | 難易度（`SPA` 等） | リザルト時 | `savelatesttxt` |
| `latest-difficulty-color.txt` | 難易度の色コード（N `#0FABFD` / H `#F4903C` / A `#E52B19`、その他は N と同じ） | リザルト時 | `savelatesttxt` |
| `playstate.txt` | `menu` / `play` / `off` | アタッチ時 `menu`、プレー開始時 `play`、リザルト時 `menu`、INF終了時 `off` | `showplaystate`（`enablefullsonginfo` でもアタッチ時・選曲復帰時に `menu` を書く） |
| `marquee.txt` | 表示用テキスト | アタッチ時 `marqueeidletext`、プレー開始時 英語曲名（大文字）、リザルト時 `英語曲名 CLEAR!` / `FAIL!`、選曲復帰時 `marqueeidletext`、INF終了時 `NO SIGNAL` | `enablemarquee`、`marqueeidletext` |
| `title.txt` / `artist.txt` / `englishtitle.txt` / `genre.txt` / `level.txt` | プレー中の曲の曲名・アーティスト・英語曲名・ジャンル・レベル | アタッチ時 空、プレー開始時 値、選曲復帰時 空 | `enablefullsonginfo` |
| `folder.txt` | プレー中の曲のシリーズ番号 | プレー開始時 値、選曲復帰時 空（アタッチ時には作られない） | `enablefullsonginfo` |

※ リザルト時のファイルは、記録できなかったプレーでは書かれない（`playstate.txt` / `marquee.txt` は書かれる）。

### ログ・その他

| ファイル | 内容 | 作成 | 読込 | 更新 | 削除 | 関連設定 |
|---|---|---|---|---|---|---|
| `log.txt` | エラー（スタックトレース付き）と一部の情報 | 初回のエラー時 | ― | エラーのたびに追記（**無限に増える**） | ― | ― |
| `archive/`（フォルダ） | 置き換えられた旧ファイル: `archive/<バージョン>.txt`（offsets）、`archive/<名前>_<日付>.txt`（encodingfixes / customtypes） | 初回の置き換え時 | ― | 置き換えのたびに追加 | ― | `updateFiles`、`updateserver` |
