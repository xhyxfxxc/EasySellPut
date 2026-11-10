# -*- coding: utf-8 -*-
"""Extract key fields from probe outputs: python analyze_probes.py [direct|proxy]"""
import sys, os, json, gzip, re, csv, io
HERE = os.path.dirname(os.path.abspath(__file__))
mode = sys.argv[1] if len(sys.argv) > 1 else "direct"
D = os.path.join(HERE, "out", mode)
def raw(n):
    p = os.path.join(D, n + ".txt")
    if not os.path.exists(p): return b""
    b = open(p, "rb").read()
    if b[:2] == b"\x1f\x8b":
        try: b = gzip.decompress(b)
        except Exception: pass
    return b
def js(n):
    try: return json.loads(raw(n))
    except Exception as e: return {"_parse_error": str(e), "_head": raw(n)[:200].decode("utf-8", "replace")}
def keys(o, depth=1):
    return sorted(o.keys()) if isinstance(o, dict) else type(o).__name__
def out(title, v): print(f"--- {title}\n{json.dumps(v, ensure_ascii=False, default=str)[:1500]}")

c = js("yahoo_chart_q1_1m")
try:
    r = c["chart"]["result"][0]; m = r["meta"]
    out("yahoo_chart 1m meta", {k: m.get(k) for k in ["symbol","regularMarketPrice","regularMarketTime","exchangeTimezoneName","dataGranularity","currentTradingPeriod","hasPrePostMarketData"]})
    out("yahoo_chart 1m bars", {"n": len(r.get("timestamp") or []), "last_ts": (r.get("timestamp") or [None])[-1]})
except Exception as e: out("yahoo_chart err", [str(e), c])
c = js("yahoo_chart_q2_1d")
try: r = c["chart"]["result"][0]; out("yahoo_chart 1d", {"n": len(r["timestamp"]), "indicators": keys(r["indicators"]), "adj": "adjclose" in r["indicators"]})
except Exception as e: out("yahoo_chart_1d err", [str(e), c])
q = js("yahoo_quote_v7")
try:
    res = q["quoteResponse"]["result"]; out("yahoo_quote_v7 fields", {"n": len(res), "fields": keys(res[0])})
    out("yahoo_quote_v7 sample", {k: res[0].get(k) for k in ["symbol","regularMarketPrice","regularMarketTime","preMarketPrice","postMarketPrice","bid","ask","quoteSourceName","exchangeDataDelayedBy","marketState"]})
except Exception as e: out("yahoo_quote_v7 err", [str(e), q])
o = js("yahoo_options_v7")
try:
    r = o["optionChain"]["result"][0]; calls = r["options"][0]["calls"]
    out("yahoo_options", {"expirations": len(r["expirationDates"]), "strikes": len(r["strikes"]), "calls_n": len(calls), "contract_fields": keys(calls[0]) if calls else None, "sample": calls[len(calls)//2] if calls else None})
except Exception as e: out("yahoo_options err", [str(e), o])
s = js("yahoo_quoteSummary")
try: r = s["quoteSummary"]["result"][0]; out("yahoo_quoteSummary modules", {k: (keys(v)[:25] if isinstance(v, dict) else v) for k, v in r.items()})
except Exception as e: out("yahoo_quoteSummary err", [str(e), s])
s = js("yahoo_search_news")
try: out("yahoo_search", {"quotes": [x.get("symbol") for x in s.get("quotes", [])], "news": [(x.get("title"), x.get("publisher"), x.get("providerPublishTime")) for x in s.get("news", [])][:3]})
except Exception as e: out("yahoo_search err", [str(e), s])
out("yahoo_spark", str(js("yahoo_spark"))[:300])
out("yahoo_timeseries", str(js("yahoo_timeseries"))[:400])
rss = raw("yahoo_rss_news").decode("utf-8", "replace"); out("yahoo_rss", {"items": rss.count("<item>"), "titles": re.findall(r"<title>(.*?)</title>", rss)[:3]})

o = js("cboe_options")
try:
    d = o["data"]; opts = d["options"]
    out("cboe_options", {"timestamp": o.get("timestamp"), "underlying_keys": [k for k in d if k != "options"], "n_contracts": len(opts), "contract_fields": keys(opts[0]), "sample": opts[len(opts)//2]})
except Exception as e: out("cboe_options err", [str(e)])
o = js("cboe_spx_options")
try: out("cboe_spx_options", {"n": len(o["data"]["options"])})
except Exception as e: out("cboe_spx err", str(e))
h = js("cboe_hist")
try: d = h["data"]; out("cboe_hist", {"n": len(d), "first": d[0], "last": d[-1]})
except Exception as e: out("cboe_hist err", [str(e), str(h)[:300]])
h = js("cboe_intraday")
try: d = h["data"]; out("cboe_intraday", {"n": len(d), "first": d[0], "last": d[-1]})
except Exception as e: out("cboe_intraday err", [str(e), str(h)[:300]])

for n in ["nasdaq_summary", "nasdaq_historical", "nasdaq_news", "nasdaq_financials"]:
    out(n, str(js(n))[:700])
oc = js("nasdaq_option_chain")
try:
    d = oc["data"]; rows = d["table"]["rows"]
    out("nasdaq_option_chain", {"total": d.get("totalRecord"), "lastTrade": d.get("lastTrade"), "headers": d["table"].get("headers"), "n_rows": len(rows), "sample": rows[3] if len(rows) > 3 else rows})
except Exception as e: out("nasdaq_option_chain err", [str(e), str(oc)[:500]])
ch = js("nasdaq_chart_intraday")
try: d = ch["data"]; out("nasdaq_chart", {"keys": keys(d), "n": len(d.get("chart") or []), "last": (d.get("chart") or [None])[-1]})
except Exception as e: out("nasdaq_chart err", [str(e), str(ch)[:300]])

out("stooq_daily_csv", raw("stooq_daily_csv").decode("utf-8", "replace")[:400])
t = js("sec_tickers"); out("sec_tickers", {"n": len(t) if isinstance(t, dict) else t, "first": t.get("0") if isinstance(t, dict) else None})
f = js("sec_companyfacts")
try: out("sec_companyfacts", {"entity": f.get("entityName"), "taxonomies": keys(f["facts"]), "us-gaap_n": len(f["facts"].get("us-gaap", {}))})
except Exception as e: out("sec_companyfacts err", str(e))
sb = js("sec_submissions"); out("sec_submissions", {"name": sb.get("name"), "recent_forms": (sb.get("filings", {}).get("recent", {}).get("form") or [])[:8]})
cn = js("cnbc_quote")
try:
    q = cn["FormattedQuoteResult"]["FormattedQuote"][0]
    out("cnbc_quote", {"fields": keys(q), "sample": {k: q.get(k) for k in ["symbol","last","last_time","realTime","curmktstatus","ExtendedMktQuote"]}})
except Exception as e: out("cnbc_quote err", [str(e), str(cn)[:400]])
g = raw("google_finance_page").decode("utf-8", "replace")
out("google_finance", {"has_price_class": 'data-last-price' in g, "price": re.findall(r'data-last-price="([\d.]+)"', g)[:2], "consent": "consent" in g[:5000].lower()})
fv = raw("finviz_page").decode("utf-8", "replace")
out("finviz", {"title": re.findall(r"<title>(.*?)</title>", fv)[:1], "has_snapshot": "snapshot-table2" in fv or "snapshot-td2" in fv, "news_table": "news-table" in fv})
out("marketwatch", raw("marketwatch_page")[:300].decode("utf-8", "replace"))
out("sina_dailyk", raw("sina_us_dailyk")[:300].decode("utf-8", "replace") + " ... " + raw("sina_us_dailyk")[-200:].decode("utf-8", "replace"))
out("tencent_kline", raw("tencent_us_kline")[:600].decode("utf-8", "replace"))
out("tencent_minute", raw("tencent_us_minute")[:400].decode("utf-8", "replace"))
out("eastmoney_kline", raw("eastmoney_us_kline")[:700].decode("utf-8", "replace"))
