#!/usr/bin/env bash
# bootstrap.sh — one-time setup of shared Azure resources for Ulbra.One.
#
# Creates:
#   • Resource group + Storage Account + blob container  (Terraform remote state)
#   • Azure Container Registry  crulbraone  (shared across dev/prod)
#   • Entra App Registration + Service Principal + federated credentials (GitHub OIDC)
#   • Role assignments: Contributor + User Access Administrator (subscription scope)
#
# Run ONCE by an Owner:
#   az login
#   bash infra/bootstrap/bootstrap.sh
#
# Idempotent: every step checks for existing resources before creating.
set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration — adjust if your environment differs
# ---------------------------------------------------------------------------
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-$(az account show --query id -o tsv)}"
LOCATION="eastus2"
GITHUB_REPO="aelbra/ulbra-one"          # org/repo on GitHub

# Terraform remote-state resources
TF_RG="rg-ulbraone-tfstate"
TF_SA="stulbraonetfstate"               # must be globally unique, <=24 chars, lower alphanumeric
TF_CONTAINER="tfstate"

# Shared ACR (Bootstrap creates; Terraform reads via data source)
ACR_NAME="crulbraone"

# Entra App Registration
APP_NAME="sp-ulbraone-github-oidc"
# ---------------------------------------------------------------------------

echo "==> Using subscription: ${SUBSCRIPTION_ID}"
az account set --subscription "${SUBSCRIPTION_ID}"

TENANT_ID=$(az account show --query tenantId -o tsv)

# ---------------------------------------------------------------------------
# 1. Resource group for tfstate + shared ACR
# ---------------------------------------------------------------------------
echo "==> [1/6] Resource group ${TF_RG}"
if ! az group show --name "${TF_RG}" &>/dev/null; then
  az group create --name "${TF_RG}" --location "${LOCATION}"
  echo "    Created."
else
  echo "    Already exists — skipping."
fi

# ---------------------------------------------------------------------------
# 2. Storage account for Terraform remote state
# ---------------------------------------------------------------------------
echo "==> [2/6] Storage account ${TF_SA}"
if ! az storage account show --name "${TF_SA}" --resource-group "${TF_RG}" &>/dev/null; then
  az storage account create \
    --name "${TF_SA}" \
    --resource-group "${TF_RG}" \
    --location "${LOCATION}" \
    --sku Standard_LRS \
    --kind StorageV2 \
    --allow-blob-public-access false \
    --min-tls-version TLS1_2
  echo "    Created."
else
  echo "    Already exists — skipping."
fi

# ---------------------------------------------------------------------------
# 3. Blob container for tfstate files
# ---------------------------------------------------------------------------
echo "==> [3/6] Blob container ${TF_CONTAINER}"
if ! az storage container show \
    --name "${TF_CONTAINER}" \
    --account-name "${TF_SA}" \
    --auth-mode login &>/dev/null; then
  az storage container create \
    --name "${TF_CONTAINER}" \
    --account-name "${TF_SA}" \
    --auth-mode login
  echo "    Created."
else
  echo "    Already exists — skipping."
fi

# ---------------------------------------------------------------------------
# 4. Azure Container Registry (Basic, shared across dev/prod)
# ---------------------------------------------------------------------------
echo "==> [4/6] ACR ${ACR_NAME}"
if ! az acr show --name "${ACR_NAME}" --resource-group "${TF_RG}" &>/dev/null; then
  az acr create \
    --name "${ACR_NAME}" \
    --resource-group "${TF_RG}" \
    --location "${LOCATION}" \
    --sku Basic \
    --admin-enabled false
  echo "    Created."
else
  echo "    Already exists — skipping."
fi

# ---------------------------------------------------------------------------
# 5. App Registration + Service Principal for GitHub OIDC
# ---------------------------------------------------------------------------
echo "==> [5/6] App Registration '${APP_NAME}'"

APP_ID=$(az ad app list --display-name "${APP_NAME}" --query "[0].appId" -o tsv 2>/dev/null || true)

if [[ -z "${APP_ID}" || "${APP_ID}" == "None" ]]; then
  APP_ID=$(az ad app create --display-name "${APP_NAME}" --query appId -o tsv)
  echo "    App Registration created: ${APP_ID}"
else
  echo "    App Registration already exists: ${APP_ID}"
fi

# Service principal
SP_ID=$(az ad sp list --filter "appId eq '${APP_ID}'" --query "[0].id" -o tsv 2>/dev/null || true)
if [[ -z "${SP_ID}" || "${SP_ID}" == "None" ]]; then
  SP_ID=$(az ad sp create --id "${APP_ID}" --query id -o tsv)
  echo "    Service principal created: ${SP_ID}"
else
  echo "    Service principal already exists: ${SP_ID}"
fi

# Federated credentials — one per subject.
# Parallel indexed arrays (portable to bash 3.2 — macOS default — which lacks `declare -A`).
CRED_NAMES=("github-main" "github-env-dev" "github-env-prod")
CRED_SUBJECTS=(
  "repo:${GITHUB_REPO}:ref:refs/heads/main"
  "repo:${GITHUB_REPO}:environment:dev"
  "repo:${GITHUB_REPO}:environment:prod"
)

for i in "${!CRED_NAMES[@]}"; do
  CRED_NAME="${CRED_NAMES[$i]}"
  SUBJECT="${CRED_SUBJECTS[$i]}"
  EXISTING=$(az ad app federated-credential list --id "${APP_ID}" \
    --query "[?name=='${CRED_NAME}'].name" -o tsv 2>/dev/null || true)
  if [[ -z "${EXISTING}" ]]; then
    az ad app federated-credential create \
      --id "${APP_ID}" \
      --parameters "{
        \"name\": \"${CRED_NAME}\",
        \"issuer\": \"https://token.actions.githubusercontent.com\",
        \"subject\": \"${SUBJECT}\",
        \"audiences\": [\"api://AzureADTokenExchange\"]
      }"
    echo "    Federated credential '${CRED_NAME}' created."
  else
    echo "    Federated credential '${CRED_NAME}' already exists — skipping."
  fi
done

# ---------------------------------------------------------------------------
# 6. Role assignments (subscription scope)
# ---------------------------------------------------------------------------
echo "==> [6/6] Role assignments for SP"

SUBSCRIPTION_SCOPE="/subscriptions/${SUBSCRIPTION_ID}"

for ROLE in "Contributor" "User Access Administrator"; do
  EXISTING=$(az role assignment list \
    --assignee "${APP_ID}" \
    --role "${ROLE}" \
    --scope "${SUBSCRIPTION_SCOPE}" \
    --query "[0].id" -o tsv 2>/dev/null || true)
  if [[ -z "${EXISTING}" || "${EXISTING}" == "None" ]]; then
    az role assignment create \
      --assignee "${APP_ID}" \
      --role "${ROLE}" \
      --scope "${SUBSCRIPTION_SCOPE}"
    echo "    Assigned '${ROLE}'."
  else
    echo "    '${ROLE}' already assigned — skipping."
  fi
done

# ---------------------------------------------------------------------------
# Output — copy these into GitHub repository secrets / variables
# ---------------------------------------------------------------------------
echo ""
echo "================================================================"
echo "  Bootstrap complete. Add the following to GitHub:"
echo "  Settings → Secrets and variables → Actions"
echo "================================================================"
echo ""
echo "  AZURE_CLIENT_ID       = ${APP_ID}"
echo "  AZURE_TENANT_ID       = ${TENANT_ID}"
echo "  AZURE_SUBSCRIPTION_ID = ${SUBSCRIPTION_ID}"
echo ""
echo "  (No client secret needed — OIDC federated identity is used.)"
echo "================================================================"
