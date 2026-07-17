# Azure Deploy Runbook

End-to-end guide for provisioning and deploying Ulbra.One to Azure (Container Apps + Terraform).

---

## Prerequisites

- Azure CLI (`az`) — `az login` as a subscription **Owner** (bootstrap only)
- Terraform >= 1.7 (`terraform`)
- GitHub repository access (to configure secrets + environments)
- Docker (for local image builds if needed)

---

## Step 1 — One-time Bootstrap (run once per subscription)

The bootstrap script creates shared resources that live outside Terraform state (tfstate backend, ACR, OIDC service principal):

```bash
az login
bash infra/bootstrap/bootstrap.sh
```

What it creates (idempotent — safe to re-run):

1. Resource group `rg-ulbraone-tfstate` + storage account `stulbraonetfstate` + blob container `tfstate` — Terraform remote state backend.
2. ACR `crulbraone` (Basic, shared across dev/prod) — consumed by Terraform as a `data` source.
3. Entra App Registration `sp-ulbraone-github-oidc` + Service Principal with **federated credentials** for `main` branch, `dev` environment, and `prod` environment (GitHub OIDC — no stored client secret).
4. Role assignments on the subscription: **Contributor** + **User Access Administrator** (UAA is required so Terraform can create RBAC role assignments for the managed identity).

At the end the script prints the three values you need next.

### GitHub Secrets to set

Go to **Settings → Secrets and variables → Actions → Secrets** and add:

| Secret name | Value | Notes |
|---|---|---|
| `AZURE_CLIENT_ID` | printed by bootstrap | App Registration client ID |
| `AZURE_TENANT_ID` | printed by bootstrap | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | printed by bootstrap | Azure subscription ID |
| `POSTGRES_ADMIN_LOGIN` | e.g. `ulbraadmin` | Postgres flexible server admin user |
| `JWT_SECRET` | strong random string | JWT signing secret; stored in Key Vault by Terraform |

Optional:

| Secret / variable | Notes |
|---|---|
| `google_client_id` | Google OAuth client ID (public). Passed by the pipeline via `-var="google_client_id=..."` (defaults to the dev client; override per-env with a `GOOGLE_CLIENT_ID` repo/environment variable) |

---

## Step 2 — Configure the `prod` GitHub Environment (manual approval gate)

The workflow's `deploy-prod` job declares `environment: prod`. GitHub only enforces required reviewers if the environment is configured:

1. Go to **Settings → Environments → New environment** → name it `prod`.
2. Enable **Required reviewers** and add at least one reviewer.
3. Optionally set a **Wait timer** (e.g. 5 min) after dev deploys.

The `dev` environment can be created the same way (no reviewers needed unless desired).

---

## Step 3 — Normal Deploy Flow (push to main)

```
git push origin main
```

Pipeline (`.github/workflows/azure-deploy.yml`):

Mirrors the ulbra-sau deploy ergonomics (dev auto on push, prod by promoting a built tag):

```
push to main        →  build (ACR)  →  deploy → dev
workflow_dispatch   →  deploy → prod (promotes an existing image_tag, no rebuild)
```

1. **build** (push only) — Docker build for `src/API/Ulbra.One.Api/Dockerfile` and `web/Dockerfile`; pushed to `crulbraone.azurecr.io` tagged with the 7-char commit SHA.
2. **deploy** — `terraform init` (key `<env>.tfstate`) + `terraform apply`. The `*.tfvars` files are **gitignored** (they hold local secrets), so CI passes config **inline**: `-var="environment=<env>"`, `-var="google_client_id=..."`, and the SHA-tagged images. Secrets come from GitHub Secrets via `TF_VAR_jwt_secret` / `TF_VAR_ai_api_key` / `TF_VAR_postgres_admin_login`. All other vars use their `variables.tf` defaults.
3. **prod** is manual-only: `gh workflow run azure-deploy.yml -f promote_to=prod -f image_tag=<sha>` promotes a tag already built+tested in dev (no rebuild), gated by the GitHub `prod` environment approval if configured.

> Local `terraform apply` (below) still uses `-var-file=dev.tfvars` because the file exists on your machine — only CI avoids it.

### Manual Terraform apply (outside CI)

```bash
cd infra/terraform

terraform init \
  -backend-config="resource_group_name=rg-ulbraone-tfstate" \
  -backend-config="storage_account_name=stulbraonetfstate" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=dev.tfstate"   # or prod.tfstate

terraform apply \
  -var-file=dev.tfvars \
  -var="api_image=crulbraone.azurecr.io/api:<sha>" \
  -var="web_image=crulbraone.azurecr.io/web:<sha>" \
  -var="postgres_admin_login=<login>" \
  -var="jwt_secret=<secret>"
```

---

## Portable Config Matrix

The application selects its storage backend, message broker, and secrets source at runtime via three environment variables / app-settings. Local/dev defaults are unchanged.

| Setting key | Local / dev default | Azure (prod) value | Notes |
|---|---|---|---|
| `ObjectStorage:Provider` | `Local` | `AzureBlob` | `Local` writes to disk; `AzureBlob` uses `DefaultAzureCredential` (Managed Identity in ACA) or falls back to `ObjectStorage:ConnectionString` |
| `MassTransit:Transport` | `RabbitMq` | `AzureServiceBus` | `RabbitMq` uses `RABBITMQ_URL`; `AzureServiceBus` uses MI (`DefaultAzureCredential`) — no connection string needed |
| `KeyVault:Uri` | _(absent — no Key Vault)_ | `https://kv-ulbraone-prod.vault.azure.net/` | When present, Azure Key Vault is added as a configuration source in `Program.cs` (also via `DefaultAzureCredential`) |

In Azure Container Apps the managed identity (`id-ulbraone-api-<env>`) is automatically assigned the required RBAC roles by Terraform (Storage Blob Data Contributor, Azure Service Bus Data Owner, Key Vault Secrets User).

---

## Scheduled Jobs (CNAB remittance sweep)

The daily CNAB remittance generation runs as an **Azure Container Apps Job** (`azurerm_container_app_job`,
Terraform module `container_app_job`), provisioned alongside the API/web apps. It is a scheduled job — not
an in-process timer — because the API scales to zero, so a `PeriodicTimer` would not fire reliably.

- **Image / entrypoint:** reuses the SHA-tagged `api` image with `args = ["--job","remittance-sweep"]`.
  `Program.cs` detects `--job <name>` right after building the host and runs `JobRunner` once
  (resolve `IRemittanceSweepService` → `SweepAsync` → exit code), skipping the web pipeline, migrations
  and seeding. No hosted services start, so the job and web replicas never double-run.
- **Identity/config:** reuses the API's user-assigned identity and the same env/secrets (DB connection
  string via Key Vault, `ObjectStorage__*`, `AZURE_CLIENT_ID`). The in-process `RemittanceSweepJob`
  stays disabled (`RemittanceSweep__Enabled=false`).
- **Schedule:** `remittance_sweep_cron` (default `"0 9 * * *"` = 06:00 BRT). Disable per-env with
  `enable_remittance_sweep_job=false`.
- **No CI change:** the job flows through the same `terraform apply` as the app images.

Operate / observe:

```bash
# List executions of the scheduled job
az containerapp job execution list -n caj-ulbraone-remittance-sweep-prod -g rg-ulbraone-prod -o table

# Trigger a run on demand (e.g. to homologate or catch up)
az containerapp job start -n caj-ulbraone-remittance-sweep-prod -g rg-ulbraone-prod

# Logs (run summary: companies / remittances / installments / failures)
az containerapp job logs show -n caj-ulbraone-remittance-sweep-prod -g rg-ulbraone-prod --container caj-ulbraone-remittance-sweep-prod
```

The same sweep can also be triggered via `POST /api/remittances/sweep` (permission `TreasuryWrite`).

---

## Scaling Postgres Vertically

The Postgres Flexible Server SKU is controlled by the `postgres_sku_name` variable (default `B_Standard_B1ms` for both envs). To scale up:

1. Edit `infra/terraform/prod.tfvars` — add or update:
   ```hcl
   postgres_sku_name = "GP_Standard_D2s_v3"
   ```
2. Run `terraform apply` (CI push or manually) — Azure performs an in-place resize with a brief maintenance window.

---

## Rollback

Redeploy a prior commit SHA without reverting code:

```bash
# Re-run the pipeline for a prior SHA by triggering workflow_dispatch, or:
cd infra/terraform
terraform apply \
  -var-file=prod.tfvars \
  -var="api_image=crulbraone.azurecr.io/api:<prior-sha>" \
  -var="web_image=crulbraone.azurecr.io/web:<prior-sha>" \
  -var="postgres_admin_login=<login>" \
  -var="jwt_secret=<secret>"
```

Container Apps performs a zero-downtime revision swap.

---

## Notes

- `terraform validate` and the actual `terraform apply` run in the team's Azure environment (CI or a workstation with `az login`). There are no `terraform` or `az` binaries in the local dev container used by agents.
- The `google_client_id` is passed by the pipeline as a `-var` (public, defaults to the dev OAuth client). For a dedicated **prod** client, set a `GOOGLE_CLIENT_ID` repo/environment variable — the workflow uses `${{ vars.GOOGLE_CLIENT_ID || <dev default> }}`.
- ACR is **not** managed by the per-environment Terraform workspace — it is created by bootstrap and consumed as a `data` source. Deleting either env workspace will not affect the registry.
