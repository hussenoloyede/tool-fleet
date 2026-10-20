# ToolFleet

Track downhole tools across field bases and roll out firmware in stages: a small canary group first, an automatic halt if too many fail, and a human sign-off before the rest of the fleet updates. Every change lands in an audit log.

> Work in progress. The full demo runs locally: publish a build, run a canary, watch it halt or approve the rest, and read the audit log. Deployment to Azure is next. See the [MVP scope](docs/mvp-scope.md) for the plan.

<!-- TODO week 4: add a GIF of the demo, the live demo link and the Azure DevOps status badge here. -->

## Architecture

```mermaid
flowchart LR
    UI[Angular 22 web app] -- /api --> API[ASP.NET Core 10 minimal API]
    SIM[Field tool simulator] -- report results --> API
    API --> DOM[Domain: rollout state machine, calibration rules]
    API -- EF Core 10 --> DB[(Azure SQL / SQL Server)]
```

```
Draft ─start─▶ Canary ─pass─▶ AwaitingPromotion ─promote─▶ Full ─pass─▶ Completed
                 └── failures above threshold ──▶ Halted ◀──┘
```

| Folder | What's in it |
| --- | --- |
| `src/ToolFleet.Domain` | Entities and business rules. Plain C#, no framework dependencies. |
| `src/ToolFleet.Api` | Minimal API endpoints, EF Core `DbContext`, audit logging, dev seed data |
| `tests/ToolFleet.Domain.Tests` | xUnit tests for the rollout state machine and calibration rules |
| `tests/ToolFleet.Api.Tests` | HTTP tests through `WebApplicationFactory` on in-memory SQLite |
| `web` | Angular frontend |
| `tools/simulator.cs` | Pretends to be field tools reporting update results |
| `azure-pipelines.yml` | CI: build, test, coverage, artifacts |

## Run it locally

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), Node 24 and Docker.

```bash
# one-time setup
dotnet tool restore                     # installs dotnet-ef, pinned in dotnet-tools.json
docker compose up -d                    # SQL Server on localhost:1433

# build and test
dotnet build
dotnet test

# run the API on http://localhost:5080 (applies migrations and seeds 40 demo tools)
dotnet run --project src/ToolFleet.Api

# after changing an entity, add a migration for it
dotnet ef migrations add <WhatChanged> --project src/ToolFleet.Api --output-dir Data/Migrations

# in a second terminal: the web app on http://localhost:4200
cd web && npm install && npm start
```

No Docker? Run the API with SQLite instead: `dotnet run --project src/ToolFleet.Api --launch-profile sqlite`.

The OpenAPI description is at `http://localhost:5080/openapi/v1.json` in development.

Deploying to Azure (App Service, Azure SQL with a managed identity, and the pipeline's service connection) is a one-time setup described in [docs/azure-setup.md](docs/azure-setup.md).

### Try a rollout in the browser

1. Firmware: publish `4.2.0` for the MWD Pulser, then press Roll out.
2. New rollout: keep 10% canary and a 20% halt threshold, then Create rollout.
3. On the rollout page press Start rollout, then Report canary results. The field simulator plays the part of the tools. Drag the failure chance up to watch it halt.
4. If the canary passes, Approve rest of fleet and report again.
5. Audit log shows every change.

### Or from the terminal

```bash
# publish firmware 4.2.0 for the MWD Pulser model
curl -s -X POST localhost:5080/api/firmware -H "Content-Type: application/json" \
  -d '{"model":"MWD Pulser","version":"4.2.0","notes":"Fixes pulse timing drift"}'

# create a rollout with the release id from above: 10% canary, halt above 20% failures
curl -s -X POST localhost:5080/api/rollouts -H "Content-Type: application/json" \
  -d '{"firmwareReleaseId":"<release-id>","canaryPercent":10,"failureThresholdPercent":20}'

# let the simulator start it and report results for the canary tools
dotnet run tools/simulator.cs -- <rollout-id> --fail-rate 0.1

# if the canary passed, promote and run the simulator again for the rest
curl -s -X POST localhost:5080/api/rollouts/<rollout-id>/promote
dotnet run tools/simulator.cs -- <rollout-id>

# see what happened
curl -s "localhost:5080/api/audit?entityType=Rollout"
```

## Design decisions

- The domain owns the rules. `Rollout` exposes `Start`, `RecordResult`, `Promote` and `Abort`; nothing else can change its state. The endpoints load, call one method and save. That is why the state machine can be tested without a database.
- Randomness stays out of the domain. The API shuffles the tools and the domain takes the first N as the canary, so tests are deterministic.
- Promotion is manual. A canary that passes waits for a person. Automatic promotion is easy to add, but a wrong firmware image on 800 tools in the field is expensive.
- Audit rows are written in the same transaction as the change, by overriding `SaveChangesAsync`, so the log can't miss a change or record one that rolled back.
- Business-rule violations return 422 with an RFC 9457 problem-details body.

## What I'd do next

- Microsoft Entra ID login, with separate engineer and approver roles
- Optimistic concurrency (`rowversion`) so two approvers can't promote at the same time
- Roll back to `PreviousVersion` automatically when a rollout halts
- Teams notification when a rollout halts
- Generate the TypeScript API types from the OpenAPI document
