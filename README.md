# Apache Balancer Manager

A modern dashboard for the `mod_proxy_balancer` **balancer-manager** pages of one or more Apache HTTPD 2.4 servers.

Apache ships a functional but bare balancer-manager: one page per server, no history, no cross-server view, and a form that answers `HTTP 200` whether or not it actually applied your change. This project puts a single pool-first dashboard in front of all of your Apache instances, with live graphs, one-click worker actions, and a REST API you can automate against.

Nothing is installed on the Apache servers: the backend simply scrapes and drives the same `/balancer-manager` pages you already have. One [optional add-on](#precise-traffic-counters-optional) does install a small script, for those who want byte-exact traffic figures instead of the rounded ones Apache prints.

![The pool overview across two Apache servers](docs/images/dashboard.jpg)

---

## Contents

- [Why](#why)
- [Features](#features)
- [Screenshots](#screenshots)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Apache configuration](#apache-configuration)
- [Precise traffic counters (optional)](#precise-traffic-counters-optional)
- [Configuration](#configuration)
- [Running locally](#running-locally)
- [Docker](#docker)
- [Deploying the dashboard](#deploying-the-dashboard)
- [REST API](#rest-api)
- [Security](#security)
- [How it works](#how-it-works)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)

---

## Why

If you run more than one Apache reverse proxy in front of the same backends, the stock balancer-manager makes routine work tedious:

- **One page per server.** Taking a backend out of rotation means repeating the same click on every proxy, in a separate browser tab, and hoping you did not miss one.
- **No history.** The page shows counters, not rates. "Is this worker actually draining?" is a question you answer by reloading and doing arithmetic in your head.
- **Silent failures.** Apache 2.4's cross-site check (`AH10187`) rejects modification requests whose `Referer` is not the manager page — by returning `HTTP 200` and quietly doing nothing.
- **Pool names, not services.** The page is organised by server; when you are draining a backend you think in pools.

This dashboard inverts that: **pools first, servers as a column inside them**, rates instead of raw counters, and every action verified against Apache's own post-action HTML before it is reported as successful.

## Features

- **Pool-first, multi-server view.** Every balancer pool across every configured Apache server on one page, with the same worker on `lb-01` and `lb-02` shown on adjacent rows.
- **Partial success.** An unreachable Apache server is reported in a banner; the reachable ones still render. One dead proxy never blanks the dashboard.
- **One-click worker actions.** Enable/disable, drain, and hot standby, applied per worker per server.
- **Verified actions.** Every change is confirmed against the status flags Apache reports *after* the POST, so a silently-ignored request surfaces as an error instead of a false success.
- **Live graphs per pool.** Expand a pool to get request rate, in-flight requests, and byte throughput per worker, built client-side from the polling history.
- **Adaptive rate windows.** The moving-average windows scale to the history actually on hand, and the captions name the window that was really measured.
- **Byte-exact traffic, if you want it.** An [optional add-on](#precise-traffic-counters-optional) on the Apache side replaces the rounded `To`/`From` columns with counters accurate to the byte, and the byte graph drops from a multi-minute window to the same 15 seconds as the others.
- **Auto-refresh at 1s / 3s / 5s / 15s**, with a manual refresh button and a last-updated stamp.
- **Pinned pools.** Pin the pools you watch to the top of the list; pins persist in `localStorage`.
- **On-screen console.** A terminal-style log in the header records every action and its outcome, so you have a trail of what you just did.
- **Copy-to-clipboard worker URLs**, with a fallback for non-HTTPS deployments.
- **Swagger UI** over the whole REST API, for scripting and CI-driven maintenance windows.

## Screenshots

### Pool overview

All pools from every configured server, aggregated, with health badges summarising worker state. Pinned pools float to the top.

![Pool overview](docs/images/dashboard.jpg)

### Expanded pool: workers and live graphs

Expanding a pool reveals its workers — the same backend on each Apache server, side by side — and starts three live graphs fed by the auto-refresh.

![An expanded pool with its worker table and live charts](docs/images/pool-expanded.jpg)

### Live metrics

Requests per minute, requests in flight, and bytes per second, one coloured line per worker. The graphs start empty when you expand a pool and fill in from there; nothing is stored server-side.

![Close-up of the three live charts](docs/images/charts.png)

### REST API

The BFF exposes the whole surface through Swagger UI.

![Swagger UI of the BFF](docs/images/swagger.jpg)

## Architecture

```
┌──────────────────────────┐        ┌──────────────────────────┐        ┌────────────────────┐
│  ApacheBalancerWasm      │  JSON  │  ApacheBalancerBFF       │  HTML  │  Apache HTTPD 2.4  │
│  Interface               │ ─────► │  (ASP.NET Core)          │ ─────► │  /balancer-manager │
│  Blazor WebAssembly      │ ◄───── │  scrape + POST + verify  │ ◄───── │  mod_proxy_balancer│
│  + Radzen                │        │                          │        │  (one per server)  │
└──────────────────────────┘        └──────────────────────────┘        └────────────────────┘
        static files                    REST + Swagger                      unmodified
```

- **`ApacheBalancerBFF`** — an ASP.NET Core backend-for-frontend. It fans out over the configured servers concurrently, parses each `/balancer-manager` page with HtmlAgilityPack, and exposes one aggregated JSON contract. It also owns the write path: fetch a fresh nonce, POST the modification, re-parse the response, and confirm the flags actually changed.
- **`ApacheBalancerWasmInterface`** — a Blazor WebAssembly single-page app (Radzen components). It reshapes the BFF's server-first payload into a pool-first tree, keeps a short in-browser history of the worker counters, and draws the graphs. It is a pure static site; it talks only to the BFF.
- **`Shared`** — the DTOs both sides bind to, so the contract cannot drift.

## Requirements

- **.NET 10 SDK** (the projects target `net10.0`).
- **Apache HTTPD 2.4** with `mod_proxy_balancer` and a reachable `/balancer-manager` handler on each server you want to manage.
- Network reachability from wherever the BFF runs to each Apache instance.
- Docker, only if you want to run the BFF as a container.

## Apache configuration

Each managed server needs the balancer-manager handler enabled. A minimal example:

```apache
LoadModule proxy_module            modules/mod_proxy.so
LoadModule proxy_http_module       modules/mod_proxy_http.so
LoadModule proxy_balancer_module   modules/mod_proxy_balancer.so
LoadModule slotmem_shm_module      modules/mod_slotmem_shm.so
LoadModule lbmethod_byrequests_module modules/mod_lbmethod_byrequests.so
LoadModule lbmethod_bybusyness_module modules/mod_lbmethod_bybusyness.so

<Proxy "balancer://web-public-backend">
    BalancerMember "https://web-public.app-1.example.com" hcmethod=GET hcuri=/
    BalancerMember "https://web-public.app-2.example.com" hcmethod=GET hcuri=/
    ProxySet lbmethod=bybusyness
</Proxy>

<Location "/balancer-manager">
    SetHandler balancer-manager
    Require ip 10.0.0.0/24        # the network the BFF runs on — never expose this publicly
</Location>
```

Two things are worth knowing:

- **Restrict `/balancer-manager` by IP.** It is an unauthenticated control surface for your load balancing. Limit it to the management network the BFF runs on.
- **The BFF sends a `Referer` header** matching the manager URL on every request. Apache 2.4's cross-site check (`AH10187`) logs a warning on reads without it, and silently discards writes. No Apache-side configuration is needed for this — it is handled for you.

## Precise traffic counters (optional)

**Everything else in this project works without this section.** Skip it and the dashboard reads `To`/`From` off the manager page exactly as before. Set it up and the byte graph gets an order of magnitude more resolution.

### The problem it solves

balancer-manager prints `To` and `From` through `apr_strfsize`, which compacts them to three significant characters. A worker sitting at `161M` does not move until it has carried another full megabyte, and past 1 GB the step is about 107 MB. At a few dozen KB/s that is one visible increment every several minutes — so the byte graph steps rather than flows, and the dashboard has to average it over a window of minutes just to keep a single step from reading as a spike.

That is Apache's resolution, not a rounding the dashboard introduces, and no amount of smoothing recovers it. `Elected` and `Busy` are printed as plain integers and are unaffected, which is why only the byte graph needs this.

### How it works

Apache already knows the exact byte counts — it just does not print them anywhere. `mod_proxy_balancer` puts the chosen worker in the request environment, and `mod_logio` provides the byte counts, so an access-log line can carry both. This add-on runs a small aggregator as a piped logger, keeps running totals in memory, and republishes them once a second as a small JSON file that Apache serves back to the BFF:

```
request ──► mod_proxy_balancer ──► backend
                 │
                 └─► access log line: "balancer://pool https://worker 512 8192"
                          │
                          ▼
                 balancer-bytes-agg  (piped logger, totals in memory)
                          │
                          └─► /run/apache2/balancer-metrics/balancer-bytes.json
                                       │
                                       └─► GET /balancer-bytes ──► the BFF
```

What it costs: one `perl` process per Apache server, a few hundred bytes of JSON on a tmpfs, and one log line per balancer-proxied request — which is never written to a disk. Requests that did not go through a balancer are not logged at all. The aggregator is written against `perl-base`, an Essential package on Debian, so there is nothing to install alongside it.

### Install

Both files live in [`tools/`](tools/).

```bash
# On each Apache server
sudo install -m 0755 -o root -g root tools/balancer-bytes-agg /usr/local/bin/balancer-bytes-agg
sudo install -m 0644 tools/balancer-bytes.conf /etc/apache2/conf-available/balancer-bytes.conf

# Restrict the endpoint to the network the BFF runs on
sudo editor /etc/apache2/conf-available/balancer-bytes.conf   # the Require ip line

sudo a2enconf balancer-bytes
sudo apache2ctl configtest && sudo systemctl restart apache2
```

`%I` and `%O` come from `mod_logio`. Debian's stock `combined` LogFormat already uses `%O`, so it is almost certainly loaded — confirm with `apache2ctl -M | grep logio` and `a2enmod logio` if it is not.

Then point the BFF at it by adding `MetricsPath` to that server:

```json
{
  "ApacheManagement": {
    "Servers": [
      { "Id": "lb-01", "Name": "Load Balancer Primary", "BaseUrl": "http://10.0.0.11", "MetricsPath": "/balancer-bytes" },
      { "Id": "lb-02", "Name": "Load Balancer Secondary", "BaseUrl": "http://10.0.0.12" }
    ]
  }
}
```

`MetricsPath` is per server, so you can roll this out to one load balancer and leave the others alone — `lb-02` above keeps working exactly as it did.

### Verify

```bash
# On the Apache server: exactly one aggregator, and the snapshot filling up.
# Read the file directly — the endpoint itself is restricted to the BFF's network.
pgrep -af balancer-bytes-agg
cat /run/apache2/balancer-metrics/balancer-bytes.json

# From the BFF host: the exact counters land in the API next to the verbatim cells
curl -s http://<bff>/api/balancer/status | grep -oE '"(To|From)Bytes":[0-9]+' | head
```

A healthy snapshot looks like this — `generated` should never be more than a second or two behind the current time, because the aggregator republishes on a timer whether or not any traffic came in:

```json
{
  "version": 1,
  "since": "2026-09-17T09:00:00Z",
  "generated": "2026-09-17T15:05:19Z",
  "workers": [
    { "balancer": "balancer://web-public-backend", "worker": "https://web-public.app-1.example.com",
      "requests": 13407, "to": 29360128, "from": 179306496 }
  ]
}
```

In the dashboard, the `To / From per second` caption is the tell: it reads **15s moving average** once the exact counters are in use, against the 1–5 minute window the rounded cells need.

### If it does not work

The BFF never fails a status poll over this: if the endpoint is missing, broken or stale it logs **one** warning naming the reason, falls back to the rounded cells, and logs again when the endpoint recovers. So check the BFF log first — it will tell you whether it got an HTTP error, a malformed document or a stale snapshot. The aggregator's own diagnostics go to stderr, which for a piped logger is the Apache **error log**.

### Notes and limits

- **`ProxyPass` beats `Alias`.** On a reverse-proxy vhost with a catch-all `ProxyPass /`, the snapshot path is handed straight to the backend, and what you get back is *the backend's* 404 rather than the file. The `Server:` header in the response tells you which one answered. Exclude the path before the catch-all, the same way `/balancer-manager` already is on that vhost:

  ```apache
  ProxyPass /balancer-bytes !
  ```

  It has to answer at **the same base URL the BFF already uses** for that server, because `MetricsPath` is resolved relative to `BaseUrl`. Serving it from a neighbouring vhost on another address is not enough.
- **One aggregator per Apache server.** Every piped `CustomLog` spawns its own process, and two aggregators writing the same snapshot would overwrite each other's totals. `pgrep -af balancer-bytes-agg` should show exactly one.
- **Watch out for vhost log directives.** A `<VirtualHost>` that declares any `CustomLog` of its own stops inheriting the server-level ones, so if your proxying vhost has its own access log, move the `CustomLog` line from the conf file into that vhost. The `LogFormat` line is still inherited and does not need to be repeated.
- **These are client-side bytes.** `%I`/`%O` count what passed between Apache and the *client*, where `To`/`From` count what passed between Apache and the *backend*. They agree closely in a plain reverse proxy, but they will diverge if `mod_deflate` compresses responses on the way out. Arguably the client-side figure is the more useful one; it is simply not the same figure.
- **An idle worker reads as zero, not as unknown.** The snapshot only lists workers that have taken a request since the aggregator started; every other worker on that server is reported as `0` bytes. That is exact rather than a guess — the aggregator counts from its own start, and the dashboard only ever plots deltas between samples it took after that — and it is what keeps a pool in the 15-second window instead of dropping it back to the wide one the moment a single worker goes quiet. A snapshot with *no* rows at all is the exception: it is treated as not yet usable, so a pipeline that is never fed falls back to the rounded cells and says so in the log, rather than confidently plotting no traffic everywhere.
- **Counters restart with Apache.** They count from the moment the aggregator started, not from the moment the worker was created, and a graceful restart resets them. The dashboard plots rates, not totals, so a reset costs you one zero-valued point.
- **Restrict the endpoint.** The snapshot names your backends and says how much traffic each one carries. Limit it to the management network, exactly like `/balancer-manager`.

## Configuration

### BFF — which Apache servers to manage

`ApacheBalancerBFF/appsettings.json`:

```json
{
  "ApacheManagement": {
    "Servers": [
      { "Id": "lb-01", "Name": "Load Balancer Primary",   "BaseUrl": "http://10.0.0.11" },
      { "Id": "lb-02", "Name": "Load Balancer Secondary", "BaseUrl": "http://10.0.0.12" }
    ]
  }
}
```

| Field | Meaning |
| --- | --- |
| `Id` | Stable identifier used by the API and shown as the `Server` badge in the dashboard. |
| `Name` | Human-friendly display name. |
| `BaseUrl` | Base URL of the Apache instance, **without** the `/balancer-manager` path. |
| `MetricsPath` | *Optional.* Path of the [precise traffic counters](#precise-traffic-counters-optional) endpoint on that server, e.g. `/balancer-bytes`. Omit it — the default — and the server is read from its balancer-manager page alone. |

Add as many servers as you like; they are all queried concurrently. In containers you can override any of these with environment variables, e.g. `ApacheManagement__Servers__0__BaseUrl=http://10.0.0.11`.

### Dashboard — where the BFF lives

`ApacheBalancerWasmInterface/wwwroot/appsettings.json`:

```json
{ "BffBaseUrl": "http://localhost:5084" }
```

Environment-specific overrides go in `appsettings.Development.json` / `appsettings.Production.json` next to it. Because this is a WebAssembly app, the file is fetched by the browser at startup — you can edit it in a deployed site without rebuilding.

## Running locally

Run the two projects side by side:

```bash
# Terminal 1 — the BFF (http://localhost:5084, Swagger at /swagger)
dotnet run --project ApacheBalancerBFF

# Terminal 2 — the dashboard (http://localhost:5188)
dotnet run --project ApacheBalancerWasmInterface
```

Then open <http://localhost:5188>. The development profile already points the dashboard at `http://localhost:5084`.

`ApacheBalancerBFF/ApacheBalancerBFF.http` contains ready-made requests for the API if you prefer to start there.

### Without a proxy cluster

Every row in the dashboard is scraped from a live Apache server, so with none reachable the grid
comes up empty. [`tools/fake-balancer-manager.cs`](tools/fake-balancer-manager.cs) stands in for
two of them — seeded with workers in every status the dashboard paints, and applying the
`w_status_*` fields on POST, so the action buttons genuinely toggle:

```bash
# Terminal 1 — two stand-in servers (.NET 10 runs the single file directly)
ASPNETCORE_URLS="http://localhost:8801;http://localhost:8802" dotnet run tools/fake-balancer-manager.cs

# Terminal 2 — the BFF, pointed at them from the environment so no appsettings file is edited
ApacheManagement__Servers__0__Id=lb-01 ApacheManagement__Servers__0__BaseUrl=http://localhost:8801 \
ApacheManagement__Servers__1__Id=lb-02 ApacheManagement__Servers__1__BaseUrl=http://localhost:8802 \
dotnet run --project ApacheBalancerBFF

# Terminal 3 — the dashboard
dotnet run --project ApacheBalancerWasmInterface
```

It is a development fixture, not a mod_proxy_balancer emulator: it implements the parts of the
page this dashboard reads, and its state lives in memory until you stop it.

## Docker

The BFF ships with a Dockerfile, and CI publishes an image to GHCR on every push to `main`:

```bash
# Build locally (from the repository root — the Dockerfile expects that context)
docker build -t apache-balancer-bff -f ApacheBalancerBFF/Dockerfile .

docker run -d -p 8080:8080 \
  -e ApacheManagement__Servers__0__Id=lb-01 \
  -e ApacheManagement__Servers__0__Name="Load Balancer Primary" \
  -e ApacheManagement__Servers__0__BaseUrl=http://10.0.0.11 \
  -e ApacheManagement__Servers__1__Id=lb-02 \
  -e ApacheManagement__Servers__1__Name="Load Balancer Secondary" \
  -e ApacheManagement__Servers__1__BaseUrl=http://10.0.0.12 \
  apache-balancer-bff
```

Or pull the published image:

```bash
docker pull ghcr.io/gfk/apachebalancermanager-bff:latest
```

## Deploying the dashboard

The dashboard is a static site — publish it and serve the output with any web server:

```bash
dotnet publish ApacheBalancerWasmInterface -c Release
# output: ApacheBalancerWasmInterface/bin/Release/net10.0/browser-wasm/publish/wwwroot
```

Set `BffBaseUrl` in the published `wwwroot/appsettings.json` to wherever the BFF is reachable from the *browser*, and make sure the host serves the app's `index.html` for unknown paths so client-side routing works.

## REST API

Base path `/api/balancer`. Full interactive documentation is at `/swagger`.

### `GET /api/balancer/status`

Aggregated status of every configured server. All servers are queried concurrently, and the response is **always `HTTP 200`**: unreachable servers land in `Errors` while the rest are returned in `SuccessData`.

```jsonc
{
  "SuccessData": [
    {
      "ServerId": "lb-01",
      "ServerName": "Load Balancer Primary",
      "BaseUrl": "http://10.0.0.11",
      "Balancers": [
        {
          "Name": "web-public-backend",
          "Nonce": "…",
          "Method": "bybusyness",
          "MaxMembers": "2 [2 Used]",
          "StickySession": "(None)",
          "Workers": [
            {
              "Url": "https://web-public.app-1.example.com",
              "RawStatus": "Init Ok",
              "StatusFlags": ["Init", "Ok"],
              "Elected": "13407", "Busy": "0", "Load": "0",
              "To": "28M", "From": "171M",
              "ToBytes": 29360128, "FromBytes": 179306496,
              "HealthCheckMethod": "GET", "HealthCheckUri": "/"
            }
          ]
        }
      ]
    }
  ],
  "Errors": [
    { "ServerId": "lb-03", "ErrorMessage": "Connection timed out.", "Timestamp": "2026-09-17T15:21:34Z" }
  ]
}
```

Counter cells (`Elected`, `To`, `From`, …) are returned **verbatim, as Apache rendered them** — `"28M"`, not `29360128`. Apache formats them through `apr_strfsize`, and the BFF does not pretend to a precision the source does not have. The dashboard parses them back into numbers for plotting.

`ToBytes` and `FromBytes` are the exception, and they are numbers because their source is one: they come from the [precise traffic counters](#precise-traffic-counters-optional) endpoint rather than the manager page. They are `null` for every server that does not have that add-on configured or whose endpoint could not be read; `0` means the aggregator is running and that worker has carried nothing since it started. `To` and `From` are always present either way.

### `POST /api/balancer/worker/status`

Applies one state change to one worker on one server.

```json
{
  "ServerId": "lb-01",
  "BalancerName": "web-public-backend",
  "WorkerUrl": "https://web-public.app-1.example.com",
  "ActionType": "Drain"
}
```

`ActionType` is case-insensitive, and one of:

| Action | Effect | Apache field |
| --- | --- | --- |
| `Enable` | Clears Disabled, Drain and Stopped in one call | `w_status_D/N/S=0` |
| `Disable` / `DisableOff` | Sets / clears Disabled | `w_status_D` |
| `Drain` / `DrainOff` | Sets / clears Draining Mode | `w_status_N` |
| `Stop` / `StopOff` | Sets / clears Stopped | `w_status_S` |
| `IgnoreErrorsOn` / `IgnoreErrorsOff` | Sets / clears Ignore Errors | `w_status_I` |
| `HotStandbyOn` / `HotStandbyOff` | Sets / clears Hot Standby | `w_status_H` |
| `HotSpareOn` / `HotSpareOff` | Sets / clears Hot Spare | `w_status_R` |
| `HcFailOn` / `HcFailOff` | Sets / clears the health-check failure flag | `w_status_C` |

Responses:

| Status | Meaning |
| --- | --- |
| `200` | Applied **and verified**. The body carries the worker's post-action state. |
| `400` | Unknown `ServerId`, `BalancerName`, `WorkerUrl` or `ActionType` — the message lists the valid values. |
| `502` | Apache was unreachable, or accepted the request and did not apply it. |

That last case is the important one. Apache answers `HTTP 200` even when it discards a modification, so the BFF re-parses the page Apache returns and compares the worker's status flags against what the action should have produced. A mismatch is reported as a failure with the flags that did not change, rather than a green checkmark over nothing.

## Security

> [!WARNING]
> **The BFF has no authentication or authorization, and its CORS policy accepts any origin.** Anyone who can reach it can disable your backends.

This is deliberate: the service is designed to run inside an isolated management network, in front of `/balancer-manager` endpoints that are themselves IP-restricted. Swagger UI is always enabled for the same reason.

If you deploy it anywhere less controlled, put it behind something that authenticates — a reverse proxy with SSO, an mTLS gateway, a VPN — and tighten the CORS policy in `Program.cs` to the dashboard's origin. Do not expose either the BFF or `/balancer-manager` to the internet.

## How it works

**Reading.** The BFF `GET`s each `/balancer-manager` page concurrently and parses it with HtmlAgilityPack: one `<h3>` per pool carrying the pool name and nonce in its anchor's query string, followed by a summary table and a worker table (11 columns, or 17 when health checks are configured). Apache serves these pages as ISO-8859-1, so the BFF registers the code-pages provider to decode them correctly. A failure on one server is caught per-server and becomes an `Errors` entry. A server configured with a `MetricsPath` is read twice, concurrently: that second, optional request never throws and never fails a poll, so a missing or broken counters endpoint costs nothing but a log line.

**Writing.** Modifications need a per-balancer nonce that changes on every page load, so a write is a four-step dance: fetch the page for a fresh nonce, locate the balancer and worker, POST the minimal form payload Apache expects (`b`, `w`, `nonce`, plus the status flags for that action), then re-parse Apache's response and verify the flags. The `Referer` header is set to the manager URL throughout, without which Apache's cross-site check discards the write silently.

**Graphing.** The dashboard keeps up to 120 samples per worker, in the browser, only for the pools you have expanded — roughly two minutes at the 1s refresh, half an hour at 15s. Collapsing a pool throws its history away.

The counters need care. `Elected` is exact, so its per-minute rate is averaged over a short 15-second window. `To`/`From` are not: Apache prints them through `apr_strfsize`, so the displayed value only moves in steps — 1 MB apart in the `161M` band, and about 107 MB apart once a worker crosses 1 GB. A naive two-second delta across one of those steps reads as half a megabyte per second against a true rate two orders of magnitude lower. So the byte rates use a much wider window — half the history on hand, clamped between 1 and 5 minutes — and no point is drawn until that window is genuinely full. The chart captions always name the window that was actually measured, not the one that was asked for.

When every worker in a pool reports exact counters from the [precise traffic counters](#precise-traffic-counters-optional) endpoint, there is no rounding left to average out and the byte rates drop to the same 15-second window as `Elected`, drawing from the first interval. A pool that spans a server with the add-on and one without it falls back to the wide window for all of its lines: the three charts share one time domain and one caption, and lines measured over different spans would not be comparable within a single chart.

`Busy` is a gauge rather than a counter, so it is plotted exactly as Apache reported it, on an axis that frames the variation instead of being pinned to zero.

## Project layout

```
ApacheBalancerBFF/              ASP.NET Core BFF
  Configuration/                Server list bound from appsettings
  Controllers/                  REST surface
  Domain/                       Action → Apache form-field mapping and verification
  Services/                     HTTP client, HTML parser, orchestration
ApacheBalancerWasmInterface/    Blazor WebAssembly dashboard
  Components/                   Console window, live pool charts
  Layout/                       Shell and header sections
  Models/                       Pool-first view models, metric samples
  Pages/                        The dashboard
  Services/                     API client, view-model mapper, metrics history
Shared/                         DTOs shared by both projects
tools/                          Optional add-on for the Apache servers, and a local fixture
  balancer-bytes-agg            Piped-log aggregator publishing byte-exact counters
  balancer-bytes.conf           Apache conf snippet that runs it and serves its snapshot
  fake-balancer-manager.cs      Stand-in balancer-manager for running the dashboard locally
```

## Contributing

Issues and pull requests are welcome.

```bash
git clone https://github.com/gfk/ApacheBalancerManager.git
cd ApacheBalancerManager
dotnet build
```

A few notes on the house style, visible throughout the codebase:

- **Explicit types over `var`**, and explicit lambda parameter types.
- **Comments explain *why*, not *what*.** Most of the non-obvious code here exists to work around a specific Apache behaviour; if you add such a workaround, say which one.
- **Keep Apache's output verbatim in the DTOs.** Parsing belongs in the consumer, so the API never implies precision the source lacks.
- If you touch the HTML parser, note which Apache version's layout you tested against.

The screenshots in this README were taken against a live two-proxy cluster; hostnames and pool names in them are placeholders.

## License

[MIT](LICENSE) © Guillaume Filion
