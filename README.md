# Reflux API

A custom fork of [olji/Reflux](https://github.com/olji/Reflux) that adds an HTTP API server for reading beatmania IIDX INFINITAS play records from another PC, and a tray tool to manage it.
Details: [README-custom_ja.md](README-custom_ja.md) (Japanese only). The original README follows below.

[olji/Reflux](https://github.com/olji/Reflux) のフォークです。別の PC から INFINITAS のプレー記録を読める HTTP API サーバーと、その管理ツール（タスクトレイ）を足しています。
詳しくは [README-custom_ja.md](README-custom_ja.md)（日本語のみ）。以下は Reflux本家の README です。

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
