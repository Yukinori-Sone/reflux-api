# Reflux API Manager 仕様

Reflux API Server（Reflux カスタム版のコア）をサービスで動かすときの、タスクトレイの管理ツール。`RefluxAPIManager/`（WinForms、**net48**。.NET Framework 4.8 は Windows 10/11 に入っているので追加のインストールは不要）。
exe 1つ（`RefluxAPIManager.exe`。SharpDX・SharpZipLib を exe に埋め込み、`Program.LoadEmbeddedAssembly`）で、**Reflux.exe と同じフォルダに置く**（Reflux の配布物に入っている）。

経緯: 旧 `RefluxAPIServerMonitor.exe`（2026-10-15〜）→ 2026-11-22 にいったん別製品の Beat Shutter（別フォルダ・別リポジトリ）に分けかけたが、
撮影も Reflux API Server の機能（保存先・配信・残す区分ともコア側）と見て、管理ツールとして reflux-api に戻した（ユーザーと相談して決定）。

## 役割

1. Reflux API Server の見張り: トレイのアイコンの色、状態ウィンドウ、Windows サービスの開始・停止・再起動
2. Reflux API Server の設定: `config.ini` の `[Api]`、配信用・最新プレーのファイル出力、動作設定
3. リザルト画面のスクリーンショット（サービスのコアには画面が無いので、ユーザーのセッションで代わりに撮る）

## コアとのつながり

- コアの API（`http://localhost:<ポート>/`）で状態・設定を読み書きする。ポートと APIキーは接続のたびに `config.ini` から読む（コアが待ち受けている値）。例外としてファイルで扱うもの（同じフォルダなので）
  - `config.ini` の `[Api]`（enabled・host・port・apikey）: API で変えると接続が切れるので、ファイルを直接書いてサービスを再起動する
  - `screenshots/`（スクリーンショットを書く。コアが `/api/screenshots` で返す）と `log.txt`（状態ウィンドウに最後を出す）
- 配信用・最新プレーのファイル出力（`config.ini` の5項目）は `GET|PUT /api/outputs`、動作設定は `GET|PUT /api/settings`（どちらもその場で反映）。`/api/outputs` の無い古いコアでは変更できない旨を出す
- サービス（初期値 `RefluxAPIServer`）の状態・開始・停止は Windows のサービス制御。権限が無ければ自身を `--service start|stop|restart` で UAC 昇格して実行。`config.ini` に書き込めないときも同じく `--write-config <一時ファイル> <config.ini>` で昇格（書き先は `config.ini` という名前のファイルだけ）

## 設定（`RefluxAPIManager.json`）

exe の隣（書き込めなければ `%LOCALAPPDATA%\RefluxAPIManager`。すでにファイルがある方を使う）。`ManagerSettings.cs`。

| 項目 | 内容 |
|---|---|
| `ServiceName` | 初期値 `RefluxAPIServer`（画面には出さない） |
| `Port` / `ApiKey` | API への接続の控え。接続のたびに `config.ini` から読み、変わっていればここも書き直す。`config.ini` を読めないときだけこの値で接続する（画面に別の接続設定は持たない。ユーザー指定） |
| `AutoStartChosen` | ログオン時の起動（`HKCU\...\Run` の `RefluxAPIManager`）をユーザーが選んだか。選んでいなければ起動のたびにオン |
| `Screenshot*` | スクリーンショットの設定（下記） |

初回の起動（ファイルが無いとき）は、旧モニターの設定（`HKCU\Software\RefluxAPIServerMonitor`）を引き継ぎ、旧モニター・Beat Shutter（試作）のログオン時の起動（Run の `RefluxAPIServerMonitor` / `BeatShutter`）を消してこのツールに置き換える。

## 画面

- 画面の文言では「コア」（内部の呼び名）ではなく「Reflux API Server」、「本家」ではなく「Reflux本家」、Windows サービスの操作は「Reflux API サービスを開始／停止／再起動」と書く（ユーザー指定）
- アイコンの色で状態（緑 = 監視中、黄 = INFINITAS 待ちなど、赤 = エラー・サービス停止・API 応答なし、灰 = サービス未登録で Reflux も止まっている）
- 左クリックで状態ウィンドウ（サービス・INFINITAS・ゲームの状態、INFINITAS ID、バージョン、`config.ini` の API の設定、直近のプレー、スクリーンショット、`log.txt` の最後）
- 右クリックのメニュー: 状態、設定、Reflux API サービスの開始・停止・再起動、Reflux・スクリーンショットのフォルダ・`log.txt` を開く、ログオン時に起動、終了
- 設定ウィンドウ「Reflux API Manager の設定」は3つのタブ（普通の画面に収まるように。ユーザー指定）: 「API」= `config.ini` の `[Api]`（APIキーの生成・コピー。保存するとサービスを再起動）／「スクリーンショット」／「動作設定」= 配信用・最新プレーのファイル出力と Reflux API Server の動作設定（どちらも API）。「保存して反映」は全部のタブの変更をまとめて反映する。タブの大きさは一番大きいページに合わせる（表示の直前に測る）
- `--status` / `--settings` で起動時にそのウィンドウを開く。同じユーザーのセッションでは1つだけ（`Local\RefluxAPIManager`）
- .NET Framework の部品は UIA で種類が Pane に見えるので、UI の自動確認は BM_CLICK を送る

## リザルト画面のスクリーンショット（`ResultScreenshot.cs`・`DesktopDuplication.cs`・`PngWriter.cs`）

- 設定（設定ウィンドウで オン・オフ、撮り続ける最長時間、PNG / JPEG、PNG の圧縮（標準 = zlib 6 で 1.8MB・約1秒、最高 = 9 で 3% 小さく 5〜10 秒）、JPEG の品質（50〜100、初期値 92）。値は `RefluxAPIManager.json`）。PNG は行ごとにフィルターを選んで自前で書き出す（GDI+ の PNG は 2.8MB になっていた）。保存は連写のあとなので、圧縮に時間がかかっても撮る瞬間には影響しない。フルスクリーンの INFINITAS も撮れるよう Desktop Duplication を使う。コアがリザルト画面と判定した時点から 0.15 秒ごとに撮って 0.3 秒前のフレームと比べ、読み込み画面（黒）と抜けたあとの青い画面を除いて、左の成績パネルと中央が止まった1枚を残す（止まる前に抜けたら、それに近い1枚。クイックリトライは残さない）。調整用に `RefluxAPIManager.json` へ `"ScreenshotKeepBurst": true` を足すと、連写した全フレームを `screenshots/burst/` に残し、判定の値を `capture.log` に書く
- ファイル名 = `<プレー日時 UTC>_<曲ID>_<譜面>`（コアの `/api/latest` を待って決める。記録が来なければ `_unrecorded`）。どのリザルトを残すかはコアがプレーに付ける `resultKind` ごとに選ぶ（`ScreenshotSettings.Kinds`）
- コアの「リザルト画面」判定は実際の表示より 2〜3 秒早く（黒い読み込み画面）、抜けたあとの青い画面もしばらく含むので、連写して選ぶ（読み込み・青い画面はパネル部分の灰色の割合で見分ける。しきい値は 2026101400 の実画面で調整）
