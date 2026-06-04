param(
    [string]$GitHubOrg = "fnandop",
    [string]$GitHubRepo = "azure-appconfiguration-poc",
    [string]$AppName = "azure-appconfiguration-poc",
    [string]$Location = "eastus",
    [string]$ResourceGroupDev = "rg-appconfiguration-poc-dev",
    [string]$ResourceGroupProd = "rg-appconfiguration-poc-prod",
    [string[]]$RequiredRoles = @("Contributor", "User Access Administrator"),
    [bool]$AssignUserAccessAdminAtSubscriptionScope = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Logging in to Azure..."
az login | Out-Null

$SubscriptionId = (az account show --query id -o tsv).Trim()
$TenantId = (az account show --query tenantId -o tsv).Trim()

Write-Host "Using subscription: $SubscriptionId"
Write-Host "Using tenant:       $TenantId"

Write-Host "Creating resource groups if they do not exist..."
az group create --name $ResourceGroupDev --location $Location --output none
az group create --name $ResourceGroupProd --location $Location --output none

$DevScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupDev"
$ProdScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupProd"
$SubscriptionScope = "/subscriptions/$SubscriptionId"

Write-Host "DEV scope:          $DevScope"
Write-Host "PROD scope:         $ProdScope"
Write-Host "Subscription scope: $SubscriptionScope"

Write-Host "Creating or reusing app registration..."
$ClientId = (az ad app list --display-name $AppName --query "[0].appId" -o tsv).Trim()

if ([string]::IsNullOrWhiteSpace($ClientId)) {
    $ClientId = (az ad app create --display-name $AppName --query appId -o tsv).Trim()
    Write-Host "Created app registration."
} else {
    Write-Host "Found existing app registration."
}

Write-Host "Client ID: $ClientId"

Write-Host "Creating or reusing service principal..."
$SpObjectId = ""
try {
    $SpObjectId = (az ad sp show --id $ClientId --query id -o tsv).Trim()
} catch {
    $SpObjectId = ""
}

if ([string]::IsNullOrWhiteSpace($SpObjectId)) {
    Write-Host "Service principal does not exist yet. Creating it..."
    az ad sp create --id $ClientId --output none

    Write-Host "Waiting for service principal propagation..."
    for ($i = 1; $i -le 12; $i++) {
        try {
            $SpObjectId = (az ad sp show --id $ClientId --query id -o tsv).Trim()
        } catch {
            $SpObjectId = ""
        }

        if (-not [string]::IsNullOrWhiteSpace($SpObjectId)) {
            break
        }

        Start-Sleep -Seconds 5
    }
}

if ([string]::IsNullOrWhiteSpace($SpObjectId)) {
    throw "Service principal was not found after creation."
}

Write-Host "Service principal object ID: $SpObjectId"

function Assign-RoleIfMissing {
    param(
        [string]$Scope,
        [string]$RoleName
    )

    $ExistingAssignment = (az role assignment list `
        --assignee-object-id $SpObjectId `
        --scope $Scope `
        --query "[?roleDefinitionName=='$RoleName'].id | [0]" `
        -o tsv).Trim()

    if (-not [string]::IsNullOrWhiteSpace($ExistingAssignment)) {
        Write-Host "Role '$RoleName' already assigned on scope: $Scope"
    } else {
        Write-Host "Assigning role '$RoleName' on scope: $Scope"
        az role assignment create `
            --assignee-object-id $SpObjectId `
            --assignee-principal-type ServicePrincipal `
            --role $RoleName `
            --scope $Scope `
            --output none
    }
}

foreach ($Role in $RequiredRoles) {
    Assign-RoleIfMissing -Scope $DevScope -RoleName $Role
    Assign-RoleIfMissing -Scope $ProdScope -RoleName $Role
}

if ($AssignUserAccessAdminAtSubscriptionScope) {
    Assign-RoleIfMissing -Scope $SubscriptionScope -RoleName "User Access Administrator"
}

function New-OrReplaceFederatedCredential {
    param(
        [string]$Name,
        [string]$EnvironmentName
    )

    Write-Host "Creating federated credential for environment: $EnvironmentName"

    $ExistingId = (az ad app federated-credential list `
        --id $ClientId `
        --query "[?name=='$Name'].id | [0]" `
        -o tsv).Trim()

    if (-not [string]::IsNullOrWhiteSpace($ExistingId)) {
        Write-Host "Existing federated credential '$Name' found. Deleting it..."
        az ad app federated-credential delete `
            --id $ClientId `
            --federated-credential-id $ExistingId
    }

    $Credential = @{
        name = $Name
        issuer = "https://token.actions.githubusercontent.com"
        subject = "repo:$GitHubOrg/$GitHubRepo:environment:$EnvironmentName"
        description = "GitHub Actions OIDC for $EnvironmentName environment"
        audiences = @("api://AzureADTokenExchange")
    }

    $File = "github-$EnvironmentName-credential.json"
    $Credential | ConvertTo-Json -Depth 5 | Set-Content -Path $File -Encoding UTF8

    az ad app federated-credential create `
        --id $ClientId `
        --parameters $File `
        --output none
}

New-OrReplaceFederatedCredential -Name "github-dev" -EnvironmentName "dev"
New-OrReplaceFederatedCredential -Name "github-prod" -EnvironmentName "prod"

Write-Host ""
Write-Host "Done."
Write-Host ""
Write-Host "Add these to GitHub Actions secrets or variables:"
Write-Host ""
Write-Host "AZURE_CLIENT_ID=$ClientId"
Write-Host "AZURE_TENANT_ID=$TenantId"
Write-Host "AZURE_SUBSCRIPTION_ID=$SubscriptionId"
Write-Host ""
Write-Host "Deployment service principal object ID:"
Write-Host "$SpObjectId"
Write-Host ""
Write-Host "GitHub OIDC subjects configured:"
Write-Host "repo:$GitHubOrg/$GitHubRepo:environment:dev"
Write-Host "repo:$GitHubOrg/$GitHubRepo:environment:prod"
Write-Host ""
Write-Host "Assigned roles at resource-group scope:"
Write-Host "- Contributor"
Write-Host "- User Access Administrator"
