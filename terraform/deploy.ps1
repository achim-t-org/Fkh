<#
.SYNOPSIS
    Deploys a customer environment: checks GitHub team state, then runs terraform apply.

.PARAMETER VarFile
    Path to the customer .tfvars file, e.g. customers/customer-a.tfvars

.PARAMETER GithubToken
    GitHub personal access token with read:org scope.
    Defaults to the TF_VAR_github_token environment variable.

.PARAMETER SqlSaPassword
    SQL SA password.
    Defaults to the TF_VAR_sql_sa_password environment variable; if not set,
    the script prompts for it securely.

.PARAMETER FunctionProjectPath
    Path to the Azure Function project folder. Defaults to ../fk8s-functions relative to this script.

.PARAMETER AutoApprove
    If specified, passes -auto-approve to terraform apply (no interactive prompt).

.EXAMPLE
    .\deploy.ps1 -VarFile customers/customer-a.tfvars
    .\deploy.ps1 -VarFile customers/customer-a.tfvars -AutoApprove
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $VarFile,

    [Parameter(Mandatory = $false)]
    [string] $GithubToken = $env:TF_VAR_github_token,

    [Parameter(Mandatory = $false)]
    [SecureString] $SqlSaPassword,

    [Parameter(Mandatory = $false)]
    [string] $FunctionProjectPath = "",

    [Parameter(Mandatory = $false)]
    [switch] $AutoApprove
)

$ErrorActionPreference = "Stop"

# ── Ensure required environment variables ────────────────────────────────────

if ([string]::IsNullOrWhiteSpace($GithubToken)) {
    Write-Host "TF_VAR_github_token is not set. Attempting to read token from 'gh auth token'..." -ForegroundColor Yellow

    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $ghCommand) {
        throw "GitHub token is required. Install GitHub CLI and sign in with 'gh auth login', or set TF_VAR_github_token."
    }

    $GithubToken = (& gh auth token 2>$null).Trim()
    if ([string]::IsNullOrWhiteSpace($GithubToken)) {
        throw "GitHub token is required. 'gh auth token' did not return a token. Run 'gh auth login' or set TF_VAR_github_token."
    }

    $env:TF_VAR_github_token = $GithubToken
    Write-Host "TF_VAR_github_token has been set from GitHub CLI authentication." -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($env:TF_VAR_sql_sa_password)) {
    if (-not $SqlSaPassword) {
        $SqlSaPassword = Read-Host "Enter SQL SA password" -AsSecureString
    }

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlSaPassword)
    try {
        $plainSqlSaPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    if ([string]::IsNullOrWhiteSpace($plainSqlSaPassword)) {
        throw "SQL SA password is required. Pass -SqlSaPassword, set TF_VAR_sql_sa_password, or enter it when prompted."
    }

    $env:TF_VAR_sql_sa_password = $plainSqlSaPassword
    Write-Host "TF_VAR_sql_sa_password has been set for this session." -ForegroundColor Green
}

function Show-KubernetesDiagnostics {
    Write-Host ""
    Write-Host -ForegroundColor Red @'
 _______                   __                                         _          __      _ _          _ 
|__   __|                 / _|                                       | |        / _|    (_) |        | |
   | | ___ _ __ _ __ __ _| |_ ___  _ __ _ __ ___     __ _ _ __  _ __ | |_   _  | |_ __ _ _| | ___  __| |
   | |/ _ \ '__| '__/ _` |  _/ _ \| '__| '_ ` _ \   / _` | '_ \| '_ \| | | | | |  _/ _` | | |/ _ \/ _` |
   | |  __/ |  | | | (_| | || (_) | |  | | | | | | | (_| | |_) | |_) | | |_| | | || (_| | | |  __/ (_| |
   |_|\___|_|  |_|  \__,_|_| \___/|_|  |_| |_| |_|  \__,_| .__/| .__/|_|\__, | |_| \__,_|_|_|\___|\__,_|
                                                         | |   | |       __/ |                          
                                                         |_|   |_|      |___/                           
'@

    $kubectlCommand = Get-Command kubectl -ErrorAction SilentlyContinue
    if (-not $kubectlCommand) {
        Write-Host "kubectl is not installed or not on PATH. Skipping Kubernetes diagnostics." -ForegroundColor Yellow
        return
    }

    $commands = @(
        @{ Label = "Current context"; Cmd = "kubectl config current-context" },
        @{ Label = "Cluster info"; Cmd = "kubectl cluster-info" },
        @{ Label = "Storage classes"; Cmd = "kubectl get storageclass" },
        @{ Label = "PVCs in app namespace"; Cmd = "kubectl get pvc -n app -o wide" },
        @{ Label = "Describe mssql-data-pvc"; Cmd = "kubectl describe pvc mssql-data-pvc -n app" },
        @{ Label = "Recent events in app namespace"; Cmd = "kubectl get events -n app --sort-by=.metadata.creationTimestamp" },
        @{ Label = "Node status"; Cmd = "kubectl get nodes -o wide" }
    )

    foreach ($entry in $commands) {
        Write-Host ""
        Write-Host ("--- " + $entry.Label + " ---") -ForegroundColor Yellow
        try {
            Invoke-Expression $entry.Cmd
        }
        catch {
            Write-Host ("Failed: " + $entry.Cmd) -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor Red
        }
    }
}

# ── Resolve paths ─────────────────────────────────────────────────────────────

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$checkScript = Join-Path $scriptDir "checkGitHubTeam.ps1"

if ([string]::IsNullOrWhiteSpace($FunctionProjectPath)) {
    $FunctionProjectPath = Join-Path $scriptDir ".." "fk8s-functions"
}
$FunctionProjectPath = Resolve-Path $FunctionProjectPath

if (-not (Test-Path $FunctionProjectPath)) {
    throw "Function project path not found: $FunctionProjectPath"
}

if (-not (Test-Path $VarFile)) {
    throw "Var file not found: $VarFile"
}

Push-Location $scriptDir
try {

# ── Step 1: Terraform init ───────────────────────────────────────────────────

Write-Host ""
Write-Host -ForegroundColor Cyan @'
 _______                   __                       _       _ _   
|__   __|                 / _|                     (_)     (_) |  
   | | ___ _ __ _ __ __ _| |_ ___  _ __ _ __ ___    _ _ __  _| |_ 
   | |/ _ \ '__| '__/ _` |  _/ _ \| '__| '_ ` _ \  | | '_ \| | __|
   | |  __/ |  | | | (_| | || (_) | |  | | | | | | | | | | | | |_ 
   |_|\___|_|  |_|  \__,_|_| \___/|_|  |_| |_| |_| |_|_| |_|_|\__|
                                                                  
                                                                  
'@

# Force en-US locale so .NET tools don't misparse "8.0" as "80"
# on systems where the decimal separator is a comma (e.g. da-DK).
$env:LANG = "en_US.UTF-8"
$env:LC_ALL = "en_US.UTF-8"

terraform init
if ($LASTEXITCODE -ne 0) { throw "Terraform init failed." }

# ── Step 2: Bootstrap Azure infrastructure ────────────────────────────────────
# The Kubernetes provider depends on AKS kubeconfig values. On a fresh deploy
# AKS doesn't exist yet, so we must create it first with a targeted apply
# before any command that triggers full provider initialization.

Write-Host ""
Write-Host -ForegroundColor Cyan @'
 ____              _       _                   _                          
|  _ \            | |     | |                 | |                         
| |_) | ___   ___ | |_ ___| |_ _ __ __ _ _ __ | |                         
|  _ < / _ \ / _ \| __/ __| __| '__/ _` | '_ \| |                         
| |_) | (_) | (_) | |_\__ \ |_| | | (_| | |_) |_|                         
|____/ \___/ \___/ \__|___/\__|_|  \__,_| .__/(_)                         
                                        | |                                
                                        |_|                                
'@

$bootstrapArgs = @(
    "apply",
    "-var-file=$VarFile",
    "-target=azurerm_resource_group.this",
    "-target=azurerm_kubernetes_cluster.this",
    "-target=azurerm_kubernetes_cluster_node_pool.win",
    "-target=azurerm_storage_account.function",
    "-target=azurerm_service_plan.function",
    "-target=azurerm_windows_function_app.this",
    "-target=azurerm_user_assigned_identity.function",
    "-target=azurerm_role_assignment.function_aks"
)
if ($AutoApprove) { $bootstrapArgs += "-auto-approve" }

terraform @bootstrapArgs
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap apply failed."
}

# ── Step 3: Check / import GitHub team ───────────────────────────────────────

Write-Host ""
Write-Host -ForegroundColor Cyan @'
  _____ _ _   _    _       _       _______                          _               _    
 / ____(_) | | |  | |     | |     |__   __|                        | |             | |   
| |  __ _| |_| |__| |_   _| |__      | | ___  __ _ _ __ ___     ___| |__   ___  ___| | __
| | |_ | | __|  __  | | | | '_ \     | |/ _ \/ _` | '_ ` _ \   / __| '_ \ / _ \/ __| |/ /
| |__| | | |_| |  | | |_| | |_) |    | |  __/ (_| | | | | | | | (__| | | |  __/ (__|   < 
 \_____|_|\__|_|  |_|\__,_|_.__/     |_|\___|\__,_|_| |_| |_|  \___|_| |_|\___|\___|_|\_\
                                                                                         
                                                                                         
'@

& $checkScript -VarFile $VarFile -GithubToken $GithubToken
if ($LASTEXITCODE -ne 0) { throw "GitHub team check failed." }

# ── Step 4: Full Terraform apply ──────────────────────────────────────────────

Write-Host ""
Write-Host -ForegroundColor Cyan @'
 _______                   __                                         _       
|__   __|                 / _|                                       | |      
   | | ___ _ __ _ __ __ _| |_ ___  _ __ _ __ ___     __ _ _ __  _ __ | |_   _ 
   | |/ _ \ '__| '__/ _` |  _/ _ \| '__| '_ ` _ \   / _` | '_ \| '_ \| | | | |
   | |  __/ |  | | | (_| | || (_) | |  | | | | | | | (_| | |_) | |_) | | |_| |
   |_|\___|_|  |_|  \__,_|_| \___/|_|  |_| |_| |_|  \__,_| .__/| .__/|_|\__, |
                                                         | |   | |       __/ |
                                                         |_|   |_|      |___/ 
'@

$applyArgs = @("apply", "-var-file=$VarFile")
if ($AutoApprove) { $applyArgs += "-auto-approve" }

terraform @applyArgs
if ($LASTEXITCODE -ne 0) {
    Show-KubernetesDiagnostics
    throw "Terraform apply failed."
}

$functionAppName = terraform output -raw function_app_name
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($functionAppName)) {
    throw "Could not retrieve function_app_name from Terraform output."
}

$resourceGroupName = terraform output -raw resource_group_name
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resourceGroupName)) {
    throw "Could not retrieve resource_group_name from Terraform output."
}

# ── Step 5: Publish function code ─────────────────────────────────────────────

& (Join-Path $scriptDir "deploy-functionupdate.ps1") -FunctionAppName $functionAppName -ResourceGroupName $resourceGroupName -FunctionProjectPath $FunctionProjectPath
if ($LASTEXITCODE -ne 0) { throw "Function publish failed." }

# ── Step 6: Sync GitHub Actions secrets from Terraform outputs ────────────────

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
if ($ghCommand) {
    Write-Host ""
    Write-Host "Syncing GitHub Actions secrets from Terraform outputs..." -ForegroundColor Cyan

    $secrets = @{
        AZURE_CLIENT_ID        = terraform output -raw identity_client_id
        AZURE_TENANT_ID        = terraform output -raw tenant_id
        AZURE_SUBSCRIPTION_ID  = terraform output -raw subscription_id
        ACR_LOGIN_SERVER       = terraform output -raw acr_login_server
    }

    $repo = terraform output -raw github_repo
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repo)) {
        Write-Host "  Could not determine GitHub repo from Terraform output — skipping secret sync." -ForegroundColor Yellow
        return
    }

    foreach ($kv in $secrets.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace($kv.Value)) {
            gh secret set $kv.Key --repo $repo --body $kv.Value
            Write-Host "  Set secret $($kv.Key)" -ForegroundColor Green
        }
        else {
            Write-Host "  Skipped $($kv.Key) (empty output)" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host "GitHub CLI (gh) not found — skipping GitHub Actions secret sync." -ForegroundColor Yellow
    Write-Host "Set these secrets manually in the repo: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, ACR_LOGIN_SERVER" -ForegroundColor Yellow
}

Write-Host ""
Write-Host -ForegroundColor Green @'
 _____             _                                  _                               _      _       
|  __ \           | |                                | |                             | |    | |      
| |  | | ___ _ __ | | ___  _   _ _ __ ___   ___ _ __ | |_    ___ ___  _ __ ___  _ __ | | ___| |_ ___ 
| |  | |/ _ \ '_ \| |/ _ \| | | | '_ ` _ \ / _ \ '_ \| __|  / __/ _ \| '_ ` _ \| '_ \| |/ _ \ __/ _ \
| |__| |  __/ |_) | | (_) | |_| | | | | | |  __/ | | | |_  | (_| (_) | | | | | | |_) | |  __/ ||  __/
|_____/ \___| .__/|_|\___/ \__, |_| |_| |_|\___|_| |_|\__|  \___\___/|_| |_| |_| .__/|_|\___|\__\___|
            | |             __/ |                                              | |                   
            |_|            |___/                                               |_|                   
'@
}
finally {
    Pop-Location
}
