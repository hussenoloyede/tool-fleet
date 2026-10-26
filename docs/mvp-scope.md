# ToolFleet MVP scope

Project 2 of the SWE internship portfolio plan. A web app for tracking downhole tools across field bases and rolling out firmware to them in stages, so a bad release is caught on a few tools before it reaches the whole fleet.

Status: weeks 1 to 3 are done. The demo runs live on Azure with the in-page simulator, and every build is published with its real file. Week 4 adds the field agent that delivers it.

## The demo story

The MVP is finished when this demo works end to end:

1. Open the fleet page. 40 tools across four bases (Labuan, Miri, Bintulu, Kemaman), with the ones overdue for calibration flagged in red.
2. Publish firmware `4.3.0` for the `MWD Pulser` model by uploading the build file. ToolFleet stores it and shows its SHA-256 fingerprint and size.
3. Create a rollout: 10% canary, halt if more than 20% of a stage fails.
4. Start it. The field agents at each base pick up jobs only for the canary tools, download the file, check its fingerprint, flash it and report back. One fails.
5. Show the rollout halting automatically when failures cross the threshold, or passing and waiting for a human to approve the rest.
6. Approve the rest of the fleet, watch the agents work through it, then open the audit log and show who did what, and when.

On the public demo link nobody can run an agent, so visitors use the in-page field simulator for step 4 instead. The README GIF shows the real agents.

Everything in scope exists to make that story work. Anything that doesn't serve it waits.

## How a firmware file reaches a tool

Downhole tools are not on the internet and can't be updated downhole. They are flashed in the workshop between jobs, by cable from a PC at the base. ToolFleet never talks to a tool directly; a small field agent on that workshop PC does.

```
ToolFleet API  ◀── 1. "Next job for MP-0012 or MP-0015?" (connected tools) ──  Field agent on the
(Azure)        ──── 2. job: MP-0015 needs 4.3.0, download link, SHA-256 ────▶  Labuan workshop PC
               ◀── 4. "MP-0015 succeeded, flashed file had SHA-256 …" ──────      │ 3. download, check
                                                                                  ▼    fingerprint, flash
Blob Storage   ──── file, via a link that expires after 15 minutes ─────────▶  MP-0015 (simulated)
```

Why ToolFleet holds the file rather than leaving distribution to email or a shared drive:

- The canary is enforced, not just requested. The file only goes to tools that have a job, and the rest of the fleet only gets jobs after approval.
- Every base flashes exactly the approved bytes. A stale or corrupted copy fails the fingerprint check before it touches a tool.
- There is proof of what was installed. If a tool fails downhole, the record shows the exact file, not just a typed version number.
- Earlier builds stay available, so rolling back is a later feature rather than a hunt for an old file.

The agent asks for work and the server never pushes to it, because bases sit behind firewalls on unreliable links. The agent can always reach out and retry; the server can't reach in.

## In scope

### Fleet register (done)
- Field locations (name, country).
- Tools: serial number, model, location, current firmware version, last calibration date, calibration interval in days.
- Calibration status computed from those dates: `Ok`, `DueSoon` (within 30 days) or `Overdue`. Filterable on the list endpoint and in the UI.
- Record a new calibration against a tool.

### Firmware releases
- A release belongs to one tool model and has a version and release notes. Version strings are unique per model. (done)
- Publishing a release uploads the build file. (done in week 3)
  - Stored in Azure Blob Storage in a private container; locally in Azurite, the Blob Storage emulator, started with npx.
  - ToolFleet computes the SHA-256 fingerprint and size on upload and saves them with the release. Files over 50 MB are rejected.
  - The file is never public. The API hands out read-only download links that expire after 15 minutes. The website's Download button gets one from `GET /api/firmware/{id}/download`, which will need an engineer sign-in once Entra ID login exists. Agents get theirs only with a claimed job (week 4).
  - Published files are never deleted, so earlier builds remain available.
- Only the newest release of a model can be rolled out, and only if its file was uploaded. (done; the API and the UI both enforce it)

### Staged rollouts (done)
A rollout targets every tool of the release's model that isn't already on that version.

```
Draft ──start──▶ Canary ──stage passes──▶ AwaitingPromotion ──promote──▶ Full ──stage passes──▶ Completed
                   │                                                      │
                   └──── failures > threshold ──▶ Halted ◀────────────────┘
any non-final state ──abort──▶ Aborted
```

- Canary size is a percentage of targets, rounded up, minimum one tool.
- Each target is `Pending`, `InProgress` (week 4: an agent has claimed it), `Succeeded` or `Failed`. A success updates the tool's firmware version.
- The rollout halts the moment the failure rate within the active stage exceeds the threshold, without waiting for the rest of the stage.
- Promotion from canary to full is always a manual step. That human gate is a design decision worth talking about in interviews.
- Only one active rollout per tool model at a time.
- Invalid transitions (promoting a halted rollout, reporting twice for one tool) and broken rules (duplicate serial number) return `422 Unprocessable Entity` with a problem-details body.

### Field agent (week 4)
A .NET worker service, `ToolFleet.Agent`, one per base. It runs on the workshop PC; in the demo, several run on one laptop.

- Configured with its base and an agent key. Keys are issued per base, shown once, and stored hashed. The API works out the agent's base from the key, so a Labuan agent can never claim a Miri tool.
- Configured with the serial numbers currently plugged in, which stands in for the cable. A tool that is out on a job is simply not in the list, and its target stays `Pending` ("waiting for the tool to be back at base").
- Every 10 seconds it asks for the next job for its connected tools. Claiming is atomic: the target moves to `InProgress` in the same database update, so two agents can never flash the same tool.
- For each job: download the file through the expiring link, check the SHA-256 matches, "flash" it, then report the result together with the fingerprint of what it flashed. The API marks the target failed if that fingerprint doesn't match the release.
- Flashing is simulated: the agent writes the file to `flashed/<serial>/firmware.bin` and fails a configurable share of tools, so the halt path can be shown.
- Claims older than 30 minutes with no result go back to `Pending`, so a crashed agent can't block a rollout forever.
- `tools/simulator.cs` and the in-page simulator stay, for quick demos and the public link.

### Audit log (done)
- Every insert, update and delete is written to an `AuditEntries` table in the same transaction: timestamp, actor, entity, action and the changed values as JSON.
- Read-only endpoint, filterable by entity, shown in plain language in the UI. From week 4 the actor for agent reports is the agent's base, e.g. `agent:Labuan`.

### Engineering
- xUnit tests for the rollout state machine and calibration rules, plus API tests through `WebApplicationFactory`. (done)
- Vitest tests for the Angular screens. (done)
- Angular frontend: fleet, firmware, rollouts, rollout detail with live progress and field simulator, audit log. (done)
- Azure DevOps pipeline: restore, build with warnings as errors, test with results and coverage published, Angular test and build, deploy on every push to main. (done)
- Deployed to Azure: App Service (Linux F1) for the API and site, Azure SQL Database (free offer), an Azure Storage account for firmware files. All reached with the app's managed identity, no passwords or keys. (done)
- Tests for the week 3 work: upload rejects empty and oversized files, records the right fingerprint, cleans file names, and only the newest build with a file can be rolled out. (done)
- Tests for the week 4 work: claiming a job is atomic; a fingerprint mismatch fails the target; an agent can't claim a tool at another base.

## Out of scope for the MVP

These are good "What I'd do next" README items.

- Microsoft Entra ID login and roles (engineer vs. approver). Moved after the MVP to make room for the agent; until then the actor is `anonymous` for people and `agent:<base>` for agents.
- Talking to real tool hardware. The agent's flash step is the one simulated piece.
- Signing firmware files, so a tool can prove the file came from ToolFleet and not just that it arrived intact.
- Rollback to the previous firmware version on halt.
- Optimistic concurrency on rollouts (`rowversion`), so two approvers can't promote at once.
- Scheduling rollouts by location or time window.
- Notifications (Teams or email) when a rollout halts.
- Multi-tenant or per-region permissions.

## API surface

| Method | Route | Purpose |
| --- | --- | --- |
| GET, POST | `/api/locations` | List or add field bases |
| GET, POST | `/api/tools` | List (filter by model, location, calibration) or register |
| GET | `/api/tools/{id}` | One tool |
| POST | `/api/tools/{id}/calibrations` | Record a calibration |
| GET | `/api/firmware` | List releases, with file name, size and SHA-256 |
| POST | `/api/firmware` | Publish a release: a multipart upload with the build file |
| GET | `/api/firmware/{id}/download` | A download link for the file that expires after 15 minutes |
| GET, POST | `/api/rollouts` | List or create (draft) |
| GET | `/api/rollouts/{id}` | Rollout with targets and progress |
| POST | `/api/rollouts/{id}/start`, `/promote`, `/abort` | State transitions |
| POST | `/api/rollouts/{id}/targets/{toolId}/result` | Simulators report success or failure |
| POST | `/api/agents` | Week 4: register an agent for a base and get its key once |
| POST | `/api/agent/jobs/next` | Week 4: agent sends its connected serials, claims one job (download link, SHA-256) or gets 204 |
| POST | `/api/agent/jobs/{targetId}/result` | Week 4: agent reports the result and the fingerprint of what it flashed |
| GET | `/api/audit` | Audit entries, filter by entity |
| GET | `/health` | Liveness for Azure |

## Milestones (part-time, about 10 hours a week)

| Week | Goal | Done when |
| --- | --- | --- |
| 1 | Domain, API and database locally | Done. All endpoints work; tests pass |
| 2 | Angular screens and simulator | Done. The demo story runs on localhost |
| 3 | CI, Azure, and the real firmware file | Done. Azure DevOps runs every test on each push; the app is live on Azure with a demo link; publishing a build uploads the file to Blob Storage (Azurite locally) and shows its SHA-256 |
| 4 | Field agent and polish | Two agents (Labuan and Miri) on one laptop complete a rollout end to end with no simulator, including a halt; README has a GIF of it, an updated architecture diagram and the CI badge |

Week 3 is the heaviest. If it runs over, finish CI and the live link first and move the upload into week 4, pushing the agent to week 5. Start applying as soon as the live link and green CI exist, whatever week that lands in. They are what gets the application read; the agent is what you talk about in the interview.
