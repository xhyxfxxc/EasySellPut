# -*- coding: utf-8 -*-
"""Trim raw responses from scratch/fixtures-raw (see capture_fixtures_win.ps1) into small unit-test fixtures."""
import os, json, re, shutil
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "scratch", "fixtures-raw")
OUT = os.path.join(ROOT, "tests", "Meili.Data.Tests", "Fixtures")

def rd(n): return open(os.path.join(RAW, n), "rb").read()
def js(n): return json.loads(rd(n).decode("utf-8-sig"))
def save(n, data):
    p = os.path.join(OUT, n)
    with open(p, "wb") as f: f.write(data if isinstance(data, bytes) else json.dumps(data, ensure_ascii=False).encode("utf-8"))
    print(f"{n:<28} {os.path.getsize(p):>8}")

for n in ["nasdaq_info_stock.json", "nasdaq_info_etf.json", "nasdaq_summary.json", "nasdaq_historical.json", "nasdaq_option_chain.json",
          "nasdaq_news.json", "nasdaq_financials.json", "cnbc_quote.json", "tencent_kline.json", "tencent_minute.json",
          "eastmoney_quote.json", "eastmoney_kline.json"]:
    save(n, rd(n))
for n in ["sina_quote.txt", "tencent_quote.txt"]: save(n, rd(n))  # GBK bytes kept verbatim
ch = js("nasdaq_chart.json"); ch["data"]["chart"] = (ch["data"].get("chart") or [])[:80]; save("nasdaq_chart.json", ch)
t = js("sec_tickers.json")
save("sec_tickers.json", {k: v for k, v in t.items() if v["ticker"] in ("AAPL", "MSFT", "NVDA", "TSLA", "BRK-B", "GOOGL", "AMZN", "META")})
f = js("sec_companyfacts.json")
want = ["Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax", "GrossProfit", "OperatingIncomeLoss", "NetIncomeLoss",
        "EarningsPerShareDiluted", "Assets", "Liabilities", "StockholdersEquity", "NetCashProvidedByUsedInOperatingActivities",
        "PaymentsToAcquirePropertyPlantAndEquipment"]
g = f["facts"]["us-gaap"]; f["facts"] = {"us-gaap": {k: g[k] for k in want if k in g}}
for v in f["facts"]["us-gaap"].values():
    for u in v["units"]: v["units"][u] = [x for x in v["units"][u] if x.get("form") == "10-K"][-30:]
save("sec_companyfacts.json", f)
s = rd("sina_dailyk.txt").decode("utf-8", "replace")
m = re.search(r"\((\[.*\])\)", s, re.S); arr = json.loads(m.group(1))[-80:]
save("sina_dailyk.txt", ("/*<script>location.href='//sina.com';</script>*/\nvar _AAPL=(" + json.dumps(arr) + ");").encode())