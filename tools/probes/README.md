# 数据源探测脚本

- `probe_sources.py [direct|proxy|both]`：对所有候选免费数据源发真实请求，原始响应写入 `out/<mode>/`，汇总写入 `out/summary_<mode>.json`。`direct` = 不走显式代理；`proxy` = 显式走本机 Clash Verge `127.0.0.1:45444`。
- `analyze_probes.py [direct|proxy]`：从原始响应里提取关键字段。
- `probe_burst.py`：Yahoo / Nasdaq 连续请求的限流测试。

只依赖 Python 3.11 标准库。`out/` 已被 .gitignore 忽略，可随时重跑生成。
