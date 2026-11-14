# Rimworld Statistics 複数AI並列調査の日本語引き継ぎ指示書（2026-11-14）

目的：最新mainに対する**機能追加、修正・最適化、MOD固有統計の拡充**を、調査の時点で衝突させずに並行検討し、実装可能な小さなPRへ分割する。

この文書には**6人分の独立した調査プロンプト**がある。各担当を別AIセッションへ配布するための資料であり、文書を保存しただけで外部AIセッションが自動起動するわけではない。

対象リポジトリ：https://github.com/pengin0503/Rimworld-Statistics  
基準main：\`dc90d3cd99e4c1bdae5240ec24a049c010304b04\`（作業開始前に最新状態を再取得）  
対象ゲーム：RimWorld 1.7。細かな1.7パッチバージョン差は作業計画の妨げにしないが、選択されるDLL/APIは確認する。  
前提：v1.8.0公開済み、PR #255（コロニー別グラフ）、#256（種族別加入・死亡）、#237（MOD固有統計6件）は既にマージ済み。

最初に読む資料：

1. [次期開発候補調査・日本語版](../../research/2026-11-14-next-development-opportunity-audit.md)
2. [追加調査・元セーブの匿名化分析・新候補](../../research/2026-11-14-additional-investigation-ja.md)
3. \`docs/planned-mod-statistics/README.md\`
4. \`docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md\`
5. \`docs/planned-mod-statistics/2026-11-14-popular-race-mod-audit.md\`
6. \`docs/supported-mods.md\` と最新ソース

## 全担当共通：そのままコピーして渡す指示

> あなたは \`pengin0503/Rimworld-Statistics\` のRimWorld 1.7対応MODの調査担当AIです。**現段階は調査と設計のみ**行ってください。C#本体、翻訳、パッケージ、公開対応MOD一覧、Issue状態、リリース、Steam Workshopは変更しないでください。  
> 最新のmainで現在の実装を確かめ、過去資料の「未実装」という記述は鵜呑みにせず、実装済みとの重複を除いてください。別のAIとファイル編集が競合しないよう、あなた専用の調査Markdownだけを独立したブランチで編集します。必要であれば文書のみのPRを作成してください。  
> 報告は**日本語**で作成し、各候補に「現状・利用価値・根拠となる正確なファイル/メソッド・未証明点・既存統計との重複・実際の成功条件・失敗/中断/巻戻しなどの負のテスト・実装範囲・必要なMOD DLL/セーブ・P0/P1/P2/保留」を記載してください。根拠が取れなければ「未確認」と明記し、成功イベント・配布対応版・人気を推測で断定してはいけません。  
> 新規取得は初期ON、従来の明示的OFFは維持、設定切替に再起動を求めないことが基本。MOD/DLC無しの区間や記録されていないイベントを架空のゼロとせず、Unknown/Partial/NotApplicableを正しく使ってください。Anomalyの所持を前提にしないでください。可能なら実ゲーム検証には \`pengin0503/RimWorld-for-mod-test\` を使用します。  
> 本段階では単独の実装PRやマージを行わず、調査結果と最小の実装・テスト案を返してください。ソース・IL・選択された1.7DLLが証明できない場合は、対象統計を無理に作らず保留とします。

## AI-A：過去セーブ・分岐・統計履歴の正確性

**専用成果物**：\`docs/research/next-iteration/agent-a-ancestry-chart-accuracy.md\`

最初に読む：Issue #72、\`Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs\`、\`MetricChartSeries.cs\`、\`HistoricalCounterIntervals.cs\`、\`CheckpointSelector.cs\`、\`UI/Dialog_ColonyMetricHistory.cs\`、\`Tests/RimworldStatistics.Core.Tests/MetricChartHistoryTests.cs\`。

調査課題：

- \`SaveChartPoints\` の「同じModCampaignIdなら連続した折れ線」という条件が、同一キャンペーンの兄弟枝・巻戻し・親欠落時に不正確な連続性を示し得るか確認する。
- 2026-11-11の**6つの提供済みセーブ**に、**異なる6ファイルだがチェックポイントは5種類**、3本の直接親ID接続、21:07・21:19で同一の埋込チェックポイントとゲーム時刻とのずれ（62,500/171,392 tick）がある。この結果を本番 \`SaveReader\` がどのように取り込むか、\`exactBoundary\`、\`CheckpointMetadataEvidence\`、\`UpsertCheckpoint\` と照合する。元セーブは読み取り専用、識別子は公開しない。**3本すべてが確定intervalという意味ではない**。
- 親が実証済みのチェックポイント、未証明のSaveObservation、ゲーム内Recorder、圧縮アーカイブからの復元点をそれぞれ視覚的に区別する具体案を提示。
- 同一tick異値、親の中間欠落、MOD/DLC構成差、セーブコピー、過去Readerバージョン、読み込み失敗、分岐を含む受入条件を定義する。
- Issue #72の未完項目（実ゲームUI、確実な正の差分を持つ実親子、55件以上の実セーブ）について、既存証拠と新たに必要な証拠を区分する。統計を正確な日付に逆算しない。

完了条件：根拠付きの確定/未確定の区別、元データを改変しない描画の最小設計、負のテスト表、1PRで実装可能な境界を日本語で提出。

## AI-B：保存耐久性・大規模化・インポート速度

**専用成果物**：\`docs/research/next-iteration/agent-b-profile-performance.md\`

最初に読む：Issue #195、\`Source/RimworldStatistics.Core/Profiles/ProfileSession.cs\`、\`ProfileStore.cs\`、\`CheckpointArchivePlanner.cs\`、\`Import/ImportEngine.cs\`、\`tools/ProfileBenchmark/Program.cs\`、\`ProfileStoreTests.cs\`、\`ProfileSessionTests.cs\`、\`ImportEngineTests.cs\`。

調査課題：

- Issue #195にある「5ファイルごとの保存要求」は現在の \`SaveInterval=5秒\` と違う。**既に改善された点を再実装しない**。
- \`WriteLatest\` のロック内直列化、\`ReadVerifiedPrimary\` の既存profile照合、バックアップの比較・回転、アーカイブの追加、取込中の中断・再試行を個別に測定/評価する。
- 0.2、5、10MBの合成プロフィールを用いたp50/p95/最悪遅延、ゲームスレッドロック待ち、メモリ割当、総ディスク読み書き量を計測する設計または実測。
- 同時起動した2プロセス、古いプロファイルを書こうとする競合、書込中のクラッシュ、壊れた一次profile、schema1のバックアップ、3世代バックアップのケースを含める。
- **更新日時だけの照合に変更して競合検知を弱める案は禁止**。ロック外の不変スナップショット直列化などは実際の安全な実装経路を立証した場合のみ提案。

完了条件：現行コードに基づく数字とボトルネックの根拠、過剰最適化の拒否、最小のテスト対象、実ゲーム検証が必要な部分を明示。

## AI-C：既存UI・比較・グラフ・エクスポート

**専用成果物**：\`docs/research/next-iteration/agent-c-ui-ux.md\`

最初に読む：\`Source/RimworldStatistics/UI/DetailsTab.cs\`、\`OverviewTab.cs\`、\`CoverageTab.cs\`、\`RankingsTab.cs\`、\`ComparisonPanel.cs\`、\`Dialog_MetricChart.cs\`、\`Dialog_ColonyMetricHistory.cs\`、\`ImportTab.cs\`、\`DataManagementTab.cs\`、\`docs/csv-export.md\`、\`ViewPreferences.cs\`。

調査課題：

- 既に存在する検索、カテゴリ/状態フィルタ、お気に入り、ランキング、列比較、CSV5種＋manifestを踏まえ、**利用価値のある追加のみ**選ぶ。
- 履歴の測定時刻と最新保存時刻を分ける表示、確定した点と部分/未証明の点の凡例・説明、期間指定、オーバーレイの軸合わせ、画面狭小時の列/日本語・12言語の長文処理を調査。
- 「1日当たりの件数」などの正規化を提案するなら、累積値が**同じ長さの継続取得区間**を持つ場合だけ実施し、最高値や断片的な過去セーブからは算出しない。
- \`metric_history.csv\` 等の任意拡張や安全な読み取り専用診断を検討する。元CSV形式・欠測規則・式注入対策を維持する。
- 大量のグラフ点を表示範囲に応じて間引く場合、極値と欠測の断絶を保持する案を示す。実際に負荷があるかベンチマークが先。

完了条件：既存UIの説明と差分、ユーザー操作フロー（空/失敗/Partial含む）、言語別の回帰チェック、Agent Aに依存する表示用インターフェースを整理。

## AI-D：HAR・Biotechゼノタイプ・地形/種族MOD

**専用成果物**：\`docs/research/next-iteration/agent-d-species-landforms.md\`

最初に読む：PR #256、\`Patches/Metrics/GameFacts.cs\`、\`MembershipMetricPatches.cs\`、\`DeathMetricPatches.cs\`、\`Core/Stats/Families/MembershipFamily.cs\`、\`HealthDeathFamily.cs\`、\`RosterSnapshot.cs\`、\`SpeciesBreakdownTests.cs\`、\`2026-11-14-popular-race-mod-audit.md\`。

調査課題：

- **種族別初回加入と自由入植者死亡は既に実装済み**。今後の対象は、在籍ピーク・囚人・奴隷・ゼノタイプ別人数の**新しい分類軸**。
- HAR種族の \`Pawn.def\`/packageId、Biotech \`XenotypeDef\` とその変化を独立した次元とし、同じポーンが両方に属しても人数を二重加算しない。
- MODを外した後でも旧記録のキー・表示名が失われない保存方式を検討。未知を人間種に強制分類しない。
- Geological Landformsの \`Landform.Id\` とVanilla Landmarks Expanded等が実際に運営したコロニーにどう帰属するか確認。単なるマッププレビュー/生成でコロニー数を増やさない。
- Archon実帰還とNewRatkinPlusの実労働は別イベントの候補。通常の死亡/加入/名目発電では代替不可。Rabbie、Yuran、Dragonianは特別な確定イベントが無ければHAR共通分類だけで十分。

完了条件：二重計上しないデータモデル、安定キー/多言語、過去プロファイル互換、実1.7 DLL確認、狭い成功フック。

## AI-E：独自イベント・Puppeteer・Winston Waves

**専用成果物**：\`docs/research/next-iteration/agent-e-campaign-modded-outcomes.md\`

最初に読む：\`docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md\`、\`Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs\`、\`OptionalTransitionCallSite.cs\`、\`ModProviderAdapters.cs\`、\`Core/Stats/Families/ModdedIncidentFamily.cs\`、\`VpeStatistics.cs\`。

調査課題：

- **Vanilla Events Expanded**：複数段階のイベントの確定完了。開始・タイムアウト・cancel・一般incidentとの重複を排除。
- **Puppeteer**：実際に成立した傀儡/意識転送を観測。通常VPEのcast件数は既に別の取得経路がある。
- **Vanilla Ideology Expanded – Dryads**：合成要求ではなく、結果ドライアドが実生成されたときだけ記録。
- **Alpha Genes**：独自の遺伝子操作で汎用Biotechと重複しない確定成功だけを抽出。
- **今回新規：Winston Waves**：Workshop約19万購読の有力候補。1.7を実際にロードするDLLと、正常なウェーブ撃退完了（通常/ボス）、報酬確定のメソッドを特定する。ウェーブ開始・UI表示・襲撃発生を成功扱いしない。ストーリーテラー切替、撤退、同一ウェーブ再開、ゲームロード、バージョン更新への耐性を評価。

完了条件：各MODで「現状重複/実成功/IL未証明/保留」を区別し、**証拠のある1〜2件の小さな実装候補**に絞って提示。ソース/バイナリ未確認の場合は架空のメソッドを書かない。

## AI-F：サービス・経済・生産・Empire Refactored

**専用成果物**：\`docs/research/next-iteration/agent-f-service-production-modded-outcomes.md\`

最初に読む：\`docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md\`、\`2026-11-13-evidence-gated-outcome-disposition.md\`、\`Core/Stats/Families/FishingFamily.cs\`、\`TradeFamily.cs\`、\`WorkFamily.cs\`、\`IntegratedTransitionAdapters.cs\`。

調査課題：

- **Hospital**：患者受入・本当に治った退院・現物として受け取った代金を別にする。単なる退院と現実の入金を混同しない。
- **Vanilla Fishing Expanded**：既存 \`FishingFamily\` はOdyssey条件付き。Odysseyなし環境での実捕獲が独立して可能かと、Odyssey導入時の二重計上排除。
- **Vanilla Furniture Expanded – Farming**：スプリンクラー効果による実成長の増分を捕える。毎tick全植物走査や見かけのエフェクトは不採用。
- **今回新規：Empire Refactored**：上流 \`About/About.xml\` は1.7、\`LoadFolders.xml\` はv1.7フォルダーを明記。\`ISettlementListener.OnSettlementCreated\` / \`OnSettlementUpgraded\` / \`OnBuildingConstructed\` と \`LifecycleRegistry.InvokeOn...\` の**実呼出し時点**、プレイヤー帰属、旧版とは異なるパッケージIDを監査。従属入植地の創設/発展を元ゲームのコロニー数と混同しない。\`TaxLedger.AddTax\` は請求書発行、\`ITaxDeliveryInterceptor\` は配達前なので売上/着金と見なさない。
- **今回新規：Caravan Adventures**：賞金クレジットの実決済、固有ストーリー達成のソースが得られるか先行調査。Royalty必須の領域と独立領域を区分。成功箇所を確認できなければ保留。
- World Domination 2.0、Rim War維持版は人気と重複の観点から二次監査枠。見送り済みのGastronomy/Prison暴動/Rimefeller精製量を根拠なしに復活させない。

完了条件：候補ごとの取得確実性・所要実装規模・TPS負担・1.7アセンブリ選択、既存集計との重複、必要なアップロード物を具体化。最も独立した1件の最小実装範囲まで限定。

## 統括AI：報告が揃ってからの統合手順

1. 担当A〜Fからの日本語報告を読む。実際に起動されていないAIの成果を「完了」としない。各報告が最新mainを見ているか確認する。
2. 全候補を**重要度 × 固有性 × 証拠の強さ × 1.7利用者規模 × 実装負荷 × 既存重複**で再評価し、先行すべき小規模な単位を選ぶ。
3. #72/#195と既存の実装計画へマッピングし、既存の修正済み課題を二重Issue化しない。
4. それぞれの設計を小さな独立PRへ切り分ける。共通 \`MetricRegistry\`、\`Languages\`、プロフィール形式を複数のAIが並行して変更しない。**整合性確認→保存耐久性→共通分類→個別MOD成果→UI→回帰テスト**の順が安全。
5. ソース検査、TDD、CI、コードレビュー、必要時 \`RimWorld-for-mod-test\`、最終パッケージ確認を行う。元セーブは公開せず読み取り専用で扱う。
6. **リリースとWorkshop更新はこの調査の権限外**。研究しただけで \`docs/supported-mods.md\` に追加しない。

## 成果物の合格条件

- [ ] 日本語で、現在の実装と既存計画の照合結果がある
- [ ] 根拠となるファイル名・メソッド、上流URL、ゲーム1.7で選択されるバイナリを区別している
- [ ] 本当に増やせる統計か、既存統計と重複しないかを説明できる
- [ ] 成功/失敗/中断/再ロード/分岐/設定OFF/対象MOD不在の負のテスト案がある
- [ ] 実測・予測・未確認を明確に区別する
- [ ] 作業単位が限定され、独立したPRでレビューできる
- [ ] コードの変更や公開対応MOD名の追加は行っていない
