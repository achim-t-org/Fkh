<#
.SYNOPSIS
    Deploys a customer environment: checks GitHub team state, then runs terraform apply.

.PARAMETER VarFile
    Path to the customer .tfvars file, e.g. customers/customer-a.tfvars

.PARAMETER GithubToken
    GitHub personal access token with read:org scope.
    Defaults to the TF_VAR_github_token environment variable.

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
    [switch] $AutoApprove
)

$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$checkScript = Join-Path $scriptDir "checkGitHubTeam.ps1"

if (-not (Test-Path $VarFile)) {
    throw "Var file not found: $VarFile"
}

# ── Step 1: Check / import GitHub team ───────────────────────────────────────

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Step 1: GitHub team check" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

& $checkScript -VarFile $VarFile -GithubToken $GithubToken
if ($LASTEXITCODE -ne 0) { throw "GitHub team check failed." }

# ── Step 2: Terraform apply ───────────────────────────────────────────────────

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Step 2: Terraform apply" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

$applyArgs = @("apply", "-var-file=$VarFile")
if ($AutoApprove) { $applyArgs += "-auto-approve" }

terraform @applyArgs
if ($LASTEXITCODE -ne 0) { throw "Terraform apply failed." }

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Deployment complete" -ForegroundColor Green
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
