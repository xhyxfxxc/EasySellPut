# -*- coding: utf-8 -*-
"""Probe free, no-key US market data sources from this PC.
Usage: python probe_sources.py [direct|proxy|both]
Writes raw bodies to ./out/<mode>/<name>.txt and a summary JSON ./out/summary_<mode>.json
Only uses Python stdlib."""
import sys, os, time, json, gzip, zlib, ssl, urllib.request, urllib.error, http.cookiejar, concurrent.futures as cf, datetime as dt

PROXY = "http://127.0.0.1:45444"
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36")
BROWSER = {"User-Agent": UA, "Accept": "application/json, text/plain, */*",
           "Accept-Language": "en-US,en;q=0.9", "Accept-Encoding": "gzip, deflate"}
NASDAQ = dict(BROWSER, **{"Origin": "https://www.nasdaq.com", "Referer": "https://www.nasdaq.com/"})
SEC = {"User-Agent": "MeiliTerminal research contact@example.com", "Accept-Encoding": "gzip, deflate"}
SINA = dict(BROWSER, Referer="https://finance.sina.com.cn/")
HERE = os.path.dirname(os.path.abspath(__file__))
today = dt.date.today()
d_from = (today - dt.timedelta(days=40)).isoformat()

def opener(mode, jar):
    handlers = [urllib.request.HTTPCookieProcessor(jar), urllib.request.HTTPSHandler(context=ssl.create_default_context())]
    if mode == "proxy":
        handlers.append(urllib.request.ProxyHandler({"http": PROXY, "https": PROXY}))
    else:
        handlers.append(urllib.request.ProxyHandler({}))
    return urllib.request.build_opener(*handlers)

def fetch(op, url, headers, timeout=20):
    t0 = time.perf_counter()
    req = urllib.request.Request(url, headers=headers)
    status, body, err, hdrs = None, b"", None, {}
    try:
        with op.open(req, timeout=timeout) as r:
            status = r.status; body = r.read(); hdrs = dict(r.headers)
    except urllib.error.HTTPError as e:
        status = e.code; hdrs = dict(e.headers or {})
        try: body = e.read()
        except Exception: pass
    except Exception as e:
        err = f"{type(e).__name__}: {e}"
    enc = next((v for k, v in hdrs.items() if k.lower() == "content-encoding"), "").lower()
    try:
        if enc == "gzip" or body[:2] == b"\x1f\x8b": body = gzip.decompress(body)
        elif enc == "deflate": body = zlib.decompress(body)
    except Exception: pass
    ms = int((time.perf_counter() - t0) * 1000)
    return status, body, err, ms, hdrs

def probes(crumb):
    c = f"&crumb={crumb}" if crumb else ""
    Y1, Y2 = "https://query1.finance.yahoo.com", "https://query2.finance.yahoo.com"
    N = "https://api.nasdaq.com/api"
    return [
        ("yahoo_chart_q1_1m", f"{Y1}/v8/finance/chart/AAPL?range=1d&interval=1m&includePrePost=true", BROWSER),
        ("yahoo_chart_q2_1d", f"{Y2}/v8/finance/chart/SPY?range=1y&interval=1d", BROWSER),
        ("yahoo_quote_v7", f"{Y2}/v7/finance/quote?symbols=AAPL,SPY,TSLA{c}", BROWSER),
        ("yahoo_options_v7", f"{Y2}/v7/finance/options/AAPL?{c[1:]}", BROWSER),
        ("yahoo_quoteSummary", f"{Y2}/v10/finance/quoteSummary/AAPL?modules=price,summaryDetail,defaultKeyStatistics,financialData,assetProfile,calendarEvents{c}", BROWSER),
        ("yahoo_search_news", f"{Y2}/v1/finance/search?q=AAPL&quotesCount=5&newsCount=5", BROWSER),
        ("yahoo_spark", f"{Y2}/v7/finance/spark?symbols=AAPL,SPY&range=1d&interval=5m", BROWSER),
        ("yahoo_timeseries", f"{Y2}/ws/fundamentals-timeseries/v1/finance/timeseries/AAPL?type=annualTotalRevenue,annualNetIncome&period1=1500000000&period2={int(time.time())}", BROWSER),
        ("yahoo_rss_news", "https://feeds.finance.yahoo.com/rss/2.0/headline?s=AAPL&region=US&lang=en-US", BROWSER),
        ("cboe_options", "https://cdn.cboe.com/api/global/delayed_quotes/options/AAPL.json", BROWSER),
        ("cboe_quote", "https://cdn.cboe.com/api/global/delayed_quotes/quotes/AAPL.json", BROWSER),
        ("cboe_hist", "https://cdn.cboe.com/api/global/delayed_quotes/charts/historical/AAPL.json", BROWSER),
        ("cboe_intraday", "https://cdn.cboe.com/api/global/delayed_quotes/charts/intraday/AAPL.json", BROWSER),
        ("cboe_spx_options", "https://cdn.cboe.com/api/global/delayed_quotes/options/_SPX.json", BROWSER),
        ("nasdaq_info_stock", f"{N}/quote/AAPL/info?assetclass=stocks", NASDAQ),
        ("nasdaq_info_etf", f"{N}/quote/SPY/info?assetclass=etf", NASDAQ),
        ("nasdaq_summary", f"{N}/quote/AAPL/summary?assetclass=stocks", NASDAQ),
        ("nasdaq_option_chain", f"{N}/quote/AAPL/option-chain?assetclass=stocks&limit=60", NASDAQ),
        ("nasdaq_historical", f"{N}/quote/AAPL/historical?assetclass=stocks&fromdate={d_from}&todate={today}&limit=30", NASDAQ),
        ("nasdaq_chart_intraday", f"{N}/quote/AAPL/chart?assetclass=stocks", NASDAQ),
        ("nasdaq_realtime_trades", f"{N}/quote/AAPL/realtime-trades?&limit=20", NASDAQ),
        ("nasdaq_news", f"{N}/news/topic/articlebysymbol?q=aapl|stocks&offset=0&limit=5&fallback=true", NASDAQ),
        ("nasdaq_financials", f"{N}/company/AAPL/financials?frequency=1", NASDAQ),
        ("stooq_daily_csv", "https://stooq.com/q/d/l/?s=aapl.us&i=d", BROWSER),
        ("stooq_quote_csv", "https://stooq.com/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv", BROWSER),
        ("sec_tickers", "https://www.sec.gov/files/company_tickers.json", SEC),
        ("sec_companyfacts", "https://data.sec.gov/api/xbrl/companyfacts/CIK0000320193.json", SEC),
        ("sec_submissions", "https://data.sec.gov/submissions/CIK0000320193.json", SEC),
        ("cnbc_quote", "https://quote.cnbc.com/quote-html-webservice/restQuote/symbolType/symbol?symbols=AAPL%7CSPY&requestMethod=itv&noform=1&partnerId=2&fund=1&exthrs=1&output=json&events=1", BROWSER),
        ("google_finance_page", "https://www.google.com/finance/quote/AAPL:NASDAQ", dict(BROWSER, Accept="text/html")),
        ("finviz_page", "https://finviz.com/quote.ashx?t=AAPL", dict(BROWSER, Accept="text/html")),
        ("marketwatch_page", "https://www.marketwatch.com/investing/stock/aapl", dict(BROWSER, Accept="text/html")),
        ("sina_us_quote", "https://hq.sinajs.cn/list=gb_aapl,gb_spy", SINA),
        ("sina_us_dailyk", "https://stock.finance.sina.com.cn/usstock/api/jsonp.php/var%20_AAPL=/US_MinKService.getDailyK?symbol=aapl&_=1", SINA),
        ("tencent_us_quote", "https://qt.gtimg.cn/q=usAAPL,usSPY", BROWSER),
        ("tencent_us_kline", "https://web.ifzq.gtimg.cn/appstock/app/usfqkline/get?param=usAAPL.OQ,day,,,30,qfq", BROWSER),
        ("tencent_us_minute", "https://web.ifzq.gtimg.cn/appstock/app/UsMinute/query?code=usAAPL.OQ", BROWSER),
        ("eastmoney_us_quote", "https://push2.eastmoney.com/api/qt/stock/get?secid=105.AAPL&fields=f43,f44,f45,f46,f47,f48,f57,f58,f60,f86,f170", BROWSER),
        ("eastmoney_us_kline", "https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=105.AAPL&klt=101&fqt=1&lmt=20&end=20500101&fields1=f1,f2,f3&fields2=f51,f52,f53,f54,f55,f56,f57", BROWSER),
    ]

def run(mode):
    outdir = os.path.join(HERE, "out", mode); os.makedirs(outdir, exist_ok=True)
    jar = http.cookiejar.CookieJar(); op = opener(mode, jar)
    results = []
    # Yahoo cookie + crumb
    s1, _, e1, ms1, _ = fetch(op, "https://fc.yahoo.com/", BROWSER)
    s2, b2, e2, ms2, _ = fetch(op, "https://query2.finance.yahoo.com/v1/test/getcrumb", dict(BROWSER, Accept="text/plain"))
    crumb = b2.decode("utf-8", "replace").strip() if s2 == 200 else ""
    results.append(dict(name="yahoo_cookie_fc", status=s1, ms=ms1, err=e1, cookies=[c.name for c in jar]))
    results.append(dict(name="yahoo_getcrumb", status=s2, ms=ms2, err=e2, crumb_ok=bool(crumb) and len(crumb) < 40))
    def one(p):
        name, url, h = p
        st, body, err, ms, hdrs = fetch(op, url, h)
        with open(os.path.join(outdir, name + ".txt"), "wb") as f: f.write(body[:400000])
        return dict(name=name, url=url, status=st, ms=ms, bytes=len(body), err=err,
                    ctype=hdrs.get("Content-Type"), ratelimit={k: v for k, v in hdrs.items() if "rate" in k.lower() or "retry" in k.lower()},
                    head=body[:160].decode("utf-8", "replace"))
    with cf.ThreadPoolExecutor(6) as ex:
        results += list(ex.map(one, probes(crumb)))
    with open(os.path.join(HERE, "out", f"summary_{mode}.json"), "w", encoding="utf-8") as f:
        json.dump(dict(mode=mode, at=dt.datetime.now().isoformat(), results=results), f, ensure_ascii=False, indent=1)
    for r in results:
        print(f"[{mode}] {r['name']:<24} status={r.get('status')} {r.get('ms')}ms bytes={r.get('bytes','-')} err={r.get('err') or ''}")

if __name__ == "__main__":
    m = sys.argv[1] if len(sys.argv) > 1 else "both"
    for mode in (["direct", "proxy"] if m == "both" else [m]):
        run(mode)
