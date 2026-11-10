# Rimworld Statistics — Windows PCからSteam Workshopへ投稿・更新する

**公開作業はユーザー本人がSteamクライアント上で実施する。** この手順書、GitHub Actions、補助スクリプトはSteamへログイン・投稿しない。既存の正式Releaseや統計コードには変更を加えない。

## 確定情報と配布元

- RimWorld Steam App ID: `294100` / 対応ゲーム本体: **RimWorld 1.7のみ**（1.5～1.7の旧セーブを部分的に読み取れることは、旧バージョン上での実行保証ではない）。
- MOD Package ID: `pengin0503.rimworldstatistics`。MODバージョンは**選択した正式Releaseの`About.xml`で検証**される（本書を作成した時点ではv1.3.0）。
- 必須MOD: Harmony / Package ID `brrainz.harmony` / Workshop ID `2009463077`。
- 原本: [GitHub正式Releases](https://github.com/pengin0503/Rimworld-Statistics/releases)。署名済み/検証済みの**公開済み安定タグvX.Y.Z**を選択する。元のRelease ZIPを再ビルドしない。現在のv1.3.0原本にはPreview.pngがないため、専用の配布候補を使う。
- 配布候補: [Prepare Steam Workshop candidate (NO UPLOAD)](https://github.com/pengin0503/Rimworld-Statistics/actions/workflows/workshop-preflight.yml) を**main**から実行。`release_tag`を空欄にするとGitHubで最新の安定正式Releaseを選択、`v1.4.0`などを指定するとそのタグだけを選択。新しい正式Release公開時も自動起動する。成功実行のArtifact名は`RimworldStatistics-Workshop-Preflight-vX.Y.Z`。プレビュー、LICENSE、ハッシュ付き`workshop-manifest.json`、日英説明文、Windows VDFスクリプトが同梱される。**Steamへは送信しない**。
- 初回掲載文: GitHub上の[日本語](listing-ja.bbcode)・[English](listing-en.bbcode)は`{{MOD_VERSION}}`などの**テンプレート**。実際の掲載には必ずArtifact内でレンダリング済みの同名BBCodeファイルを使う。新機能の説明は将来のReleaseごとに事実確認・更新する。

## A. 推奨: RimWorld標準の開発者モードから初回投稿

1. **Steam版RimWorldを所有し、投稿者本人のSteamアカウントでSteamデスクトップクライアントへログイン**する。ブラウザ上のGitHub準備が成功していてもSteam投稿権限の保証にはならない。
2. GitHub Actionsの成功した **main** のWorkshop preflight実行を開き、**Artifacts** の`RimworldStatistics-Workshop-Preflight-vX.Y.Z`を保存して展開する。内部の`selected-release-tag.txt`と`RimworldStatistics-Workshop-Candidate-vX.Y.Z.zip`のタグが一致するか確認する。古い実行のArtifactと混同しない。
3. 候補ZIPのSHA256を同梱`.sha256`と照合する。PowerShellで`(Get-FileHash -Algorithm SHA256 -LiteralPath '.\RimworldStatistics-Workshop-Candidate-vX.Y.Z.zip').Hash`を実行し、`.sha256`先頭の64桁と一致することを確認する（X/Y/Zを選択したバージョンに置換）。
4. **候補ZIPを展開**し、ルートに現れる `RimworldStatistics` フォルダーを、Steamの「RimWorld → 管理 → ローカルファイルを閲覧」で開いたゲームフォルダーの `Mods` 内へ置く（例: `D:\SteamLibrary\steamapps\common\RimWorld\Mods\RimworldStatistics`）。**`steamapps/workshop/content/294100` に置かない**。既存の同名ローカルフォルダーがある場合はまず退避し、古いファイルを混ぜて上書きしない。
5. 投稿直前に `RimworldStatistics/About/About.xml`、`About/Preview.png`（640×360・1MB未満）、`1.7/Assemblies/` 内の3 DLL、日英の `Languages`、`LICENSE` を確認する。**初回は `About/PublishedFileId.txt` が存在しないこと**を確認。別アイテムのIDをコピーしない。
6. Steam経由でRimWorldを起動し、オプションで**開発者モード**を有効にする。メインメニューの**MOD**画面で、ローカルの `Rimworld Statistics` を選択し、`Advanced`（または同等の詳細メニュー）→ **Upload on Steam / Upload to Steam Workshop** から初回投稿する。UI表記はゲーム側の版により異なり得る。初回は公開範囲を**非公開/Hidden**にできる場合は必ずそれを選ぶ。初回投稿操作によりSteam上に新しいアイテムが作成される。
7. 成功後、`About/PublishedFileId.txt` の数字と、生成された `https://steamcommunity.com/sharedfiles/filedetails/?id=<Workshop ID>` の数字が一致するか確認し、**このIDをバックアップ**する。ファイルを削除すると同じアイテムへ更新できなくなったり重複投稿するおそれがある。
8. Steamの自分のWorkshopページで説明欄を編集し、日英両方のBBCodeを掲載する。**「必須アイテムの追加/削除（Add/Remove Required Items）」でHarmony (2009463077)を指定**する。About.xmlの依存宣言だけではWorkshopページ側の必須アイテム登録完了とみなさない。カテゴリ/タグ `Mod`・`1.7`（設定可能な場合）、画像、リンク、著作者、利用規約同意を確認する。
9. 非公開状態で、アップロードされたフォルダー内容、Preview、説明文、Harmony依存、Workshop ID、ゲーム側のMOD認識を確認。**投稿と実ゲーム統計機能の完全検証は別**。公開する場合のみ、**本人が**Workshopページの公開範囲をPublicへ変更する。

初回のローカル配布フォルダーに `About/PublishedFileId.txt` が最初からある場合、**そのまま初回投稿を試みない**。投稿済みの自分のアイテムのIDか、第三者のIDかを確認する。

## B. 代替: WindowsのSteamCMDで投稿する

標準のゲーム内アップロードが利用できない場合のみ使用する。ValveのSteamCMDは `+workshop_build_item` とVDFを使用する。**Steam認証情報をGitHub、チャット、PR、ログ、Artifact、VDFへ保存しない**。SteamCMDには投稿者本人がローカルで対話的にログインし、必要に応じてSteam Guardを承認する。

配布候補を上記Aの手順で `Mods/RimworldStatistics` に展開後、**同梱された** `make_workshop_vdf.ps1` と `workshop-manifest.json` をPC上で使う（リポジトリ原本: [tools/make_workshop_vdf.ps1](https://github.com/pengin0503/Rimworld-Statistics/blob/main/tools/make_workshop_vdf.ps1)）。これは投稿も認証も実行しない。別途[Valve公式SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD)を入手する。

PowerShellで、自分のゲームパスに合わせて次を実行する:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\make_workshop_vdf.ps1" `
  -ModPath "D:\SteamLibrary\steamapps\common\RimWorld\Mods\RimworldStatistics" `
  -ManifestPath ".\workshop-manifest.json" `
  -OutputPath "$PWD\workshop-initial.vdf" -PublishedFileId 0
```

出力VDFを開き `appid=294100`、`publishedfileid=0`、`contentfolder`、`previewfile`、`visibility=2` が意図した内容か確認してから、SteamCMDを**自分のPCでのみ**実行:

```powershell
.\steamcmd.exe +login YOUR_STEAM_USERNAME +workshop_build_item "C:\absolute\path\workshop-initial.vdf" +quit
```

`YOUR_STEAM_USERNAME` は自身のSteamログイン名、VDFのパスは実際の絶対パスに変更する。**コマンド行にパスワードやSteam Guardコードを記述しない**。初回成功後にSteamCMDがVDFへ書き戻した `publishedfileid` を確認し、`RimworldStatistics/About/PublishedFileId.txt` にその数値だけを保存してバックアップする。**ID=0のVDFを再実行しない**。SteamCMDだけではRimWorld固有タグの自動登録を保証できないため、Workshopページとゲーム内の表示も確認する。

## C. 今後の同一Workshopアイテムの更新

- **同じSteamアカウント**を使用し、同じ `PublishedFileId` を維持する。今後の正式リリースが出た場合は、その版のZIPを新規に展開し、**既存の `About/PublishedFileId.txt` を新フォルダーの `About/` に復元**してからアップロードする。古いDLLを混ぜず、利用者のセーブや統計プロファイルは触らない。
- **RimWorld標準投稿の場合**: ローカルMODを選び、初回と同じアップロードメニューから「更新」操作を行う。Workshop IDが一致し、既存ページが更新されることを確認する。原本ZIPの新規展開直後はIDファイルがないため、復元せずに投稿しない。
- **SteamCMDの場合**: `About/PublishedFileId.txt` があることを確認し、配布ZIPと**同じArtifact**の`workshop-manifest.json`を`-ManifestPath`に指定する。`-PublishedFileId <取得済みの数字>`と**毎回新しい`-OutputPath`**も指定。スクリプトは保存済みIDとの不一致、ID不在、余計なファイル・ハッシュ不一致を拒否し、更新時に`visibility`・`title`・`description`を上書きしない。新バージョンにも同じコマンドを使えるが、対応ゲームバージョンや配布構成が変わる場合はCIの許可ルールと掲載内容の再確認が必要。
- 更新後は同じ `https://steamcommunity.com/sharedfiles/filedetails/?id=<Workshop ID>` で配布ファイル、対応バージョン、Harmony依存、説明文、変更履歴を確認する。存在するWorkshop IDを`0`に戻さない。

## トラブル時の確認

- 「Upload」が見当たらない: ローカル `Mods` フォルダーか、Steam版の所有/ログイン、開発者モード、既存の`PublishedFileId.txt`、RimWorld本体で選択しているMODを確認。
- Preview関連エラー: `About/Preview.png` の存在、640×360のPNGで1MB未満であることを確認。
- 投稿できない: Steamクライアントのオンライン状態、Workshop利用規約、権限、Steam Guard、ゲームログを確認。**同じ初回投稿をやみくもに繰り返す前に、自分のWorkshopページへアイテムが作成済みでないか確認**。
- CI Artifact内に以前存在した `workshop.vdf` はGitHub ActionsのLinux絶対パスを含むためPCで**利用禁止**。新しい候補はこのVDFを同梱せず、必要時だけWindows上で生成する。

出典: [RimWorld Wiki Distribution](https://rimworldwiki.com/wiki/Modding_Tutorials/Distribution)、[RimWorld Wiki Modding Tutorials](https://rimworldwiki.com/wiki/Modding_Tutorials)、[Valve Steamworks Workshop Implementation](https://partner.steamgames.com/doc/features/workshop/implementation)。
