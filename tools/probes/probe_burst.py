import json,gzip,urllib.request,urllib.error,time,re
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"
def get(u,h=None):
    t=time.perf_counter(); r=urllib.request.urlopen(urllib.request.Request(u,headers=h or {"User-Agent":UA,"Accept-Encoding":"gzip"}),timeout=30); b=r.read()
    if b[:2]==b"\x1f\x8b": b=gzip.decompress(b)
    return b,int((time.perf_counter()-t)*1000),dict(r.headers)
# Yahoo rate burst test: 15 rapid chart calls
codes=[]
for i in range(15):
    try: get("https://query1.finance.yahoo.com/v8/finance/chart/MSFT?range=5d&interval=5m"); codes.append(200)
    except urllib.error.HTTPError as e: codes.append(e.code)
    except Exception as e: codes.append(type(e).__name__)
print("yahoo burst",codes)
codes=[]
for i in range(10):
    try: get("https://api.nasdaq.com/api/quote/MSFT/info?assetclass=stocks",{"User-Agent":UA,"Accept":"application/json","Origin":"https://www.nasdaq.com","Referer":"https://www.nasdaq.com/","Accept-Encoding":"gzip"}); codes.append(200)
    except Exception as e: codes.append(str(e)[:30])
print("nasdaq burst",codes)


