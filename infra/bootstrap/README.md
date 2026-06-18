# Bootstrap — one-time Azure setup

Run `bootstrap.sh` **once** (as an Azure Owner) to create the shared
resources that Terraform's backend and the deploy pipeline depend on.
After this, everything else is managed by Terraform.

## What it creates

| Resource | Name | Purpose |
|---|---|---|
| Resource Group | `rg-ulbraone-tfstate` | Container for shared/bootstrap resources |
| Storage Account | `stulbraonetfstate` | Terraform remote state backend |
| Blob Container | `tfstate` | Holds `dev.tfstate` / `prod.tfstate` |
| Container Registry | `crulbraone` (Basic) | Shared ACR for API + web images |
| App Registration | `sp-ulbraone-github-oidc` | GitHub Actions identity |
| Service Principal | (same app) | Azure RBAC assignee |
| Federated Credentials | 3 (main branch + dev env + prod env) | Keyless OIDC auth from GitHub |
| Role: Contributor | subscription scope | Terraform create/update resources |
| Role: User Access Administrator | subscription scope | Terraform RBAC role assignments |

## Prerequisites

- Azure CLI (`az`) ≥ 2.60
- Logged in as **Owner** on the target subscription:
  ```bash
  az login
  az account set --subscription "<your-subscription-id>"
  ```
- The GitHub repository must already exist at `aelbra/ulbra-one`
  (federated credential references it by name).

## How to run

```bash
# From the repository root:
bash infra/bootstrap/bootstrap.sh
```

Or override the subscription explicitly:

```bash
AZURE_SUBSCRIPTION_ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
  bash infra/bootstrap/bootstrap.sh
```

The script is **idempotent** — safe to re-run if interrupted.

## Output

After it finishes, the script prints three values:

```
AZURE_CLIENT_ID       = <app-registration-client-id>
AZURE_TENANT_ID       = <tenant-id>
AZURE_SUBSCRIPTION_ID = <subscription-id>
```

Add these as **Actions secrets** in the GitHub repository:
`Settings → Secrets and variables → Actions → New repository secret`

These are the secrets referenced by `.github/workflows/azure-deploy.yml`.

## After bootstrap

- Terraform environments (`dev`, `prod`) are applied by the CI pipeline.
- No manual `terraform init` or `az acr` commands needed after this point.
- The ACR name `crulbraone` is referenced by Terraform via a `data` source
  (no Terraform management of the ACR itself).
