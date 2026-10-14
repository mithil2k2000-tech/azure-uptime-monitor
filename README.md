# azure-uptime-monitor

An uptime monitor that runs as an Azure Function. Every five minutes it
checks a list of websites, stores each result in Azure Table storage,
and emails you through Azure Monitor when one is down. A small API,
`GET /api/status`, reports each site's current state and 24-hour uptime.

Built in C# (.NET 10, isolated worker) with JetBrains Rider. The Azure
environment is defined in Bicep.

```
   timer (every 5 min)                       GET /api/status
          │                                        │
   ┌──────▼──────────┐   results   ┌───────────────▼──┐
   │ CheckEndpoints  ├────────────▶│  Table storage   │
   └──────┬──────────┘             └──────────────────┘
          │ "Endpoint down" warning
   ┌──────▼──────────┐  alert rule  ┌──────────┐
   │ App Insights    ├─────────────▶│  email   │
   └─────────────────┘              └──────────┘
```

```json
[
  {
    "name": "portfolio",
    "url": "https://mithil2k2000-tech.github.io/portfolio/",
    "state": "up",
    "lastChecked": "2026-10-14T12:00:00+00:00",
    "lastStatusCode": 200,
    "lastLatencyMs": 84,
    "uptime24hPercent": 100,
    "checks24h": 288,
    "lastError": null
  }
]
```
_(The shape of the response; the figures are illustrative.)_

## How it works

- **`CheckEndpoints`** (timer) checks every endpoint in parallel. A
  refused connection, a timeout or a non-2xx status is a *result*, not
  an exception, so one broken site never stops the others being checked.
  Each endpoint can set its own timeout and expected status code.
- **Results** go to Table storage, one partition per endpoint. Row keys
  count *down* from the maximum timestamp, so the table's natural sort
  returns the newest first and "the last 24 hours" is a single range
  query.
- **`Status`** (HTTP) summarises each endpoint from those results.
- **Alerting:** a failed check is logged as a warning beginning
  `Endpoint down`; an Azure Monitor rule emails you if one appears in a
  ten-minute window, and clears itself when checks recover.
- **Settings are validated** at the first check (names, URLs, timeouts),
  with every problem listed.

## Security

- **No storage keys.** The storage account has shared-key access
  switched off. The function app reaches blobs, queues and tables with
  its managed identity, which holds exactly three storage roles.
- **No secrets in GitHub.** Deployment signs in to Azure with OIDC.
- HTTPS only, TLS 1.2 minimum, FTP off, no public blob access.

## Running it in Rider

1. Open `UptimeMonitor.sln`.
2. Install the **Azure Toolkit for Rider** plugin (Settings → Plugins).
   It adds a run configuration for the function app and installs the
   Azure Functions Core Tools.
3. Start **Azurite**, the local storage emulator:
   `npm install -g azurite` then `azurite --silent`.
4. Copy `src/UptimeMonitor/local.settings.example.json` to
   `local.settings.json` and edit the endpoints.
5. Run the function app. It checks every minute locally; open
   `http://localhost:7071/api/status`.

Tests: right-click the `UptimeMonitor.Tests` project → Run Unit Tests,
or `dotnet test` from a terminal. The storage tests run when
`AZURITE_CONNECTION_STRING=UseDevelopmentStorage=true` is set and
Azurite is running; otherwise they pass without checking anything.

## Tests

| Area | What's checked |
| --- | --- |
| `EndpointChecker` | up on 2xx; down on 5xx with the status; expected-status override; refused connection; timeout; caller cancellation isn't reported as an outage |
| Status | unknown with no data; state from the newest result; uptime percentage; the 24-hour window |
| Settings | duplicates, forbidden name characters, bad URLs, out-of-range timeouts |
| Functions | every endpoint checked and stored; status reports every configured endpoint |
| `TableResultStore` | round trip against Azurite: newest first, window respected, other endpoints excluded |

CI runs them on every push, with Azurite, and also checks that the
function app publishes and the Bicep compiles.

## Deploying

Needs an Azure subscription.

1. Create a resource group:
   `az group create -n rg-uptime -l uksouth`
2. Create an app registration with a **federated credential** for this
   repository's `main` branch, and give it Contributor and User Access
   Administrator on the resource group (the second because the template
   creates role assignments).
3. In GitHub → Settings → Secrets and variables → Actions → Variables,
   add `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_RESOURCE_GROUP` and `ALERT_EMAIL`.
4. Actions → Deploy → Run workflow. It applies `infra/main.bicep`,
   publishes the app, deploys it and calls `/api/status`.

To watch different sites, change the `endpoints` parameter in
`infra/main.bicep`.

**Cost:** Flex Consumption bills per execution and memory-second, and
one small function running every five minutes should stay inside the
monthly free grant, as far as I know; storage and log ingestion add
pennies. Check the Azure pricing pages for your region. To remove
everything: `az group delete -n rg-uptime`.

**Not yet deployed:** the code, tests and Bicep are checked in CI, but
the template hasn't been applied to a real subscription. In particular,
check that Flex Consumption offers .NET 10 in your region
(`dotnetVersion` parameter).
