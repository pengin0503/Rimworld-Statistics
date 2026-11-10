# 次の作業へ引き継ぐプロンプト

```text
@Superpowers @GitHub

pengin0503/Rimworld-Statistics をSteam WorkshopへAndroid端末だけで投稿できる状態にしてください。ただし、Steamへの投稿そのものは本人の明示的な最終許可があるまで絶対に行わないでください。

対象リポジトリ:
https://github.com/pengin0503/Rimworld-Statistics
対象正式版: v1.3.0（公開済みリリースとタグは変更しない）
RimWorld Steam App ID: 294100
MOD Package ID: pengin0503.rimworldstatistics
依存MOD: Harmony (2009463077)

現状の成果物（mainにあるか先に確認）:
- docs/workshop/listing-ja.bbcode
- docs/workshop/listing-en.bbcode
- docs/workshop/android-publishing.md
- tools/generate_workshop_preview.py
- tools/prepare_workshop.py
- .github/workflows/workshop-preflight.yml
- Workshop-preflightは正式v1.3.0 ZIPとSHA256を取得し、オリジナル640x360 Preview.png付きの投稿候補を作るがSteamへの送信はしない。

要件:
1. まずmainと上記ファイル、workflow実行状況、CIと正式Releaseの整合性を確認。未検証ならAndroidから実行できるworkshop-preflightを成功まで調査・修正する。
2. ユーザーのAndroidからGitHub Actionsを起動するだけで、GitHub Actions Linux x86_64上のSteamCMD経由でWorkshopへ投稿できるかをSteamworks公式ドキュメントとSteam Guardの実際の認証方式に基づいて検証する。Android ARM端末上でRimWorldやWindows版Steamを動作させる前提は禁止。
3. アップロード自体は実行せず、セキュアなworkflowまたは実行手順を完成させる。Steam資格情報・2FA seed・コード・セッショントークンをGitHubのソース/PR/ログ/Artifact/チャットに出さない。本人によるGitHub Actions secretsの登録/Steam Mobile承認が必要なら手順を提示する。第三者の非検証ActionへSteam認証情報を渡さない。
4. 初回はpublishedfileid=0、visibility=private、本人の別途明示的な確認なしで公開しない。既存アイテム更新にはWorkshop IDを要求し、二重登録を防止。VDFのContentFolder/PreviewFile/Steam App ID、掲載文、Harmony Required Item/カテゴリ設定を確認する。
5. 公開後にWorkshop IDを保存し、同じItemをAndroidから更新する安全な方法とSteam Workshopのread-back確認手順まで整備する。
6. Steam GuardやSteamCMDの無人ログインが実際には通らない場合、公開は中断し、原因・代替手段・本人が次に実施する最小限の操作を報告する。成功を偽らない。
7. 準備コードの変更はPRにまとめて静的検査・レビュー・マージ。正式Release v1.3.0を上書きしない。Rimworld-Statisticsの生涯累計統計P0/P1/P2（Issue #82）には触れない。

完成条件: Androidから実行する公開準備と安全なアップロード候補が文書・CIで再現可能。Steamへの投稿は本人の明示的な指示を受けるまで実行しない。
```
