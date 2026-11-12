# Captures the non-Yahoo/CBOE fixtures with the Windows TLS stack (same as .NET's SChannel).
# Nasdaq's CDN rejects Python urllib's TLS fingerprint (HTTP 403) since 2026-11-12, so these go through PowerShell.
# Raw responses land in scratch\fixtures-raw, then trim_fixtures.py writes the trimmed copies into the test project.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$raw = Join-Path $root 'scratch\fixtures-raw'
New-Item -ItemType Directory -Force $raw | Out-Null
$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36'
$nq = @{ 'Accept' = 'application/json, text/plain, */*'; 'Accept-Language' = 'en-US,en;q=0.9'; 'Origin' = 'https://www.nasdaq.com'; 'Referer' = 'https://www.nasdaq.com/' }
$sina = @{ 'Referer' = 'https://finance.sina.com.cn/' }
$secUa = 'MeiliTerminal/0.3 personal-use contact@example.com'
function Get-To($name, $url, $headers = @{}, $agent = $ua) {
    for ($i = 1; $i -le 4; $i++) {
        try {
            Invoke-WebRequest -UseBasicParsing $url -UserAgent $agent -Headers $headers -TimeoutSec 40 -OutFile (Join-Path $raw $name)
            $len = (Get-Item (Join-Path $raw $name)).Length
            if ($name -like 'nasdaq_*' -and (Get-Content (Join-Path $raw $name) -Raw) -match '"data":null') { throw 'nasdaq data=null' }
            '{0,-28} {1,9} bytes' -f $name, $len; return
        } catch { if ($i -eq 4) { "FAILED $name : $($_.Exception.Message)" } else { Start-Sleep -Milliseconds 1500 } }
    }
}
$N = 'https://api.nasdaq.com/api'
$today = (Get-Date).ToString('yyyy-MM-dd'); $from = (Get-Date).AddDays(-60).ToString('yyyy-MM-dd')
Get-To nasdaq_info_stock.json "$N/quote/AAPL/info?assetclass=stocks" $nq
Get-To nasdaq_info_etf.json "$N/quote/SPY/info?assetclass=etf" $nq
Get-To nasdaq_summary.json "$N/quote/AAPL/summary?assetclass=stocks" $nq
Get-To nasdaq_historical.json "$N/quote/AAPL/historical?assetclass=stocks&fromdate=$from&todate=$today&limit=60" $nq
Get-To nasdaq_chart.json "$N/quote/AAPL/chart?assetclass=stocks" $nq
Get-To nasdaq_option_chain.json "$N/quote/AAPL/option-chain?assetclass=stocks&limit=80&fromdate=all&todate=undefined&excode=oprac&callput=callput&money=all&type=all" $nq
Get-To nasdaq_news.json "$N/news/topic/articlebysymbol?q=aapl|stocks&offset=0&limit=10&fallback=true" $nq
Get-To nasdaq_financials.json "$N/company/AAPL/financials?frequency=1" $nq
Get-To cnbc_quote.json 'https://quote.cnbc.com/quote-html-webservice/restQuote/symbolType/symbol?symbols=AAPL%7CSPY&requestMethod=itv&noform=1&partnerId=2&fund=1&exthrs=1&output=json&events=1'
Get-To sec_tickers.json 'https://www.sec.gov/files/company_tickers.json' @{} $secUa
Get-To sec_companyfacts.json 'https://data.sec.gov/api/xbrl/companyfacts/CIK0000320193.json' @{} $secUa
Get-To sina_quote.txt 'https://hq.sinajs.cn/list=gb_aapl,gb_spy,gb_brk$b' $sina
Get-To sina_dailyk.txt 'https://stock.finance.sina.com.cn/usstock/api/jsonp.php/var%20_AAPL=/US_MinKService.getDailyK?symbol=aapl&_=1' $sina
Get-To tencent_quote.txt 'https://qt.gtimg.cn/q=usAAPL,usSPY'
Get-To tencent_kline.json 'https://web.ifzq.gtimg.cn/appstock/app/usfqkline/get?param=usAAPL.OQ,day,,,60,qfq'
Get-To tencent_minute.json 'https://web.ifzq.gtimg.cn/appstock/app/UsMinute/query?code=usAAPL.OQ'
Get-To eastmoney_quote.json 'https://push2.eastmoney.com/api/qt/stock/get?secid=105.AAPL&fields=f43,f44,f45,f46,f47,f48,f57,f58,f59,f60,f86,f169,f170,f116'
Get-To eastmoney_kline.json 'https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=105.AAPL&klt=101&fqt=1&lmt=60&end=20500101&fields1=f1,f2,f3&fields2=f51,f52,f53,f54,f55,f56,f57'