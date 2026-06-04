#!/usr/bin/env bash
set -euo pipefail

clean() {
  tr -d '\r'
}

GITHUB_ORG="fnandop"
GITHUB_REPO="aspire-apm-backends-pocs"

APP_NAME="aspire-apm-backends-pocs"
LOCATION="eastus"
RESOURCE_GROUP="rg-aspire-apm-backends-pocs"

# Contributor lets the workflow create/update Azure resources.
# User Access Administrator lets Aspire/Bicep create Microsoft.Authorization/roleAssignments
# for Key Vault, App Configuration, ACR, managed identities, etc.
REQUIRED_ROLES=("Contributor" "User Access Administrator")

# Set this to "true" only if your deployment needs to create role assignments outside
# the  resource groups. Resource-group scope is safer and usually enough.
ASSIGN_USER_ACCESS_ADMIN_AT_SUBSCRIPTION_SCOPE="false"

echo "Logging in to Azure..."
az login

SUBSCRIPTION_ID=$(az account show --query id -o tsv | clean)
TENANT_ID=$(az account show --query tenantId -o tsv | clean)

if [ -z "$SUBSCRIPTION_ID" ] || [ -z "$TENANT_ID" ]; then
  echo "ERROR: Could not read subscription or tenant from az account show."
  exit 1
fi

echo "Using subscription: $SUBSCRIPTION_ID"
echo "Using tenant:       $TENANT_ID"

echo "Creating resource groups if they do not exist..."

az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none \
  --location "$LOCATION" \
  --output none

SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
SUBSCRIPTION_SCOPE="/subscriptions/$SUBSCRIPTION_ID"

echo "DEV scope:          $SCOPE"
echo "Subscription scope: $SUBSCRIPTION_SCOPE"

echo "Creating or reusing app registration..."

CLIENT_ID=$(az ad app list \
  --display-name "$APP_NAME" \
  --query "[0].appId" \
  -o tsv | clean)

if [ -z "$CLIENT_ID" ]; then
  CLIENT_ID=$(az ad app create \
    --display-name "$APP_NAME" \
    --query appId \
    -o tsv | clean)

  echo "Created app registration."
else
  echo "Found existing app registration."
fi

echo "Client ID: $CLIENT_ID"

echo "Creating or reusing service principal..."

SP_OBJECT_ID=$(az ad sp show \
  --id "$CLIENT_ID" \
  --query id \
  -o tsv 2>/dev/null | clean || true)

if [ -z "$SP_OBJECT_ID" ]; then
  echo "Service principal does not exist yet. Creating it..."

  az ad sp create \
    --id "$CLIENT_ID" \
    --output none

  echo "Waiting for service principal propagation..."

  for i in {1..12}; do
    SP_OBJECT_ID=$(az ad sp show \
      --id "$CLIENT_ID" \
      --query id \
      -o tsv 2>/dev/null | clean || true)

    if [ -n "$SP_OBJECT_ID" ]; then
      break
    fi

    sleep 5
  done
fi

if [ -z "$SP_OBJECT_ID" ]; then
  echo "ERROR: Service principal was not found after creation."
  exit 1
fi

echo "Service principal object ID: $SP_OBJECT_ID"

assign_role_if_missing() {
  local scope="$1"
  local role="$2"

  EXISTING_ASSIGNMENT=$(az role assignment list \
    --assignee-object-id "$SP_OBJECT_ID" \
    --scope "$scope" \
    --query "[?roleDefinitionName=='$role'].id | [0]" \
    -o tsv | clean)

  if [ -n "$EXISTING_ASSIGNMENT" ]; then
    echo "Role '$role' already assigned on scope: $scope"
  else
    echo "Assigning role '$role' on scope: $scope"

    az role assignment create \
      --assignee-object-id "$SP_OBJECT_ID" \
      --assignee-principal-type ServicePrincipal \
      --role "$role" \
      --scope "$scope" \
      --output none
  fi
}

for role in "${REQUIRED_ROLES[@]}"; do
  assign_role_if_missing "$SCOPE" "$role"
done

if [ "$ASSIGN_USER_ACCESS_ADMIN_AT_SUBSCRIPTION_SCOPE" = "true" ]; then
  assign_role_if_missing "$SUBSCRIPTION_SCOPE" "User Access Administrator"
fi

echo "Configuring GitHub OIDC federated credentials..."

create_or_replace_federated_credential() {
  local name="$1"
  local file="github-${name}-credential.json"

  echo "Creating federated credential for : $name"

  EXISTING_ID=$(az ad app federated-credential list \
    --id "$CLIENT_ID" \
    --query "[?name=='$name'].id | [0]" \
    -o tsv | clean)

  if [ -n "$EXISTING_ID" ]; then
    echo "Existing federated credential '$name' found. Deleting it..."

    az ad app federated-credential delete \
      --id "$CLIENT_ID" \
      --federated-credential-id "$EXISTING_ID"
  fi

  cat > "$file" <<JSON
{
  "name": "$name",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:$GITHUB_ORG/$GITHUB_REPO:ref:refs/heads/main", 
  "description": "GitHub Actions OIDC for $name",
  "audiences": [
    "api://AzureADTokenExchange"
  ]
}
JSON

  az ad app federated-credential create \
    --id "$CLIENT_ID" \
    --parameters "$file" \
    --output none
}

create_or_replace_federated_credential "github" 

echo ""
echo "Done."
echo ""
echo "Add these to GitHub Actions secrets or variables:"
echo ""
echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo ""
echo "Deployment service principal object ID:"
echo "$SP_OBJECT_ID"
echo ""
echo "GitHub OIDC subjects configured:"
echo "repo:$GITHUB_ORG/$GITHUB_REPO"
echo ""
echo "Assigned roles at resource-group scope:"
echo "- Contributor"
echo "- User Access Administrator"
