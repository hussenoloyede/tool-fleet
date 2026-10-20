# Azure setup

One-time setup for the live demo. Everything sits in one resource group, so deleting that group removes it all.

| Resource | Name | Tier | Cost on Azure for Students |
| --- | --- | --- | --- |
| Resource group | `toolfleet-rg` | | Free |
| Azure SQL server | `toolfleet-sql-hussein` | | Free |
| Azure SQL database | `toolfleet` | Free offer, serverless | Free; pauses for the rest of the month if the free allowance runs out |
| App Service plan | `toolfleet-plan` | Linux, Free F1 | Free (B1 is about US$13 a month if F1 isn't available) |
| Web app | `toolfleet-hussein` | .NET 10 | Included in the plan |

Use the same region for everything. Azure for Students only allows some regions; if one is refused, the error message lists the allowed ones.

The web app reaches the database with its managed identity, an identity Azure gives the app itself. There is no database password anywhere: not in the code, not in the pipeline, not in App Service settings.

## 1. Resource group

Portal → Resource groups → Create. Subscription: Azure for Students. Name `toolfleet-rg`. Region: Southeast Asia, or the nearest allowed one.

## 2. Azure SQL database (free offer)

Portal → SQL databases → Create.

- If the page shows "Want to try Azure SQL Database for free?", click **Apply offer**.
- Resource group `toolfleet-rg`, database name `toolfleet`.
- Server: **Create new**. Name `toolfleet-sql-hussein`, same region.
  - Authentication method: **Use Microsoft Entra-only authentication**.
  - **Set admin**: choose your own account.
- Free offer behaviour when the limit is reached: **Auto-pause the database until next month**. This guarantees it never costs money.
- Networking tab: Connectivity **Public endpoint**. Allow Azure services and resources to access this server: **Yes**. Add current client IP address: **Yes**.
- Review + create.

## 3. Web app

Portal → App Services → Create → **Web App**.

- Resource group `toolfleet-rg`, name `toolfleet-hussein` (the pipeline deploys to this exact name).
- Publish **Code**, runtime stack **.NET 10 (LTS)**, operating system **Linux**, same region.
- Pricing plan: create new `toolfleet-plan`, choose **Free F1**.
- Review + create.

When it's created, open the web app:

1. **Settings → Identity** → System assigned → Status **On** → Save.
2. **Settings → Environment variables**
   - App settings tab, add:
     - `Database__MigrateOnStartup` = `true`
     - `Database__SeedDemoData` = `true`
   - Connection strings tab, add:
     - Name `Fleet`, type **SQLAzure**, value:
       `Server=tcp:toolfleet-sql-hussein.database.windows.net,1433;Database=toolfleet;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60;`
   - Apply, then Confirm.
3. **Monitoring → Health check** → Enable, path `/health` → Save. (If F1 doesn't offer it, skip.)

## 4. Let the web app into the database

Portal → SQL databases → `toolfleet` → **Query editor**. Sign in with **Microsoft Entra authentication** (your account), then run:

```sql
CREATE USER [toolfleet-hussein] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [toolfleet-hussein];
ALTER ROLE db_datawriter ADD MEMBER [toolfleet-hussein];
ALTER ROLE db_ddladmin ADD MEMBER [toolfleet-hussein];
```

`db_ddladmin` lets the app create its tables when it applies migrations on startup.

## 5. Service connection for the pipeline

Azure DevOps → ToolFleet project → **Project settings → Service connections → New service connection**.

- **Azure Resource Manager** → identity type **App registration (automatic)** with **Workload identity federation**.
- Scope **Subscription**: Azure for Students. Resource group: `toolfleet-rg`.
- Name: `toolfleet-azure`.
- Tick **Grant access permission to all pipelines** → Save.

## 6. Deploy

Push to `main`. The pipeline builds, tests, then the Deploy stage puts the API and website on `https://toolfleet-hussein.azurewebsites.net`. On the first deploy Azure DevOps may ask you to **Permit** the pipeline to use the `production` environment.

The first page load after a quiet spell is slow: the F1 app and the free database both go to sleep when idle.
