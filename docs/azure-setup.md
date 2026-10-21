# Azure setup

One-time setup for the live demo. Everything sits in one resource group, so deleting that group removes it all.

| Resource | Name | Tier | Cost on Azure for Students |
| --- | --- | --- | --- |
| Resource group | `toolfleet-rg` | | Free |
| Azure SQL server | `toolfleet-sql-hussein` | | Free |
| Azure SQL database | `toolfleet` | Free offer, serverless | Free; pauses for the rest of the month if the free allowance runs out |
| App Service plan | `toolfleet-plan` | Linux, Free F1 | Free (B1 is about US$13 a month if F1 isn't available) |
| Web app | `toolfleet-hussein` | .NET 10 | Included in the plan |

Put the database and the web app in **East Asia**. Azure for Students only allows a set of regions (Policy → Assignments → *Allowed resource deployment regions* lists them); Southeast Asia isn't one of them, and East Asia is the closest that is. The resource group itself can be in any region.

The web app reaches the database with its managed identity, an identity Azure gives the app itself. There is no database password anywhere: not in the code, not in the pipeline, not in App Service settings.

## 1. Resource group

Portal → Resource groups → Create. Subscription: Azure for Students. Name `toolfleet-rg`. Any region.

## 2. Azure SQL database (free offer)

Portal → SQL databases → Create → **SQL database (Free offer)**.

- If the page shows "Want to try Azure SQL Database for free?", click **Apply offer**.
- Resource group `toolfleet-rg`, database name `toolfleet`.
- Server: **Create new**. Name `toolfleet-sql-hussein`, region **East Asia**.
  - Authentication method: **Use Microsoft Entra-only authentication**.
  - **Set admin**: choose your own account.
- Free offer behaviour when the limit is reached: **Auto-pause the database until next month**. This guarantees it never costs money.
- Networking tab: Connectivity **Public endpoint**. Allow Azure services and resources to access this server: **Yes**. Add current client IP address: **Yes**.
- Review + create.

## 3. Web app

Portal → App Services → Create → **Web App**.

- Resource group `toolfleet-rg`, name `toolfleet-hussein` (the pipeline deploys to this exact name).
- Publish **Code**, runtime stack **.NET 10 (LTS)**, operating system **Linux**, region **East Asia**.
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

Before the first deploy, create the environment the Deploy stage targets: Azure DevOps → Pipelines → Environments → **New environment**, name `production`, resource **None**. Open it, then ⋮ → **Security** → Pipeline permissions → **+** and add the ToolFleet pipeline. Without this the run fails with "Environment production could not be found".

Push to `main`. The pipeline builds, tests, then the Deploy stage puts the API and website on the web app.

New App Service apps get a unique address with a random suffix, so `toolfleet-hussein.azurewebsites.net` does not exist. The real one is under **Default domain** on the web app's Overview page. For this project it is:

https://toolfleet-hussein-euf6h0g7bxbrhabk.eastasia-01.azurewebsites.net

Quick checks once it's deployed: `/health` should say `Healthy`, and `/api/tools` should list 40 tools. If either fails, open the web app → Monitoring → **Log stream** and reload the page to see the error.

The first page load after a quiet spell is slow: the F1 app and the free database both go to sleep when idle.
