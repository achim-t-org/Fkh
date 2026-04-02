<#
.SYNOPSIS
    Re-deploys Azure Function infrastructure and publishes the function code.

.PARAMETER VarFile
    Path to the customer .tfvars file, e.g. customers/customer-a.tfvars

.PARAMETER AutoApprove
    If specified, passes -auto-approve to terraform apply (no interactive prompt).

.PARAMETER FunctionProjectPath
    Path to the Azure Function project folder. Defaults to ../azure-function relative to this script.

.EXAMPLE
    .\deploy-function.ps1 -VarFile customers/customer-a.tfvars
    .\deploy-function.ps1 -VarFile customers/customer-a.tfvars -AutoApprove
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $VarFile,

    [Parameter(Mandatory = $false)]
    [switch] $AutoApprove,

    [Parameter(Mandatory = $false)]
    [string] $FunctionProjectPath = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $VarFile)) {
    throw "Var file not found: $VarFile"
}

if ([string]::IsNullOrWhiteSpace($FunctionProjectPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $FunctionProjectPath = Join-Path $scriptDir ".." "azure-function"
}
$FunctionProjectPath = Resolve-Path $FunctionProjectPath

if (-not (Test-Path $FunctionProjectPath)) {
    throw "Function project path not found: $FunctionProjectPath"
}

# ── Step 1: Terraform targeted apply ─────────────────────────────────────────

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Step 1: Updating Azure Function infrastructure" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

$targets = @(
    "-target=azurerm_linux_function_app.this",
    "-target=azurerm_service_plan.function",
    "-target=azurerm_storage_account.function",
    "-target=azurerm_user_assigned_identity.function",
    "-target=azurerm_role_assignment.function_aks"
)

$applyArgs = @("apply", "-var-file=$VarFile") + $targets
if ($AutoApprove) { $applyArgs += "-auto-approve" }

terraform @applyArgs
if ($LASTEXITCODE -ne 0) { throw "Terraform apply (function targets) failed." }

# ── Retrieve function app name from Terraform output ──────────────────────────

$functionAppName = terraform output -raw function_app_name 2>&1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($functionAppName)) {
    throw "Could not retrieve function_app_name from Terraform output."
}
Write-Host "  Function app: $functionAppName" -ForegroundColor Gray

# ── Step 2: Publish function code ─────────────────────────────────────────────

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Step 2: Publishing function code" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Project : $FunctionProjectPath" -ForegroundColor Gray

if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    throw "Azure Functions Core Tools (func) is not installed. Install from https://aka.ms/azure-functions-core-tools"
}

Push-Location $FunctionProjectPath
try {
    func azure functionapp publish $functionAppName --dotnet-isolated
    if ($LASTEXITCODE -ne 0) { throw "func publish failed." }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Function deployed successfully" -ForegroundColor Green
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
