# Rimworld Statistics — Steam Workshop公開準備（Androidのみ）

**PCから初回投稿する場合は [PC向け公開手順](pc-publishing.md) を優先してください。** この資料は過去のAndroidによる生成説明を保存したものです。

**対象**: GitHub正式版 [v1.3.0](https://github.com/pengin0503/Rimworld-Statistics/releases/tag/v1.3.0)、RimWorld 1.7、Steam App ID `294100`、Package ID `pengin0503.rimworldstatistics`。GitHubでの準備はSteamへの公開ではありません。

## 1. 現在の状態

- 配布ZIPの中身は `About/About.xml`、`1.7/Assemblies/` の3 DLL、日英翻訳。Steamのプレビュー画像は既存v1.3.0のimmutableなZIPに含まれていない。
- [`tools/generate_workshop_preview.py`](../../tools/generate_workshop_preview.py) が独自の640×360 PNGを生成。著作物スクリーンショットを流用しない。1 MB未満を検査。
- [`tools/prepare_workshop.py`](../../tools/prepare_workshop.py) が正式版ZIPのSHA256、全ファイル名・XML・Harmony依存を照合し、`RimworldStatistics/About/Preview.png`を追加した専用フォルダーを作る。**Steamへ通信しない**。
- [`workshop-preflight.yml`](../../.github/workflows/workshop-preflight.yml) は `workflow_dispatch` に加え、関連ファイルのPR/`main` pushでも起動する。GitHub releaseから同じバージョンをダウンロードし、Preview作成、ZIP整合性検証、配布候補ZIPをActions Artifactに保存する。**Steamログインもアップロードもない**。
- Workshopページ向け日英BBCode: [日本語](listing-ja.bbcode)・[English](listing-en.bbcode)。Workshop IDは未発行。初回公開操作も未実施。

## 2. AndroidからGitHub Actionsで準備ZIPを作る

1. Androidのブラウザで `https://github.com/pengin0503/Rimworld-Statistics/actions` にアクセスしてログイン。表示されなければブラウザの**PC版サイト**表示にする。
2. **Prepare Steam Workshop candidate (NO UPLOAD)** を選び、`Run workflow` で**main**を指定して実行。
3. 成功した実行の **Artifacts** から `RimworldStatistics-Workshop-Preflight-v1.3.0` をダウンロード。
4. Artifact内の `RimworldStatistics-Workshop-Candidate-v1.3.0.zip` は配布候補で、`RimworldStatistics/` フォルダーがルートに1つある。`workshop.vdf` はActionsランナー上の絶対パスを含む**サンプル**であり、ダウンロードしたAndroid端末で直接再実行できない。
5. `RimworldStatistics-Workshop-Candidate-v1.3.0.sha256`で整合性を検証する。v1.3.0の既存Releaseは一切上書きされない。

## 3. Androidから実際にWorkshopへ投稿できるか

**技術的な候補**はAndroid端末でGitHub Actionsを起動し、Ubuntu x86_64ランナー上でValve公式SteamCMDを使用する方法。Steamworksは`workshop_build_item`とVDFを公開している。Steam公式の方法ではアカウント資格情報でログインする必要がある。

**未検証の障害**: Steam Guardの初回ログイン・モバイル確認が非対話の短命なGitHub Actions環境で継続/承認できるか。RimWorldを所有しているSteamアカウントのWorkshopアップロード権限や、必要に応じたワークショップの追加情報・規約同意も未確認。従って**「Androidだけで絶対公開できる」とは断定しない**。

将来クラウドアップローダーを実装する場合:
- ユーザーがAndroidブラウザからリポジトリの**GitHub Actions secrets**に必要な認証情報を自分で設定する。**Issue、PR、コメント、チャット、通常ファイル、ArtifactsにSteamパスワード・Steam Guard seed/code・sessionを絶対保存しない**。Steam Mobileへの確認が必要なら本人が操作する。
- 初回ID=0で投稿すると**新しいWorkshopアイテム**が作られ得る。誤投稿・重複投稿を防ぐため、初回発行には追加の明示的承認を要求し、`visibility=2`（private）から開始。投稿後の`publishedfileid`を取得し、更新時はそのIDを必須にする。
- SteamCMDのログを無加工で公開したり、Actions Artifactへアップロードしない。SteamworksのWorkshop利用規約に同意が必要なら投稿者本人がWebから確認する。第三者Marketplace Actionに認証情報を渡す実装を避ける。短命ランナーの認証と2FAの信頼性を確かめてから投稿を許可。
- 現在は**このログイン・SteamCMD実行ワークフローを用意していない**。Androidから公開実行が成功した実績もない。

公式：[Steamworks Workshop Implementation Guide](https://partner.steamgames.com/doc/features/workshop/implementation)、[RimWorld Wiki — Uploading](https://rimworldwiki.com/wiki/Modding_Tutorials)、[Steam Guard](https://help.steampowered.com/en/faqs/view/06B0-26E6-2CF8-254C)。

## 4. 初回公開前の人間の確認事項

- SteamでRimWorldを所有し、投稿可能なアカウントであること。
- `About/About.xml` の1.7/Harmony依存、日英翻訳、ZIPルート、PNGサイズが妥当であること。
- [日本語](listing-ja.bbcode)と[English](listing-en.bbcode)を続けて掲載文にコピーし、両言語で案内する。Steamページの必須アイテム欄にHarmonyを指定し、ゲーム/バージョンのタグが必要ならページから設定する（SteamCMDだけでタグの自動設定を保証しない）。
- **発行後にWorkshopのダウンロード内容と表示を目視確認**する。画像のサムネイルと説明文だけを確認してMODが実機で正常動作したとは言わない。実ゲーム検証は別の作業とする。
- Steam上でプライベート投稿できた場合、IDを保全して後続の更新で再利用。`PublishedFileId.txt`を第三者のIDから流用しない。

## 5. 安全な中断条件と次の作業

このリポジトリ変更は**掲載文・プレビュー・パッケージ準備まで**。Steamへの公開操作は行わない。Android限定アップロードを実装する次の作業では、SteamCMD認証の技術検証、本人の明示的な公開承認、秘密情報の保護、誤投稿防止、公開後のread-backを必須とする。続きの作業向けプロンプトは [next-task-prompt.md](next-task-prompt.md)。
