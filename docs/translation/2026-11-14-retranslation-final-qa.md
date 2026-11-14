# 2026-11-14 再翻訳完了・最終検査

## 対象と原則

RimWorld Statisticsの12言語、各`RimworldStatistics.xml` 1,234キー。English / Japaneseを修正し、German / French / Spanish / SpanishLatin / Portuguese / PortugueseBrazilian / Russian / ChineseSimplified / ChineseTraditional / Koreanの**10言語・英語キー251〜1234（984キー×10＝9,840訳文）**を現在のEnglishから再翻訳した。ブロック01・02の250キーは先行対応済みで、全10言語の01〜10が完了した。用語・内容は2026-11-14引き継ぎ資料§4・§5に従う。

- ブロック03＝251〜375、04＝376〜500、05＝501〜625、06＝626〜750、07＝751〜875、08＝876〜1000、09＝1001〜1125、10＝1126〜1234。
- 言語値の更新は`tools/translation/apply_patch.py`・`tools/translation/apply_chunk.sh`による。JSON下書き・用語修正JSONと進捗をGitに残した。
- `tools/translation/progress.json`は全10言語で01〜10を完了状態としている。
- English/Japaneseの保存モード名をRimWorld公式名に統一した対象は`RWS_Details_Permadeath`、`RWS_Details_NotPermadeath`、`RWS_Metric_RankByPermadeathDays`、`RWS_MetricDesc_RankByPermadeathDays`、`RWS_Metric_RankByPermadeathEndings`、`RWS_MetricDesc_RankByPermadeathEndings`。Englishは`Commitment mode`/`Reload anytime mode`、Japaneseは`コミットメントモード`/`無制限リロードモード`。計測ロジックの誤りは新たに確認されなかったため、他のEnglish説明文の内容変更は行っていない。

## 機械検査

- **PASS**：GitHub Actionsの翻訳適用ジョブで`bash tools/check_keys.sh`成功、`1234 translated keys checked in 12 language folders`、定義キーに欠落・余分・重複なし。
- **PASS**：全10言語の各1,234値とEnglish間で`{0}`等のプレースホルダー集合が一致。ブロック09の125キー×10、10の109キー×10も、作成時にキー順・非空・プレースホルダーを検査。
- **PASS**：10言語の`RWS_Details_UnknownBucket`と`(unknown)`を含む説明の内訳ラベルの一致、`none`の原文維持を確認。
- **PASS**：翻訳適用はXMLの値のみを変更し、`Source/`・テスト・`README`は変更していない。
- **判定対象**：旧版コミット`1b4d261`との値の一致・指定された誤訳の文字列検索・英語に比べ35%未満の短い値を全言語251番以降でチェック。結果は以下。

| 言語 | 旧訳と同値、原文とは異なる | そのうち説明文 | 英語の35%未満（英50字以上） | 指定語検索候補 |
|---|---:|---:|---:|---:|
| German | 81 | 1 | 0 | 0 |
| French | 90 | 1 | 0 | 0 |
| Spanish | 94 | 4 | 0 | 0 |
| SpanishLatin | 95 | 4 | 0 | 0 |
| Portuguese | 114 | 4 | 0 | 0 |
| PortugueseBrazilian | 111 | 2 | 0 | 0 |
| Russian | 50 | 0 | 0 | 0 |
| ChineseSimplified | 30 | 0 | 237 | 1 |
| ChineseTraditional | 26 | 0 | 240 | 0 |
| Korean | 18 | 0 | 0 | 5 |

旧訳との一致は主に自然な既存ラベル（「研究」「動物」「手術成功率」など）、MOD・DLC名、プレースホルダー付き短いUI文言。**85文字を超える長文説明で旧訳と一致したものは0件**。一致した説明文も意味を確認した：Germanの伝説品質製作、Frenchのひらめき種別、Spanish系の動物死亡・熱波・火山冬・日食、Portuguese系の食中毒・熱波・火山冬・日食等。元と同じこと自体は未翻訳の根拠ではない。

中国語の短文候補は237・240件。漢字圏は英語より文字数が大幅に少なくなり、記録対象・除外条件・測定単位の説明は残っているため、文字数のみで欠落とは判定しなかった。

誤訳検索のヒットは文脈付きで確認し、誤訳ではないものを除外した。特にSpanishLatinの`tenedores`は正しい「contenedores（容器）」の部分文字列、Koreanの`문자`は「방문자（訪問者）」の部分文字列、ChineseSimplifiedの`勾选`は設定UIでの「取消勾选（チェックを外す）」として正常な用法。旧誤訳に由来する独立した誤訳はこの検査では確認されていない。

## 用語と残る人手レビュー

用語集のゲーム本体全量照合は**未実施**。Cloudflare R2／ゲーム本体の公式言語ファイルをローカルに取得できなかったため、引き継ぎ資料§5の公式用語を根拠に採用した。§5にないものを公式訳確認済みとは扱わない。

**公式訳未確認・表記の再確認候補**（多くは外部MOD固有語）：VQE Ancientsのarchogen injector / injection coma、VGEのelectro-womb・mechahybridizer、VFE Piratesのwarcasket、VFE Tribalsのcornerstone、VSEのexpertise、Altered Carbon系のneural stack・sleeve、Hegemonyのvassal、Go Explore!など。MOD名・DLC名・コード上のDef名は原文を保持した。

**翻訳に不確実性が残る具体的なキー**：
- `RWS_VqeAncients_RejectedDesc`、`RWS_VqeAncients_MutationsDesc`：架空医学用語・MOD固有の結果名。
- `RWS_VgeHybrid_BirthsDesc`、`RWS_VgeHybrid_ConversionsDesc`：MOD固有設備名。
- `RWS_NewMod_ResleevingDesc`、`RWS_NewMod_FuneralDesc`：世界観固有の人体・装備用語。
- `RWS_VfePirates_WarcasketFitsDesc`、`RWS_VfeTribals_CornerstonesDesc`：装備・研究階層の独自用語。
- `RWS_NewMod_CasinoDesc`：一部言語でのスロット名称の地域差。

これらは英語の成功・失敗条件、除外対象、単位を訳出したが、訳語そのものの公式一致は保証していない。

**未検証**：RimWorld 1.7実ゲーム内の文字幅・改行・表示確認、10言語の母語話者レビュー。これらは実施したと主張しない。
