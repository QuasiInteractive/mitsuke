"""Generates infra/workbook.json, the Azure Monitor workbook (dashboard) that infra/main.bicep deploys.

    python scripts/workbook.py              # rewrite infra/workbook.json
    python scripts/workbook.py --validate   # also run every query against the live workspace (needs az login)

Queries live here rather than hand-edited JSON so they can be checked before they ship.
"""
import json
import os
import subprocess
import sys

WORKSPACE = "__WORKSPACE_ID__"  # replaced with the Log Analytics workspace's resource id by main.bicep
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

Q = {}
Q["tiles"] = r"""
let runs = AppRequests | where Name == 'Collect';
union
  (runs | summarize v = todouble(count()) | extend Metric = 'Collection runs', Order = 1),
  (runs | summarize v = round(100.0 * countif(Success == true) / count(), 1) | extend Metric = 'Run success %', Order = 2),
  (AppTraces | where Properties.EventName == 'LogCollected' | summarize v = todouble(sum(toint(Properties.New))) | extend Metric = 'New lots seen', Order = 3),
  (AppTraces | where Properties.EventName == 'LogSent' | summarize v = todouble(count()) | extend Metric = 'Alerts delivered', Order = 4),
  (AppDependencies | where Target has 'thecarapi' | summarize v = round(percentile(DurationMs, 95) / 1000, 1) | extend Metric = 'TheCarApi p95 (s)', Order = 5),
  (AppTraces | where Message has 'OnCircuitOpened' | summarize v = todouble(count()) | extend Metric = 'Circuit breaks', Order = 6)
| project Metric, Value = coalesce(v, 0.0), Order
| order by Order asc
"""
Q["runs"] = r"""
AppRequests
| where Name in ('ScheduleCollection', 'Collect', 'SendAlert')
| summarize Succeeded = countif(Success == true), Failed = countif(Success == false) by bin(TimeGenerated, {TimeRange:grain})
"""
Q["resilience"] = r"""
AppTraces
| where Message startswith 'Resilience event occurred'
| extend Event = extract(@"EventName: '([^']+)'", 1, Message), Dependency = extract(@"Source: '([^-/']+)", 1, Message)
| summarize Count = count() by Event = strcat(Dependency, ' · ', Event), bin(TimeGenerated, {TimeRange:grain})
"""
Q["deps"] = r"""
AppDependencies
| where DependencyType in ('HTTP', 'Http')
| summarize p95_ms = percentile(DurationMs, 95) by Target, bin(TimeGenerated, {TimeRange:grain})
"""
Q["depfail"] = r"""
AppDependencies
| where DependencyType in ('HTTP', 'Http')
| summarize Calls = count(), Failed = countif(Success == false), p50_ms = round(percentile(DurationMs, 50)), p95_ms = round(percentile(DurationMs, 95)) by Target, AppRoleName
| extend FailurePct = round(100.0 * Failed / Calls, 1)
| project Target, Caller = iff(AppRoleName has 'api', 'API', 'Pipeline'), Calls, Failed, FailurePct, p50_ms, p95_ms
| order by Calls desc
"""
Q["watchlists"] = r"""
AppTraces
| where Properties.EventName == 'LogCollected'
| summarize arg_max(TimeGenerated, Properties), Runs = count() by Watchlist = tostring(Properties.Watchlist)
| project Watchlist, LastRun = TimeGenerated, Runs, OnAuction = toint(Properties.Seen), Matching = toint(Properties.Matched), WhyNot = tostring(Properties.Rejections)
| order by Matching desc, Watchlist asc
"""
Q["alerts"] = r"""
AppTraces
| where Properties.EventName in ('LogSent', 'LogSendFailed')
| summarize Count = count() by Channel = tostring(Properties.Channel), Outcome = iff(Properties.EventName == 'LogSent', 'Delivered', 'Failed')
| order by Count desc
"""
Q["api"] = r"""
AppRequests
| where AppRoleName has 'api' and Name != 'GET /api/health' and Name !has 'robots'
| summarize Requests = count(), Errors = countif(toint(ResultCode) >= 500), p95_ms = round(percentile(DurationMs, 95)) by Route = Name
| order by Requests desc
"""
Q["exceptions"] = r"""
AppExceptions
| summarize Count = count(), Last = max(TimeGenerated) by Exception = ExceptionType, Message = substring(OuterMessage, 0, 120), Role = iff(AppRoleName has 'api', 'API', 'Pipeline')
| order by Count desc
| take 15
"""
Q["ingest"] = r"""
Usage
| where IsBillable == true
| summarize MB = sum(Quantity) by DataType, bin(TimeGenerated, 1d)
"""


def md(text):
    return {"type": 1, "content": {"json": text}, "name": "intro"}


def kql(key, title, viz, width=None, extra=None, size=0):
    content = {
        "version": "KqlItem/1.0",
        "query": Q[key].strip(),
        "size": size,
        "title": title,
        "timeContextFromParameter": "TimeRange",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces",
        "crossComponentResources": [WORKSPACE],
        "visualization": viz,
    }
    content.update(extra or {})
    item = {"type": 3, "content": content, "name": key}
    if width:
        item["customWidth"] = str(width)
    return item


TILES = {
    "tileSettings": {
        "titleContent": {"columnMatch": "Metric", "formatter": 1},
        "leftContent": {"columnMatch": "Value", "formatter": 12, "formatOptions": {"palette": "blue"},
                        "numberFormat": {"unit": 0, "options": {"maximumFractionDigits": 1}}},
        "showBorder": True,
    }
}

TIME_RANGE = {
    "type": 9,
    "name": "parameters",
    "content": {
        "version": "KqlParameterItem/1.0",
        "style": "pills",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces",
        "parameters": [{
            "id": "9d2c1f6e-0d7a-4f3e-9a51-6c3f2b8e7a10",
            "version": "KqlParameterItem/1.0",
            "name": "TimeRange",
            "label": "Time range",
            "type": 4,
            "isRequired": True,
            "value": {"durationMs": 86400000},
            "typeSettings": {"allowCustom": True, "selectableValues": [
                {"durationMs": 3600000}, {"durationMs": 21600000}, {"durationMs": 86400000},
                {"durationMs": 259200000}, {"durationMs": 604800000}]},
        }],
    },
}


def workbook():
    return {
        "version": "Notebook/1.0",
        "items": [
            md("## Mitsuke · pipeline health\n"
               "Japanese auction lots → **Collect** (timer every 10 min, one queue message per watchlist: search, store, "
               "match, landed cost) → **SendAlert** (queue; each channel claimed once) → email · Web Push · Discord.\n\n"
               "Every external call runs through timeout → retry → circuit breaker, so an outage shows up below as "
               "resilience events and failed runs that recover on their own."),
            TIME_RANGE,
            kql("tiles", "At a glance", "tiles", extra=TILES, size=4),
            kql("runs", "Function runs", "barchart", width=50),
            kql("resilience", "Resilience events (timeouts, retries, circuit breaks)", "barchart", width=50),
            kql("deps", "Outbound calls: p95 latency (ms)", "timechart", width=50),
            kql("depfail", "Outbound calls: volume, failures, latency", "table", width=50),
            kql("watchlists", "Watchlists: latest run, and why lots didn't match", "table"),
            kql("alerts", "Alerts by channel", "table", width=35),
            kql("api", "API routes", "table", width=65),
            kql("exceptions", "Exceptions", "table"),
            kql("ingest", "Billable ingestion per day (MB), against the 100 MB daily cap", "barchart"),
        ],
        "fallbackResourceIds": [WORKSPACE],
        "$schema": "https://github.com/Microsoft/Application-Insights-Workbooks/blob/master/schema/workbook.json",
    }


def validate():
    """Runs each query over the last day against the real workspace; exits non-zero if any fails."""
    wid = subprocess.run("az monitor log-analytics workspace show -g mitsuke-rg -n mitsuke-logs --query customerId -o tsv",
                         capture_output=True, text=True, shell=True).stdout.strip()
    failed = False
    for key, query in Q.items():
        query = query.replace("{TimeRange:grain}", "1h")
        with open(os.path.join(ROOT, ".workbook-query.kql"), "w", encoding="utf-8") as f:
            f.write(query)
        r = subprocess.run(["az", "monitor", "log-analytics", "query", "-w", wid, "--timespan", "P1D", "-o", "json",
                            "--analytics-query", "@" + os.path.join(ROOT, ".workbook-query.kql")],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", shell=(os.name == "nt"),
                           env={**os.environ, "PYTHONIOENCODING": "utf-8"})  # az on Windows otherwise prints cp1252
        try:
            rows = json.loads(r.stdout)
            print(f"  ok    {key:11} {len(rows):3} rows  {json.dumps(rows[0], ensure_ascii=False)[:110] if rows else ''}")
        except (json.JSONDecodeError, TypeError, ValueError):
            failed = True
            print(f"  FAIL  {key:11} {(r.stderr.strip().splitlines() or ['?'])[-1][:200]}")
    os.remove(os.path.join(ROOT, ".workbook-query.kql"))
    return not failed


if __name__ == "__main__":
    path = os.path.join(ROOT, "infra", "workbook.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(workbook(), f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("wrote infra/workbook.json")
    if "--validate" in sys.argv and not validate():
        sys.exit(1)
