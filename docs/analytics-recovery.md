# 分析库（feature/analytics）中断恢复与续做记录

日期：2026-11-12（Asia/Shanghai）。工作树：`C:\tyuStockApp\build\wt-analytics`，分支 `feature/analytics`。

## 1. 中断现场审计

| 项目 | 状态 | 结论 |
|---|---|---|
| 提交 `0e7213f`（技术指标） | 6 个源文件逐一通读，结尾完整，UTF-8 合法 | 完好 |
| 提交 `49eb019`（BSM 定价 / 希腊值 / IV 求解 / 到期时间） | 5 个源文件通读，逻辑完整 | 完好 |
| 未跟踪 `Directory.Build.props`、`Directory.Packages.props` | 与 main 分支同名文件 **git blob 哈希完全一致**（9f0570a / bb74f3c），并非写了一半 | 删除，改为合并 main 获得同一文件，避免冲突 |
| 未跟踪 `.local/w.ps1` | 上一个工作者的写文件辅助脚本（389 字节），完整 | 保留为本地临时文件，不入库 |
| 单元测试 | **中断前一个测试都还没写**（无测试工程） | 本次补齐 |
| 策略盈亏模块 | **尚未开始**（csproj 注释中计划了，但没有代码） | 本次完成 |
| 编译 | 中断后原样编译：0 警告 0 错误（TreatWarningsAsErrors） | 通过 |

审计中发现的小问题（均非损坏）：

- `NormalDistribution.Cdf` 绝对误差 < 1e-14，但远尾（x≈-7…-10）相对误差约 3e-9（实测）。对期权价格/IV 影响可忽略，已在测试中注明，未改算法。
- `UsEastern`（分析库内）与 main 的 `TyuStock.Core.Time.MarketClock` 都解析了纽约时区。分析库保持零依赖，故保留；以后如需统一可让 Core 复用。

## 2. 与 main 对齐

- 合并 `main`（8d3a8da，M0 骨架）进 `feature/analytics`：获得 `Directory.Build.props`（Nullable/ImplicitUsings/中央包管理）、`Directory.Packages.props`、`TyuStockApp.slnx`。
- 改名：`TyuStockApp.Analytics` → `TyuStock.Analytics`（命名空间、`src\TyuStock.Analytics`、`tests\TyuStock.Analytics.Tests`）。
- csproj 只保留本库特有设置（TreatWarningsAsErrors、XML 文档、AOT/裁剪兼容），其余由 Directory.Build.props 统一提供。
- 测试工程与 main 的 `TyuStock.Core.Tests` 写法一致（xUnit，版本走中央包管理）。
- `TyuStockApp.slnx` 新增两行（src 与 tests 各一行）。

## 3. 本次新增：期权策略盈亏（`src\TyuStock.Analytics\Strategies`）

- `StrategyLeg`：看涨/看跌/正股，多/空，数量，权利金（正股为成本价），行权价，到期日，单腿 IV，合约乘数（默认 100，正股恒为 1）。
- `OptionStrategy`：到期盈亏、到期前任意日期按 BSM（逐腿 IV）的理论盈亏、组合希腊值、曲线采样、价格区间建议；
  到期分析为**精确解**（分段线性，含无限盈利/无限亏损判定）；到期前分析为数值解（网格 + 二分 + 黄金分割），无界性按结构判定。
- `StrategyValuation`：估值时点、无风险利率、股息率、默认 IV、IV 平移情景、结算时刻。
- `StrategyPresets`：买入看涨/看跌、备兑看涨、保护性看跌、牛/熊 看涨/看跌价差、跨式（多/空）、宽跨式（多/空）、铁鹰、蝶式（看涨/看跌）。

## 4. 测试

`tests\TyuStock.Analytics.Tests`：154 个测试全部通过（Debug 与 Release）。整个解决方案：分析库 154 + Core 19 + Data 1 全绿，0 警告。

- BSM：Hull 教材例题价格与希腊值、Haug 带股息看跌价格、看涨看跌平价网格、希腊值与有限差分对照、退化输入。
- IV：350 组参数往返、不可解输入返回 null、报价/供应商/成交价三级取值、IV 过滤。
- 指标：与独立的 Python 实现（`tests\TyuStock.Analytics.Tests\reference\reference_values.py`）交叉验证 EMA/RSI/MACD/ATR/KDJ/布林带；与暴力算法对照 SMA/WMA/最高/最低/随机指标；缺失值、VWAP 分时段。
- 策略：全部预设的盈亏平衡点与最大盈亏、收益恒等式（合成正股、备兑=卖出看跌+常数、铁鹰=两价差之和）、到期日估值收敛到到期盈亏、公平权利金入场盈亏为 0 等。

## 5. 给集成者的合并说明

- `feature/analytics` 已包含 main 的 8d3a8da；与当前 main（c1d918c）做 `git merge-tree` 试合并：**无冲突**。
- 唯一与 main 共同修改的文件是 `TyuStockApp.slnx`（仅新增 2 行）；如 main 同时改了 slnx，按行合并即可。
- 分析库零外部依赖，App 只需添加 `<ProjectReference Include="..\TyuStock.Analytics\TyuStock.Analytics.csproj" />`。