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
| `FIRESTORE_CREDENTIALS_JSON` | Chave (JSON) da service account GCP com leitura no Firestore de origem. Usada **apenas** quando o importador é ligado (`workflow_dispatch -f importer=on`); vira a var Terraform `firestore_credentials_json` → secret `Firestore--CredentialsJson` no Key Vault. Defina como **environment secret** do `dev`/`prod` se quiser credenciais distintas por ambiente. |

---

## Step 2 — Configure the `prod` GitHub Environment (manual approval gate)

The workflow's `deploy-prod` job declares `environment: prod`. GitHub only enforces required reviewers if the environment is configured:

1. Go to **Settings → Environments → New environment** → name it `prod`.
2. Enable **Required reviewers** and add at least one reviewer.
3. Optionally set a **Wait timer** (e.g. 5 min) after dev deploys.

The `dev` environment can be created the same way (no reviewers needed unless desired).

---

## Step 3 — Deploy Flow

> **DEV auto-deploys on every push to `main`**: the `push` path builds both images to
> the ACR (tagged with the short commit SHA) and runs `terraform apply` against DEV.
> PROD stays **manual-only** via `workflow_dispatch` (promotes an already-built image,
> no rebuild). Note: private repo — each push to `main` runs two billable jobs (`build`
> + `deploy`); to disable again, comment out the `push` trigger in `azure-deploy.yml`.

```
push to main        →  build images to ACR + deploy → dev (automatic)
workflow_dispatch   →  deploy → dev or prod (promotes an existing image_tag, no rebuild)
```

1. **deploy (prod, manual)** — `terraform init` (key `prod.tfstate`) + `terraform apply`. The `*.tfvars` files are **gitignored** (they hold local secrets), so CI passes config **inline**: `-var="environment=prod"`, `-var="google_client_id=..."`, and the SHA-tagged images. Secrets come from GitHub Secrets via `TF_VAR_jwt_secret` / `TF_VAR_ai_api_key` / `TF_VAR_postgres_admin_login`. All other vars use their `variables.tf` defaults.
2. **prod** is manual-only: `gh workflow run azure-deploy.yml -f promote_to=prod -f image_tag=<sha>` promotes an image already in the ACR (no rebuild), gated by the GitHub `prod` environment approval if configured. Pushes to `main` build+push images automatically, so a merged SHA is usually already in the ACR; only build+push manually (`az acr build` or `docker build`/`docker push` to `crulbraone.azurecr.io`) when promoting a SHA that never landed on `main`.

> Local `terraform apply` (below) uses `-var-file=dev.tfvars` because the file exists on your machine.

### Importador Firestore→One (one-shot) via CI

O importador (`tools/Ulbra.One.FirestoreImport`) **não** entra no build/deploy automático de todo push — é uma ferramenta de migração one-shot, `OFF` por padrão. Para ligar/desligar pela pipeline, use o `workflow_dispatch` com o input `importer`:

```bash
# ligar (builda a imagem do importador + sobe o Container App no ambiente escolhido)
gh workflow run azure-deploy.yml -f promote_to=dev -f image_tag=<sha> -f importer=on
# desligar (derruba o Container App)
gh workflow run azure-deploy.yml -f promote_to=dev -f image_tag=<sha> -f importer=off
```

- `importer=on`: o job **builda `crulbraone.azurecr.io/orcamento-import:<image_tag>`** (a partir do ref selecionado no dispatch — normalmente `main` atual) e roda o `terraform apply` com `enable_orcamento_import=true` + a imagem + a credencial GCP (`FIRESTORE_CREDENTIALS_JSON`). A api/web são promovidas no mesmo apply, então o `image_tag` deve ser um SHA que já tem imagens no ACR (i.e. já passou pelo build do push). Ao final o log imprime o **FQDN** do importador.
- `importer=off` (default): o apply mantém/força `enable_orcamento_import=false` → o Container App é destruído. Um `promote_to` de rotina (sem `importer`) também deixa off.
- ⚠️ **Não faça um deploy de rotina com `importer=off` enquanto uma migração roda** — o apply derruba o container no meio. O importador é single-writer (`max_replicas=1`): ligue → rode a migração → desligue, sem push/promote concorrente ao ambiente.
- Testar depois de ligado = só a chamada HTTP: `POST https://<fqdn>/api/import` (header `x-functions-key: <function key>`), `GET /api/import/status/{id}`, `POST /api/import/preflight`. Sem azurite e sem `func start` — o container **é** o host; blob e Firestore autenticam por managed identity / Key Vault.

**Obter a function key** (Functions containerizado no ACA — **não** há `az functionapp keys list`): o host gera as chaves e as grava no blob `azure-webjobs-secrets/<hostId>/host.json` da storage compartilhada. O `hostId` é fixado em `ulbra-one-import` (env `AzureFunctionsWebHost__hostId` no `main.tf`), então o caminho é determinístico. Com seu login Azure tendo *Storage Blob Data Reader/Contributor* na conta:

```bash
STG=<storage account name>   # mesmo do restante do stack (local.storage_name)
az storage blob download --account-name "$STG" --auth-mode login \
  -c azure-webjobs-secrets -n ulbra-one-import/host.json -f host.json --no-progress
# a function key default está em .functionKeys[] (ou use .masterKey para o admin)
python -c "import json;d=json.load(open('host.json'));print(d.get('functionKeys') or d)"
```

Passe o valor no header `x-functions-key` (ou `?code=<key>`). Alternativa sem key: só é possível trocando o `AuthorizationLevel` no código para `Anonymous` — não recomendado com ingress externo.

> **Seed de fundação em produção (AEL-912):** com `ASPNETCORE_ENVIRONMENT=Production` (mapeado por `local.per_env` no Terraform), o startup roda o `ProdDataSeeder` — serializado por um advisory lock do Postgres e com log-and-continue (não derruba o app se falhar). Ele semeia: organograma AELBRA (Empresas/Unidades/Centros de Custo + cobertura), plano de contas AELBRA, regras de retenção e categorias de despesa. **NÃO** semeia orçamento, usuários/admins nem dados transacionais. Os admins ganham papel `Admin` no login Google via `Authentication:Google:BootstrapAdmins` (`appsettings.Production.json`), não pelo seed — e eles são os **únicos** que se autoprovisionam: o login é por convite, então todo mundo mais precisa ter o e-mail cadastrado antes (tela Usuários / `POST /api/users`), senão o login devolve 403 `auth.user_not_registered`. Antes do go-live: (a) **confirmar os CNPJs do organograma** — alimentam CNAB e documentos fiscais; (b) **importar o orçamento real** antes de habilitar a esteira de requisição (célula de orçamento ausente bloqueia a aprovação por design); (c) **setar `google_client_id`** no pipeline/`prod.tfvars` (sem ele o login Google retorna 501). `AllowedDomains`/`BootstrapAdmins` já vêm de `appsettings.Production.json`.

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

The application selects its storage backend, message broker, realtime backplane, and secrets source at runtime via four environment variables / app-settings. Local/dev defaults are unchanged.

| Setting key | Local / dev default | Azure (prod) value | Notes |
|---|---|---|---|
| `ObjectStorage:Provider` | `Local` | `AzureBlob` | `Local` writes to disk; `AzureBlob` uses `DefaultAzureCredential` (Managed Identity in ACA) or falls back to `ObjectStorage:ConnectionString` |
| `MassTransit:Transport` | `RabbitMq` | `AzureServiceBus` | `RabbitMq` uses `RABBITMQ_URL`; `AzureServiceBus` uses MI (`DefaultAzureCredential`) — no connection string needed |
| `KeyVault:Uri` | _(absent — no Key Vault)_ | `https://kv-ulbraone-prod.vault.azure.net/` | When present, Azure Key Vault is added as a configuration source in `Program.cs` (also via `DefaultAzureCredential`) |
| `Redis:ConnectionString` | Redis from `docker compose` / AppHost | Azure Managed Redis (`Balanced_B0`, non-clustered; access key from Key Vault — Entra ID auth would need `Microsoft.Azure.StackExchangeRedis` in the app) | **The only one whose absence changes behaviour silently instead of failing.** Missing ⇒ the realtime backplane falls back to in-memory: the API still boots and answers 200, but signals never cross replicas — with `max_replicas = 2`, roughly half the notifications never reach the user's replica, and presence disagrees between them. `Program.cs` logs a warning and the `realtime-backplane` health check reports **Degraded** (never Unhealthy: realtime must not gate readiness). Check `/health`, not `/health/ready` |

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
- **Schedule:** `remittance_sweep_cron` (default `"0 20 * * *"` = 20:00 UTC = **17:00 BRT**). Runs every
  day; ACA Jobs schedule in UTC and Brasília is UTC-3 (no DST), so 17:00 BRT ⇒ 20:00 UTC. The run's
  `asOf` = current date (at 20:00 UTC the UTC date equals the Brasília date), so it produces that day's
  remittance. Disable per-env with `enable_remittance_sweep_job=false`.
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

## Scaling the API (CPU rule)

The API's Container App uses an explicit **CPU scale rule (~70% utilisation)** — `api_cpu_scale_threshold` in
`infra/terraform/variables.tf`, applied by the `container_app` module. It exists because of SSE: an open
`/api/realtime/stream` connection is an HTTP request that never completes, and the Container Apps **default**
rule is HTTP concurrency (10 concurrent requests per replica). Without an explicit rule the scale signal would
measure "how many browsers are open" rather than load, and the app would sit pinned at `max_replicas`.

**Dev is deliberately left out of the rule (`api_cpu_scale_threshold = null`).** A `custom_scale_rule`
*replaces* the default HTTP rule, and a CPU rule cannot scale from zero — there is no running container to
measure. Dev runs `min_replicas = 0` (playground; the ~66 s cold start is accepted), so a CPU rule there would
leave the app stuck with nothing able to wake it. Stage and prod run `min_replicas = 1`, so the rule is safe.
Changing dev to `min_replicas = 1` is what would make the CPU rule applicable there too.

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
