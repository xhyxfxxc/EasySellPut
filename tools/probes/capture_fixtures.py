# -*- coding: utf-8 -*-
"""Capture real responses as unit-test fixtures (trimmed to keep the repo small).
Usage: python tools/probes/capture_fixtures.py   -> writes tests/TyuStock.Data.Tests/Fixtures/*"""
import os, json, gzip, re, time, urllib.request, http.cookiejar, datetime as dt
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "tests", "TyuStock.Data.Tests", "Fixtures")
os.makedirs(OUT, exist_ok=True)
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"
B = {"User-Agent": UA, "Accept": "application/json, text/plain, */*", "Accept-Language": "en-US,en;q=0.9", "Accept-Encoding": "gzip"}
NQ = dict(B, Origin="https://www.nasdaq.com", Referer="https://www.nasdaq.com/")
SEC = {"User-Agent": "tyuStockApp research contact@example.com", "Accept-Encoding": "gzip"}
SINA = dict(B, Referer="https://finance.sina.com.cn/")
jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

def get(url, h=B, tries=3):
    for i in range(tries):
        try:
            r = op.open(urllib.request.Request(url, headers=h), timeout=30); b = r.read()
            if b[:2] == b"\x1f\x8b": b = gzip.decompress(b)
            return b
        except urllib.error.HTTPError as e:
            if url.startswith("https://fc.yahoo.com"): return b""
            if i == tries - 1: raise
        except Exception:
            if i == tries - 1: raise
        time.sleep(1.5)

def save(name, data):
    p = os.path.join(OUT, name)
    with open(p, "wb") as f: f.write(data if isinstance(data, bytes) else json.dumps(data, ensure_ascii=False).encode("utf-8"))
    print(f"{name:<34} {os.path.getsize(p):>8} bytes")

def nasdaq(url):
    for i in range(4):
        b = get(url, NQ); j = json.loads(b)
        if j.get("data"): return b
        time.sleep(1.5)
    return b

get("https://fc.yahoo.com/")
crumb = get("https://query2.finance.yahoo.com/v1/test/getcrumb", dict(B, Accept="text/plain")).decode().strip()
Y = "https://query2.finance.yahoo.com"
save("yahoo_quote.json", get(f"{Y}/v7/finance/quote?symbols=AAPL,SPY,TSLA,BRK-B&crumb={crumb}"))
save("yahoo_chart_1m.json", get("https://query1.finance.yahoo.com/v8/finance/chart/AAPL?interval=1m&range=1d&includePrePost=true"))
save("yahoo_chart_1d.json", get("https://query1.finance.yahoo.com/v8/finance/chart/SPY?interval=1d&range=3mo"))
save("yahoo_options.json", get(f"{Y}/v7/finance/options/AAPL?crumb={crumb}"))
save("yahoo_quotesummary.json", get(f"{Y}/v10/finance/quoteSummary/AAPL?modules=price,summaryDetail,defaultKeyStatistics,financialData,assetProfile,calendarEvents,quoteType&crumb={crumb}"))
types = "annualTotalRevenue,annualGrossProfit,annualOperatingIncome,annualNetIncome,annualDilutedEPS,annualTotalAssets,annualTotalLiabilitiesNetMinorityInterest,annualStockholdersEquity,annualOperatingCashFlow,annualCapitalExpenditure,annualFreeCashFlow"
save("yahoo_timeseries.json", get(f"{Y}/ws/fundamentals-timeseries/v1/finance/timeseries/AAPL?type={types}&period1=1420070400&period2={int(time.time())}"))
save("yahoo_search.json", get(f"{Y}/v1/finance/search?q=AAPL&quotesCount=8&newsCount=8"))

C = "https://cdn.cboe.com/api/global/delayed_quotes"
o = json.loads(get(f"{C}/options/AAPL.json"))
opts = o["data"]["options"]
exps = sorted({x["option"][-15:-9] for x in opts})[:2]
o["data"]["options"] = [x for x in opts if x["option"][-15:-9] in exps][:120]
save("cboe_options.json", o)
save("cboe_quote.json", get(f"{C}/quotes/AAPL.json"))
h = json.loads(get(f"{C}/charts/historical/AAPL.json")); h["data"] = h["data"][-60:]; save("cboe_historical.json", h)
i = json.loads(get(f"{C}/charts/intraday/AAPL.json")); i["data"] = i["data"][:60]; save("cboe_intraday.json", i)

N = "https://api.nasdaq.com/api"
today = dt.date.today(); frm = (today - dt.timedelta(days=60)).isoformat()
save("nasdaq_info_stock.json", nasdaq(f"{N}/quote/AAPL/info?assetclass=stocks"))
save("nasdaq_info_etf.json", nasdaq(f"{N}/quote/SPY/info?assetclass=etf"))
save("nasdaq_summary.json", nasdaq(f"{N}/quote/AAPL/summary?assetclass=stocks"))
save("nasdaq_historical.json", nasdaq(f"{N}/quote/AAPL/historical?assetclass=stocks&fromdate={frm}&todate={today}&limit=60"))
ch = json.loads(nasdaq(f"{N}/quote/AAPL/chart?assetclass=stocks")); ch["data"]["chart"] = (ch["data"].get("chart") or [])[:80]; save("nasdaq_chart.json", ch)
save("nasdaq_option_chain.json", nasdaq(f"{N}/quote/AAPL/option-chain?assetclass=stocks&limit=80&fromdate=all&todate=undefined&excode=oprac&callput=callput&money=all&type=all"))
save("nasdaq_news.json", get(f"{N}/news/topic/articlebysymbol?q=aapl|stocks&offset=0&limit=10&fallback=true", NQ))
save("nasdaq_financials.json", nasdaq(f"{N}/company/AAPL/financials?frequency=1"))

save("cnbc_quote.json", get("https://quote.cnbc.com/quote-html-webservice/restQuote/symbolType/symbol?symbols=AAPL%7CSPY&requestMethod=itv&noform=1&partnerId=2&fund=1&exthrs=1&output=json&events=1"))

t = json.loads(get("https://www.sec.gov/files/company_tickers.json", SEC))
keep = {k: v for k, v in t.items() if v["ticker"] in ("AAPL", "MSFT", "NVDA", "TSLA", "BRK-B", "GOOGL", "AMZN", "META")}
save("sec_tickers.json", keep)
f = json.loads(get("https://data.sec.gov/api/xbrl/companyfacts/CIK0000320193.json", SEC))
want = ["Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax", "GrossProfit", "OperatingIncomeLoss", "NetIncomeLoss",
        "EarningsPerShareDiluted", "Assets", "Liabilities", "StockholdersEquity", "NetCashProvidedByUsedInOperatingActivities",
        "PaymentsToAcquirePropertyPlantAndEquipment"]
g = f["facts"]["us-gaap"]
f["facts"] = {"us-gaap": {k: g[k] for k in want if k in g}}
for k, v in f["facts"]["us-gaap"].items():
    for u in v["units"]: v["units"][u] = [x for x in v["units"][u] if x.get("form") == "10-K"][-30:]
save("sec_companyfacts.json", f)

save("sina_quote.txt", get("https://hq.sinajs.cn/list=gb_aapl,gb_spy,gb_brk$b", SINA))
s = get("https://stock.finance.sina.com.cn/usstock/api/jsonp.php/var%20_AAPL=/US_MinKService.getDailyK?symbol=aapl&_=1", SINA).decode("utf-8", "replace")
m = re.search(r"\((\[.*\])\)", s, re.S); arr = json.loads(m.group(1))[-80:]
save("sina_dailyk.txt", ("/*<script>location.href='//sina.com';</script>*/\nvar _AAPL=(" + json.dumps(arr) + ");").encode())
save("tencent_quote.txt", get("https://qt.gtimg.cn/q=usAAPL,usSPY"))
save("tencent_kline.json", get("https://web.ifzq.gtimg.cn/appstock/app/usfqkline/get?param=usAAPL.OQ,day,,,60,qfq"))
save("tencent_minute.json", get("https://web.ifzq.gtimg.cn/appstock/app/UsMinute/query?code=usAAPL.OQ"))
save("eastmoney_quote.json", get("https://push2.eastmoney.com/api/qt/stock/get?secid=105.AAPL&fields=f43,f44,f45,f46,f47,f48,f57,f58,f59,f60,f86,f169,f170,f116"))
save("eastmoney_kline.json", get("https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=105.AAPL&klt=101&fqt=1&lmt=60&end=20500101&fields1=f1,f2,f3&fields2=f51,f52,f53,f54,f55,f56,f57"))
print("done")
