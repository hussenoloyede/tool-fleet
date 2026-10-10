# ToolFleet MVP scope

Project 2 of the SWE internship portfolio plan. A web app for tracking downhole tools across field bases and rolling out firmware to them in stages, so a bad release is caught on a few tools before it reaches the whole fleet.

## The demo story

The MVP is finished when this five-minute demo works end to end on a public URL:

1. Open the fleet page. 40 tools across four bases (Labuan, Miri, Bintulu, Kemaman), with the ones overdue for calibration flagged in red.
2. Publish firmware `4.2.0` for the `MWD Pulser` model.
3. Create a rollout: 10% canary, halt if more than 20% of a stage fails.
4. Start it. The simulator reports results for the canary tools; one fails.
5. Show the rollout halting automatically when failures cross the threshold, or passing and waiting for a human to promote it.
6. Promote to the full fleet, watch it complete, then open the audit log and show who did what, and when.

Everything in scope exists to make that story work. Anything that doesn't serve it waits.

## In scope

### Fleet register
- Field locations (name, country).
- Tools: serial number, model, location, current firmware version, last calibration date, calibration interval in days.
- Calibration status computed from those dates: `Ok`, `DueSoon` (within 30 days) or `Overdue`. Filterable on the list endpoint.
- Record a new calibration against a tool.

### Firmware releases
- A release belongs to one tool model and has a version and release notes.
- Version strings are unique per model.

### Staged rollouts
A rollout targets every tool of the release's model that isn't already on that version.

```
Draft ──start──▶ Canary ──stage passes──▶ AwaitingPromotion ──promote──▶ Full ──stage passes──▶ Completed
                   │                                                      │
                   └──── failures > threshold ──▶ Halted ◀────────────────┘
any non-final state ──abort──▶ Aborted
```

- Canary size is a percentage of targets, rounded up, minimum one tool.
- Each target is `Pending`, `Succeeded` or `Failed`. A success updates the tool's firmware version.
- The rollout halts the moment the failure rate within the active stage exceeds the threshold, without waiting for the rest of the stage.
- Promotion from canary to full is always a manual step. That human gate is a design decision worth talking about in interviews.
- Only one active rollout per tool model at a time.
- Invalid transitions (promoting a halted rollout, reporting twice for one tool) and broken rules (duplicate serial number) return `422 Unprocessable Entity` with a problem-details body.

### Audit log
- Every insert, update and delete is written to an `AuditEntries` table in the same transaction: timestamp, actor, entity, action and the changed values as JSON.
- Read-only endpoint, filterable by entity.

### Simulator
- A small script that plays the part of field tools, calling the "report result" endpoint with a configurable failure rate. No real hardware exists, so this is what makes the demo move.

### Engineering
- xUnit tests for the rollout state machine and calibration rules (domain), plus API tests through `WebApplicationFactory`.
- Azure DevOps pipeline: restore, build, test with results published, Angular build.
- Deployed to Azure: App Service or Container Apps for the API and site, Azure SQL Database.
- Angular frontend with three screens: fleet, rollout detail, audit log.

## Out of scope for the MVP

These are good "What I'd do next" README items, and some become week-4 stretch goals.

- Microsoft Entra ID login and roles (engineer vs. approver). Planned as the first stretch goal; until then the actor is `anonymous`.
- Optimistic concurrency on rollouts (`rowversion`), so two approvers can't promote at once.
- Rollback to the previous firmware version on halt.
- Scheduling rollouts by location or time window.
- Notifications (Teams or email) when a rollout halts.
- Real device communication, retries and timeouts for tools that never report.
- Multi-tenant or per-region permissions.

## API surface

| Method | Route | Purpose |
| --- | --- | --- |
| GET, POST | `/api/locations` | List or add field bases |
| GET, POST | `/api/tools` | List (filter by model, location, calibration) or register |
| GET | `/api/tools/{id}` | One tool |
| POST | `/api/tools/{id}/calibrations` | Record a calibration |
| GET, POST | `/api/firmware` | List or publish releases |
| GET, POST | `/api/rollouts` | List or create (draft) |
| GET | `/api/rollouts/{id}` | Rollout with targets and progress |
| POST | `/api/rollouts/{id}/start`, `/promote`, `/abort` | State transitions |
| POST | `/api/rollouts/{id}/targets/{toolId}/result` | Simulator or device reports success or failure |
| GET | `/api/audit` | Audit entries, filter by entity |
| GET | `/health` | Liveness for Azure |

## Milestones (part-time, about 10 hours a week)

| Week | Goal | Done when |
| --- | --- | --- |
| 1 | Domain, API and database locally | All endpoints work against SQL Server in Docker; tests pass; first migration committed |
| 2 | Angular screens and simulator | The demo story runs on localhost |
| 3 | Pipeline and Azure | Azure DevOps runs tests on every push; app is live on Azure with a demo link |
| 4 | Polish and one stretch goal | README with GIF, architecture diagram and CI badge; Entra ID login if time allows |

Start applying after week 3. A live link with green CI is what gets the application read.
