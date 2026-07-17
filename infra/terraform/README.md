# Ulbra.One — Terraform IaC

Provisions the Azure Container Apps stack for **dev** and **prod** (region `eastus2`, CAF naming).

## Prerequisites

- Terraform >= 1.7
- Azure CLI authenticated (`az login`) with contributor access to the target subscription
- Bootstrap resources created (Task C3):
  - Resource group `rg-ulbraone-tfstate`
  - Storage account `stulbraonetfstate` / container `tfstate` (remote state)
  - Container Registry `crulbraone` in `rg-ulbraone-tfstate` (shared ACR — referenced via `data "azurerm_container_registry"`)

## ACR: shared vs per-env

ACR (`crulbraone`) is **shared** across environments. It lives in `rg-ulbraone-tfstate` and is created by the bootstrap script. The Terraform root references it with a `data` source rather than managing it here. Rationale: a single registry lets the CI pipeline push once and promote the same image digest from dev → prod without re-tagging or duplicating storage.

## Layout

```
infra/terraform/
├── modules/
│   ├── resource_group/
│   ├── observability/       # Log Analytics + App Insights
│   ├── postgres/            # Flexible Server v16, B_Standard_B1ms
│   ├── service_bus/         # Standard namespace
│   ├── storage/             # Standard LRS + attachments container
│   ├── key_vault/           # RBAC mode + secrets
│   ├── identity/            # User-assigned MI + role assignments
│   ├── container_app_env/   # Container Apps Environment
│   ├── container_app/       # Parameterised (API + web)
│   └── container_app_job/   # Parameterised scheduled ACA Job (remittance sweep)
├── providers.tf
├── backend.tf
├── variables.tf             # also contains CAF name locals
├── main.tf
├── outputs.tf
├── dev.tfvars
└── prod.tfvars
```

## Usage

### Init (per environment — key varies)

```bash
# Dev
terraform init -backend-config="key=dev.tfstate"

# Prod
terraform init -backend-config="key=prod.tfstate"
```

### Plan

```bash
# Dev
terraform plan \
  -var-file=dev.tfvars \
  -var="jwt_secret=$JWT_SECRET" \
  -out=dev.tfplan

# Prod
terraform plan \
  -var-file=prod.tfvars \
  -var="jwt_secret=$JWT_SECRET" \
  -out=prod.tfplan
```

### Apply

```bash
terraform apply dev.tfplan
# or
terraform apply prod.tfplan
```

## Sensitive variables

`jwt_secret` and `postgres` admin password are **never** committed. Supply them via:

- CI pipeline secret variables (`TF_VAR_jwt_secret`)
- Local: `-var="jwt_secret=..."` flag
- `.tfvars` file excluded from version control

The postgres admin password is generated with `random_password` and stored automatically in Key Vault.

## Key Vault secret names

| Secret name | Used for |
|---|---|
| `Authentication--JwtSecret` | ASP.NET Core JWT signing |
| `ConnectionStrings--Database` | EF Core / Npgsql connection string |
| `Ai--ApiKey` | Anthropic API key for the payable AI classifier |

## Scheduled jobs (Container App Jobs)

The daily **CNAB remittance sweep** runs as an `azurerm_container_app_job` (module `container_app_job`),
not as an in-process timer: the API scales to zero, so an in-process `PeriodicTimer` would not fire
reliably. The job **reuses the API image** with `args = ["--job","remittance-sweep"]` (run-once job
mode in `Program.cs` → `JobRunner`), reuses the API's user-assigned identity (KV Secrets User +
Storage Blob Data Contributor + AcrPull), and shares the Container Apps Environment.

- `remittance_sweep_cron` (default `"0 9 * * *"` = 06:00 BRT) — the daily cutoff schedule (UTC).
- `enable_remittance_sweep_job` (default `true`) — set `false` to skip the job in an environment (e.g. dev).

The in-process `RemittanceSweepJob` stays disabled (`RemittanceSweep__Enabled=false`) so the scheduled
job and the web replicas never double-run. No CI change is needed — the job reuses the SHA-tagged `api`
image already built and applied by the deploy workflow.
