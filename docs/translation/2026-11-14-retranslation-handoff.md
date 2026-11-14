# 全言語翻訳の精査・再翻訳：引き継ぎ資料（2026-11-14）

依頼「このMODの翻訳に不自然な部分があるので、全言語精査して修正して」の作業を、**第2ブロック（英語キー順の1〜250番）まで終えた時点で中断**し、別のAIや翻訳者が続きを行えるよう記録したものです。進捗の機械可読版は [`tools/translation/progress.json`](../../tools/translation/progress.json)、作業用スクリプトは [`tools/translation/`](../../tools/translation/) にあります。

## 1. 現状（中断時点）

| 対象 | 状態 |
| --- | --- |
| English | 382キーを修正済み。**以後の全言語の翻訳元**。 |
| Japanese | 715キーを修正済み（公式訳語への統一、文体統一、英語修正の反映）。ブロック再翻訳の対象外。 |
| 他10言語 | ブロック01・02（1〜250番）を**新しい英語から全面再翻訳済み**。ブロック03〜10（251〜1234番、984キー）は未着手で、**旧英語からの機械翻訳のまま**。 |

10言語＝ChineseSimplified、ChineseTraditional、French、German、Korean、Portuguese、PortugueseBrazilian、Russian、Spanish、SpanishLatin。`tools/check_keys.sh`（キーの過不足・`{n}`プレースホルダーの一致）は中断時点で全言語合格しています。実ゲームでの表示確認（文字幅・改行）は行っていません。

### 調査で分かったこと

- 10言語は #244 で一括追加された**機械翻訳**で、誤訳が全面的に含まれていた。修正対象は「一部の不自然な箇所」ではなく、ほぼ全文。代表例：
  - save（セーブ）を「節約・救出・貯金」と誤訳（独 `Spareinlagen`、仏 `Économies importées`、露 `Импортные скидки`、Parent save→`Родитель спасает`）。
  - tick を「ダニ」（`Zecken` / `tiques` / `клещами` / `진드기`）、fork を「食器のフォーク」と誤訳。
  - Mental breaks が韓国語で「남자의 휴식（男の休息）」、仏 `Pauses mentales`、西 `Roturas mentales`。
  - pawn を「文字（characters）」と誤訳（`caracteres`、`字符`、`문자`）。
  - 説明文の欠落（繁体字の `RosterSkill20PawnsPeakDesc` が「最高的殖民者,」で途切れる、仏 `CombatExtended_ReloadsDesc` の1文目欠落など）。
  - RimWorld公式訳と異なる用語（各言語の公式訳は §5）。
- English 自体にも不自然・不正確な箇所があった：
  - セーブ時点スナップショット系の説明がテンプレート的（例：「Highest augmented colonists at a successful save, from eligible living pawns (peak values span saved observations).」「Mechanoids manufactured only after the relevant Biotech action actually completes.」）。
  - 実装と食い違う表記（`StockMaterialsLatest` は実際には Steel / ComponentIndustrial / ComponentSpacer だけを集計するのに「Construction materials by type」、`RWS_NoData` が存在しない「Import tab」を案内）。
  - 「Highest colonists with skill 20」のような意味の通らないラベル。
  英語はコード（`SnapshotCatalog.cs`、`StockSnapshot.cs`、`RosterSnapshot.cs`、`FacilitiesSnapshot.cs`、`Families/BiotechFamily.cs`、`Families/OdysseyFamily.cs`、`SavedStateMetricReader.cs`）で実際の集計内容を確認してから書き直した。
- Japanese は大筋で自然だったが、公式訳との不一致（アーコネクサス→アルコネクサス、開発モード→開発者モード、冷凍睡眠カプセル→冬眠カプセル、メカノイドクラスター→メカクラスター、社交喧嘩→喧嘩、栽培ゾーン→農業ゾーン、伝説/粗悪→幻の一品/壊れかけ、銀/銀貨→シルバー、イデオロギー→思想 など）、英単語の混入（「Partialです」）、文体の不統一（です・ます調と常体の混在）、テンプレート的な説明文があった。
  - 「勢力」は公式訳では「派閥」だが、日本語として自然で広く使われているため変更していない。

## 2. 残作業

1. **ブロック03〜10を10言語で再翻訳する**（下表）。現在の値は旧英語からの機械翻訳なので、既存訳は参考にせず、**現在の英語から訳す**。
2. **English と Japanese の permadeath 表記を公式名に合わせる**（6キー、いずれもブロック03に含まれる）：
   `RWS_Details_Permadeath`、`RWS_Details_NotPermadeath`、`RWS_Metric_RankByPermadeathDays`、`RWS_MetricDesc_RankByPermadeathDays`、`RWS_Metric_RankByPermadeathEndings`、`RWS_MetricDesc_RankByPermadeathEndings`。
   公式名：英 Commitment mode / Reload anytime mode、日 コミットメントモード / 無制限リロードモード。他10言語は §5 の公式名を使う。
3. 全言語の母語話者レビューと、実ゲームでの表示確認（特に列見出し・ボタンの文字幅）。
4. 作業が終わったら `tools/translation/progress.json` を更新する。

| ブロック | 位置 | 先頭キー | 末尾キー | 状態 |
| --- | --- | --- | --- | --- |
| 01 | 1–125 | RWS_SettingsCategory | RWS_WealthCoverage | 完了 |
| 02 | 126–250 | RWS_Source_RecoveredLineage | RWS_MetricDesc_ModsMin | 完了 |
| 03 | 251–375 | RWS_Metric_QuestSuccess | RWS_Metric_DeathsByRaid | 未着手 |
| 04 | 376–500 | RWS_MetricDesc_DeathsByRaid | RWS_MetricDesc_CraftQuality | 未着手 |
| 05 | 501–625 | RWS_Metric_CraftLegendary | RWS_Metric_Resurrections | 未着手 |
| 06 | 626–750 | RWS_MetricDesc_Resurrections | RWS_MetricDesc_FacilitiesBattery | 未着手 |
| 07 | 751–875 | RWS_Metric_FacilitiesOutdoorTempMin | RWS_MetricDesc_CleaningJobsCompleted | 未着手 |
| 08 | 876–1000 | RWS_Metric_TreeWoodPlaced | RWS_Status_FilterMissing | 未着手 |
| 09 | 1001–1125 | RWS_Status_FilterNA | RWS_VnpeGrinder_SlurryStored | 未着手 |
| 10 | 1126–1234 | RWS_VnpeGrinder_SlurryStoredDesc | RWS_Chart_NoData | 未着手 |

位置は English の XML に現れる順（1始まり）。英語にキーが追加・並べ替えされた場合は位置ではなく先頭・末尾キーで照合すること。

## 3. 作業手順

```bash
# 1) 公式訳の用語集を作る（.cache/ は git 管理外。公式訳本文はリポジトリに保存しない）
python3 tools/translation/build_vanilla_glossary.py --game-dir "/path/to/RimWorld"
#    または R2 の非公開ミラーから（boto3 と R2_ENDPOINT / R2_BUCKET / AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY が必要）
python3 tools/translation/build_vanilla_glossary.py --r2

# 2) 用語を調べる
python3 tools/translation/glossary.py core German
python3 tools/translation/glossary.py search French '^manhunter pack$'
python3 tools/translation/glossary.py count Japanese '\bfactions?\b' 派閥 勢力

# 3) 英語原文を見る（必要なら既存訳や日本語も並べる）
python3 tools/translation/show_keys.py 251 375
python3 tools/translation/show_keys.py 251 375 Japanese,German

# 4) tools/translation/work/<Language>_<NN>.json を書く（{"RWS_Key": "訳文", ...}、ブロック内の全キー）
#    地域差の大きい3組は、片方を書いてから下書きを派生させ、出力を1行ずつ読んで直す
python3 tools/translation/derive_variant.py es2la tools/translation/work/Spanish_03.json tools/translation/work/SpanishLatin_03.json
python3 tools/translation/derive_variant.py br2pt tools/translation/work/PortugueseBrazilian_03.json tools/translation/work/Portuguese_03.json
pip install opencc-python-reimplemented   # zh2tw のみ
python3 tools/translation/derive_variant.py zh2tw tools/translation/work/ChineseSimplified_03.json tools/translation/work/ChineseTraditional_03.json
#    修正は第4引数の overrides.json に書くと最後に上書きされる

# 5) 10言語分を一括適用（ブロックの過不足を先に検査し、最後に tools/check_keys.sh を実行）
bash tools/translation/apply_chunk.sh 03 251 375

# 単一言語・単一ファイルの適用（--dry で書き込まずに検査だけ）
python3 tools/translation/apply_patch.py German tools/translation/work/German_03.json --dry
```

- `apply_patch.py` はキーの値（要素の中身）だけを置き換え、ファイルの他の部分は1バイトも変えない。未知のキー、空値、前後の余分な空白（先頭1文字の半角スペースだけは許可：`RWS_DevModeMark` など）、`{n}` の不一致、XMLとして読めない結果は、書き込まずにエラーにする。
- `derive_variant.py` の出力は**下書き**。規則置換なので、必ず全行を読んで直すこと（例：zh2tw は OpenCC が「类型」を「型別」にする、「列/行」の意味が大陸と台湾で逆、など既知の問題を規則で補正しているが完全ではない）。
- `tools/translation/work/` の中間ファイルはコミットしてもよい（途中での引き継ぎ用）。

## 4. 全言語共通の規約

- **翻訳元は現在の English。** 既存の機械翻訳は参照しない（誤訳を引き継ぐため）。意味が曖昧な場合は Japanese も参照し、それでも不明ならコードで使われ方を確認する。
- `{0}` などのプレースホルダーは数も番号も変えない（`apply_patch.py` と `tools/check_translation_formats.py` が検査）。
- 値に `&` を使わない。引用符は各言語の活字引用符（独「„…“」、仏・露・葡（欧州）「«…»」、西（スペイン）「«…»」、西（中南米）・葡（ブラジル）「“…”」、中「“…”」、繁「「…」」、韓「“…”」）。JSON のエスケープも不要になる。
- MOD名・DLC名（Royalty、Ideology、Biotech、Odyssey、Hospitality、Vanilla Expanded …）とDef名（`MentalBreakDef`、`HaulToCell`、`CurrentCap` など）は英語のまま。
- 品質別内訳の「品質なし」は画面に生キー **`"none"`** が表示されるため、説明文でも `none` のまま引用する（`WorkFamily.NoQuality`）。
- 「不明」の内訳は `RWS_Details_UnknownBucket` の訳（例：独 `(unbekannt)`）が表示されるので、説明文中の "unknown" は同じ語で引用する。
- `RWS_Exp_Since`、`RWS_Details_Tip_Through`、`RWS_Cross_RecordedAt` の `{0}` は小数（例 `12.5`）。
- `RWS_Exp_Event` / `RWS_Exp_Snapshot` は設定画面で「`unitId · 種別`」として表示される短いラベル。`RWS_Exp_SelectAll` / `SelectNone` / `SelectEvents` は幅1/3のボタン。
- `RWS_Cross_MaxColony` / `MinColony` / `TopType` / `TopCategory` / `Sum` の `{0}` は統計名そのもの（例「最大のコロニー：{0}」）。
- `RWS_Col_*`、`RWS_Details_Col_*`、`RWS_Status_*`（列見出し）は短く。
- ロシア語は数詞と名詞の格一致を避けるため、「`Колоний в расчёте: {0}`」のように「名詞: {0}」の形にする。
- 文体：独は du（公式訳と同じ）、仏は vous、西（スペイン）は tú・現在完了、西（中南米）は tú・点過去・agregar、葡（ブラジル）は você 系、葡（欧州）は無主語の丁寧体（「Consulte」）、韓は 합니다체、中・繁は全角句読点。
- 1〜250番で使った語は、続きでも同じ語を使う（例：独 Spielstand/Spielende、仏 sauvegarde/fin、西 partida guardada/final、葡（ブ）jogo salvo/final、葡（欧）jogo guardado、露 сохранение/концовка、中 存档/结局、繁 存檔/結局、韓 저장 파일/엔딩/정착지）。迷ったら `show_keys.py 1 250 <Language>` で既訳を確認する。

## 5. 言語別の公式用語（RimWorld 1.7.4985 公式翻訳から抽出）

公式訳は `build_vanilla_glossary.py` で再生成できる。下表はこのMODで頻出する語の決定版（公式訳が不自然でも、ゲーム画面との一致を優先した）。

- **German**: Kolonist(en), Überfall/Überfälle, Fraktion, Ruf (goodwill), Gefangene(r), Sklave, Karawane, Siedlung, Erzähler, Szenario, Schwierigkeitsgrad, Biom, Entwicklermodus, Nervenzusammenbruch, Inspiration, Psylink, mentale Kräfte (psycasts), Psywirker, Mechanitor, Bandbreite, Gravschiff, Ideologie (ideoligion), Ritual, Ehre, Ermächtigungen, Mechanoid, Mech-Gestator, Giftmüllsack, Giftmüllatomisierer, Verseuchung, Anbauzone, Hydrokulturbecken, Kryptoschlafkapsel, Transportkapsel, Landekapsel, Insektenplage, menschenjagendes Rudel, Mech-Cluster, Belagerung, Sonneneruption, giftiger Fallout, Kältewelle, Hitzewelle, vulkanischer Winter, Sonnenfinsternis, Fäule, Kurzschluss, Meteoriteneinschlag, Hinterhalt, Medizin, Lebensmittelvergiftung, Entzug, Prügelei, Bindung, zähmen, Qualität übel/meisterlich/legendär, Stahl, Bauteil, Hightech-Bauteil, Silber, Geschützturm, Batterie, Personawaffen, Hingabe (passion), Fertigkeit, Archonexus, Königlicher Aufstieg, Cerebrex-Kern, Antike Gefahr, Außenposten, Stimmung, Behandlungsqualität, Quest, Hardcore-Modus / Laden-Erlaubt-Modus.
- **French**: colon(s), raid, faction, relations (goodwill; 公式訳は揺れあり), prisonnier, esclave, caravane, narrateur, scénario, difficulté, biome, mode développement, crise mentale, inspiration, lien psychique, pouvoirs psychiques, méchanitor, bande passante, vaisseau gravitationnel, idéoligion, rituel, honneur, licences (permits), mécanoïde, gestator, déchet toxique, atomiseur de déchets, zone agricole, bassin hydroponique, caisson d'hypersommeil, capsule de transport, capsule de largage, infestation, meute de chasseurs d'hommes, groupe de mécanoïdes, siège, éruption solaire, retombées toxiques, vague de froid/chaleur, hiver volcanique, éclipse, mildiou, court-circuit, impact de météorite, embuscade, médicaments, intoxication alimentaire, sevrage, bagarre, lien, apprivoiser, qualité horrible/merveille/légendaire, acier, composant, composant avancé, argent, tourelle, batterie, armes intelligentes, compétence, à terre, archonexus, Bénédiction royale, noyau cérébrex, danger antique, avant-poste, humeur, qualité du traitement, quête, Mode Engagement / Mode Rechargeable à tout moment.
- **Spanish**: colono, asalto, facción, reputación, prisionero, esclavo, caravana, narrador, escenario, dificultad, bioma, modo desarrollador, colapso mental, inspiración, psienlace, habilidades psíquicas, mecanitor, banda ancha, gravinave, ideoligión, ritual, honor, permisos, mecanoide, gestador de mecas, pack de residuos tóxicos, atomizador de residuos, área de cultivo, contenedor hidropónico, sarcófago de criptosueño, cápsula de transporte, infestación, manada cazahombres, mecaconglomerado, asedio, llamarada solar, lluvia tóxica, frente frío, ola de calor, invierno volcánico, eclipse, plaga, cortocircuito, impacto de meteorito, emboscada, medicina, intoxicación alimentaria, abstinencia, pelea social, vínculo, domesticar, calidad horrible/obra maestra/legendaria, acero, componentes, componentes avanzados, plata, torreta, batería, pasión, habilidad, inconsciente, arqueonexo, Ascensión real, núcleo Cerébrex, peligro ancestral, puesto de avanzada, humor, calidad del tratamiento, misión, Modo comprometido / Modo recarga libre.
- **SpanishLatin**: Spanish と同じ。ただし buena voluntad、cuentacuentos、psicoenlace、mecanizador、clúster de mecas、magistral、asentamiento、armas con IA、pelea、núcleo Cerebrex。
- **PortugueseBrazilian**: colono, invasão, facção, boa vontade, prisioneiro, escravo, caravana, narrador, cenário, dificuldade, bioma, modo desenvolvedor, colapso mental, inspiração, psínculo, psinjurações/psinjurador, mecanizador, banda-larga, gravinave, ideologia (ideoligion), ritual, favor real (honor), permissões, mecanoide, gestador mecanoide, pacote de resíduos tóxicos, zona de cultivo, bacia de hidroponia, sarcófago de criptossono, cápsula de transporte, infestação, grupo caçador de humanos, aglomerado mecanoide, cerco, erupção solar, chuva tóxica, onda de frio/calor, inverno vulcânico, eclipse, ferrugem (blight), curto-circuito, impacto de meteorito, emboscada, medicamento, intoxicação alimentar, abstinência, briga social, vínculo, domesticar, qualidade horrível/obra-prima/lendário, aço, componente, componente avançado, prata, torreta, bateria, armas persona, interesse (passion), habilidade, abatido (downed), arquonexo, Ascensão Real, núcleo cerebrex, perigo ancestral, posto avançado, humor, qualidade do tratamento, missão, Morte Permanente / Recarregar a Qualquer Momento.
- **Portuguese**（欧州。公式訳は大部分が未翻訳）: 語彙はブラジルに合わせつつ欧州の綴り・語法にする（colónia, ficheiro, ecrã, guardar/jogo guardado, registo, recolha, utilizador, «»）。公式訳にある語：Modo de desenvolvimento, Matilha de caçadores de pessoas, Zona de cultivo, Perigo ancestral, Estado de espírito（mood。1〜250番では humor を採用）。
- **Russian**: поселенец/поселенцы, налёт, фракция, репутация, пленник, раб, караван, поселение, рассказчик, сценарий, сложность, биом, режим разработчика, нервный срыв, вдохновение, пси-связь, пси-способности, псионик, механитор, пропускная способность, гравикорабль, идеолигия, ритуал, честь, привилегии, механоид, гестатор механоидов, мешок отходов, расщепитель отходов, загрязнение, посадки (growing zone), гидропонная ферма, саркофаг криптосна, транспортная капсула, насекомые (infestation), агрессивные животные (manhunter pack), кластер механоидов, осада, солнечная вспышка, токсичные осадки, заморозки, жара, вулканическая зима, затмение, болезнь растений, короткое замыкание, метеорит, засада, медикаменты, пищевое отравление, ломка, потасовка, привязанность, приручить, качество ужасно/шедевр/легенда, сталь, компонент, продвинутый компонент, серебро, турель, аккумулятор, оружие с личностью, интерес (passion), навык, обездвижен, архонексус, Знатное восхождение, ядро церебрекса, древняя опасность, лагерь (VE outpost), настроение, качество лечения, задание, «Ответственный» режим / Загрузка в любой момент.
- **ChineseSimplified**: 殖民者, 袭击, 派系, 好感度, 囚犯, 奴隶, 远行队, 定居点, 故事叙述者, 剧本, 难度, 生态群落, 开发者模式, 精神崩溃, 灵感, 启灵神经, 心灵能力, 机械师, 带宽, 逆重飞船, 文化 (ideoligion), 仪式, 荣誉, 许可, 机械族, 机械培育器, 有毒垃圾, 垃圾分解器, 污染, 种植区, 水栽培植物盆, 低温休眠舱, 运输舱, 虫灾, 机械集群, 围攻, 太阳耀斑, 有毒尘埃, 寒流, 热浪, 火山冬天, 日蚀, 枯萎, 短路, 陨石撞击, 伏击, 医药, 食物中毒, 戒断反应, 打架, 牵绊, 驯服, 品质 极差/大师级/传奇级, 钢铁, 零部件, 高级零部件, 白银, 炮塔, 蓄电池, 人格武器, 兴趣, 技能, 倒地, 超凡枢纽, 荣誉晋升, 主脑核心, 远古威胁, 哨所, 心情, 治疗质量, 任务, 存档, 硬核模式 / 普通模式。
- **ChineseTraditional**: 殖民者, 襲擊, 派系, 友好度, 俘虜, 奴隸, 旅隊, 據點, 故事敘述者, 腳本, 難度, 生態系統, 開發者模式, 精神崩潰, 靈感, 心靈連結, 心靈能力, 御機使, 頻寬, 重力船, 理念 (ideoligion), 儀式, 榮譽, 權限, 機械族, 機械培養艙, 廢料袋, 廢料袋原子化機器, 污染, 種植區, 水耕植物盆, 休眠艙, 運輸艙, 蟲災, 狂亂獸群, 機械軍團, 圍攻, 太陽閃焰, 毒霧霾, 寒流, 熱浪, 火山冬天, 日蝕, 枯萎, 短路, 隕石撞擊, 埋伏, 藥品, 食物中毒, 戒斷, 吵架, 羈絆, 馴服, 品質 糟糕/大師/傳奇, 鋼鐵, 零件, 高級零件, 白銀, 砲塔, 電池, 羈絆武器, 熱情, 技能, 倒地, 遠古聯繫, 榮譽提升, 超智腦核心, 遠古威脅, 哨站, 心情, 治療品質, 任務, 存檔, 無悔模式 / 自由讀檔模式。
- **Korean**: 정착민, 정착지 (colony), 습격, 세력, 우호도, 수감자, 노예, 상단, 이야기꾼, 시나리오, 난이도, 기후 (biome), 개발자 모드, 정신이상, 영감, 정신력 증폭 (psylink), 초능력 (psycasts), 메카나이터, 대역폭, 중력부양선, 사상 (ideoligion), 행사 (ritual), 신임장 (honor), 지원 요청 (permits), 메카노이드, 메카노이드 배양기, 독성 폐기물, 폐기물 원자분해기, 오염, 재배구역, 수경재배기, 동면관, 수송 포드, 곤충 군락, 식인동물 무리, 메카노이드 전초기지 (mech cluster), 흑점 폭발, 독성 낙진, 한파, 폭염, 화산 겨울, 일식, 마름병, 합선, 운석 충돌, 매복, 의약품, 식중독, 금단 현상, 말다툼 (social fight), 각별 (bond), 길들이기, 품질 끔찍/걸작/전설적, 강철, 부품, 고급 부품, 은, 포탑, 축전지, 결속 무기, 열정, 기술, 쓰러짐, 초월 연결체, 왕실의 승인, 세레브렉스 코어, 고대 위협, 전초기지, 기분, 치료 품질, 임무, 저장 파일, 철인 모드 / 기본 모드.
- **Japanese**（参考）: 入植者, 襲撃, 派閥（本MODは「勢力」を維持）, 友好値, 囚人, 奴隷, キャラバン, 開発者モード, 精神崩壊, サイリンク, 超能力 (psycasts), メカニター, 帯域幅, グラヴシップ, 思想, 冬眠カプセル, 輸送ポッド, ドロップポッド, 虫の襲来, 人狩りの群れ, メカクラスター, 死の灰, 枯病, 電力線ショート, 喧嘩, 幻の一品/名品/壊れかけ, スチール, コンポーネント, 先進コンポーネント, シルバー, 農業ゾーン, 水耕栽培器, メカ培養槽, 有毒廃棄物パック, アルコネクサス, 爵位の昇格, セレブレックス・コア, 太古の脅威, コミットメントモード / 無制限リロードモード。

Anomaly の2エンディング（Embraced the void / Disrupted the void）は公式訳データが手元になく、1〜250番では各言語で自然な表現を独自に当てた（独 Leere angenommen / Leere gestört、仏 Vide embrassé / Vide perturbé、西 Vacío abrazado / Vacío interrumpido、葡 Vazio abraçado / Vazio interrompido、露 Принятие пустоты / Разрыв с пустотой、中 拥抱虚空 / 瓦解虚空、繁 擁抱虛空 / 瓦解虛空、韓 공허 수용 / 공허 교란）。Anomaly の公式訳が入手できたら合わせること。

## 6. English・Japanese の変更点（レビュー依頼）

English と Japanese の修正は機能に影響しないが、文言の承認が必要。差分は `git diff` で確認できる。主な内容：

- セーブ時点スナップショット系（`Roster*`、`Facilities*`、`Stock*`、`RoyaltyPsylinkPeak`、`BiotechPollutionPeak`、`BiotechBandwidthPeak`）の説明を、実際の集計（代表セーブの値か、記録したセーブ間の最大・最小か）に合わせて書き直した。
- `StockMaterialsLatest` を「Steel and components in stock」（日「スチール・コンポーネントの所持数」）に修正。
- Biotech・Odyssey のイベント系（`BiotechMechsCreated` ほか7件、`Odyssey*`）の説明を、`BiotechFamily` / `OdysseyFamily` の判定条件に合わせて書き直した。
- `RWS_NoData` の案内先を実在する「Data management」タブに修正。
- 統合後のアドオン系統計で「since activation / since opt-in / addon period」を「since enabled」（日「有効化後」）に統一。
- `RWS_Exp_Event` / `RWS_Exp_Snapshot` / `RWS_Exp_SelectEvents` を「Event (during play)」「Snapshot (at successful saves)」「Event units only」に変更。
- 開発者向けの不要な一文（`RWS_ModHospitality_PresentGuestsDesc` の「No change to the guest MOD is required.」）を削除。
- README には「MOD設定の『開発モードを含める』」という記述があるが、実際の設定名は `RWS_Setting_IncludeDevMode`（日「開発者モード中に記録したエンディングを表示し、合計に含める」）。README は今回の作業範囲外として未変更。

## 7. 参考：作業時に確認したコード

- 単位表示：`Source/RimworldStatistics/UI/DetailsTab.cs` の `Format()`（`RWS_Unit_*`、`RWS_HoursValue`）。`RWS_Unit_PerYear` は死亡・襲撃の年率、`RWS_Unit_Years` は年齢。
- 内訳ラベル：`DetailsTab.BreakdownLabel()` → `MetricDisplayPolicy.BreakdownLabelKey()`。翻訳キーがない内訳は生キー（`none`、`Skill:Major` など）がそのまま表示される。
- エンディング表の列：`UI/EndingsTab.cs`（`RWS_Col_Ordinal` は同種エンディングの何回目か）。
- MOD設定画面：`RimworldStatisticsMod.cs`（`RWS_Exp_*`）。
- スナップショット定義：`Source/RimworldStatistics.Core/Stats/SnapshotCatalog.cs`。ライブ記録定義：`LiveCatalog.cs`。
