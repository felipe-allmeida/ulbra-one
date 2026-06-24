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
│   └── container_app/       # Parameterised (API + web)
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
| `ConnectionStrings--Default` | EF Core / Npgsql connection string |
