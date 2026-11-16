# テスト数・CI負荷監査（2026-11-17）

## 対象と実測

- 対象: `Tests/RimworldStatistics.Core.Tests` と `.github/workflows/numeric-verification.yml`。
- 基準: main `cc210e86504e7e7e01507cd5d8344cdcc85c9582`、GitHub Actions [43896935683](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/43896935683)。
- xUnit v3: **1,862件（成功1,849・スキップ13・失敗0）、実行時間8.001秒**。ビルドを含む `dotnet test` ステップの実経過時間は約14秒。
- 同じジョブ内のベンチマークは、特に `ProfileBenchmark --representative` が約51秒、`--large` が約7.5秒、通常のプロフィールスケーリングが約5.2秒。これらだけで単体テスト本体より長い。
- 主なテスト内容は、保存/復元、分岐、集計、MOD固有判定、数値の下限・上限、UI、翻訳キー、実セーブの任意受け入れ。単に同じ対象のテストが複数あることは、仕様条件が異なる限り冗長とは判断しない。

## 対応

1. **同一49セーブの重複走査を統合**:
   - `RealSavesRegressionTests.RealSaves_MatchResearchTotals` と `NumericRealSavesTests.RealSaves_GiveNumericObservations_WithoutChangingV1` は、どちらも `RWSTATS_REAL_SAVES_DIR` の49件を別セッションへ取り込み、同一v1値、再インポート冪等性、入力ファイルSHA256不変を検証していた。
   - 後者に前者固有のv1検証（実プレイ時間、脅威、退出、最終資産、最長、単一最大資産、最大人口、エンディング）および初回取込60秒上限を移植。前者のファイルを削除し、同じ実セーブを2度フルインポートする必要をなくした。
   - **両方の固有アサーションは残す**。通常CIでは環境変数がないため元々スキップされており、通常CIの経過時間にはほぼ影響しない。実セーブが供給された場合の所要時間/IOのみを削減する。
2. **通常CIとフルベンチマークを分離**:
   - `pull_request` と `push` は全xUnit・Pythonテスト、MOD API/IL照合、翻訳キー、ビルド、ZIP検査を引き続き実行する。重要な単体テストを除外しない。
   - 7つの **計測用** ベンチマークのみ `workflow_dispatch` または週1回の `schedule` に限定。フル測定は手動実行でき、週次の機械的な再評価も残す。
   - 普段の開発では特に約51秒のベンチマークを省略できる。一方ベンチマークの検出能力を完全廃止することはしない。

## 見込みと判定基準

| 指標 | 変更前 | 変更後 |
| --- | ---: | ---: |
| 通常CIのxUnit件数 | 1,862 | **1,861**（予定。CIで要確認） |
| 通常CIで実行する単体テスト内容 | 全件 | 全件。ただし重複した49セーブ受け入れ1件を統合 |
| 通常CIのベンチマーク起動件数 | 7 | **0** |
| 手動・週次のベンチマーク起動件数 | 7 | **7** |
| 実49セーブ取込の重複 | 2回 | **1回** |

**なぜ単純に大量削除しないか**: 単体テスト1,862件は8秒で終わっている。広範囲の冗長性を確認せずテストだけ数百件削除すると、MOD固有境界やセーブ互換性の回帰を検出しにくくする一方、時間短縮は限られる。必要条件の異なるテストを数合わせで結合しない。

## 再検証

- `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true` で通常実行の件数・成功を確認する。
- PR の `Numeric statistics build, tests and development packages` で全単体テストとパッケージビルドを確認する。
- Actions → 同ワークフロー → `Run workflow` で7ベンチマークを含むフル検証が可能。週次も同条件。
- `RWSTATS_REAL_SAVES_DIR` は既存どおり開発者がローカルでセットした場合のみテストし、機密セーブをGitHub Actionsや成果物にアップロードしない。
