<#
.SYNOPSIS
    Publishes Azure Function code to an existing Function App.

.PARAMETER FunctionAppName
    Name of the Azure Function App. If omitted, read from Terraform output 'function_app_name'.

.PARAMETER ResourceGroupName
    Name of the Azure resource group containing the Function App. If omitted, read from Terraform output 'resource_group_name'.

.PARAMETER FunctionProjectPath
    Path to the Azure Function project folder. Defaults to ../fk8s-functions relative to this script.

.EXAMPLE
    .\deploy-functionupdate.ps1
    .\deploy-functionupdate.ps1 -FunctionAppName fk8s-customer-functions
#>
param(
    [Parameter(Mandatory = $false)]
    [string] $FunctionAppName = "",

    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName = "",

    [Parameter(Mandatory = $false)]
    [string] $FunctionProjectPath = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($FunctionProjectPath)) {
    $FunctionProjectPath = Join-Path $scriptDir ".." "fk8s-functions"
}
$FunctionProjectPath = Resolve-Path $FunctionProjectPath

if (-not (Test-Path $FunctionProjectPath)) {
    throw "Function project path not found: $FunctionProjectPath"
}

# ── Resolve function app name and resource group ─────────────────────────────

Push-Location $scriptDir
try {
    if ([string]::IsNullOrWhiteSpace($FunctionAppName)) {
        $FunctionAppName = terraform output -raw function_app_name
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($FunctionAppName)) {
            throw "Could not retrieve function_app_name from Terraform output. Pass -FunctionAppName explicitly."
        }
    }
    if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
        $ResourceGroupName = terraform output -raw resource_group_name
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ResourceGroupName)) {
            throw "Could not retrieve resource_group_name from Terraform output. Pass -ResourceGroupName explicitly."
        }
    }
}
finally {
    Pop-Location
}
Write-Host "  Function app: $FunctionAppName" -ForegroundColor Gray
Write-Host "  Resource group: $ResourceGroupName" -ForegroundColor Gray

# ── Publish function code ────────────────────────────────────────────────────

Write-Host ""
Write-Host -ForegroundColor Cyan @'
 _____       _     _ _     _        __                  _   _                             _      
|  __ \     | |   | (_)   | |      / _|                | | (_)                           | |     
| |__) |   _| |__ | |_ ___| |__   | |_ _   _ _ __   ___| |_ _  ___  _ __     ___ ___   __| | ___ 
|  ___/ | | | '_ \| | / __| '_ \  |  _| | | | '_ \ / __| __| |/ _ \| '_ \   / __/ _ \ / _` |/ _ \
| |   | |_| | |_) | | \__ \ | | | | | | |_| | | | | (__| |_| | (_) | | | | | (_| (_) | (_| |  __/
|_|    \__,_|_.__/|_|_|___/_| |_| |_|  \__,_|_| |_|\___|\__|_|\___/|_| |_|  \___\___/ \__,_|\___|

'@
Write-Host "  Publishing function code" -ForegroundColor Cyan
Write-Host "  Project : $FunctionProjectPath" -ForegroundColor Gray

if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    throw "Azure Functions Core Tools (func) is not installed. Install from https://aka.ms/azure-functions-core-tools"
}

Push-Location $FunctionProjectPath
try {
    # Force en-US locale so func publish doesn't misparse "8.0" as "80".
    $env:LANG = "en_US.UTF-8"
    $env:LC_ALL = "en_US.UTF-8"

    # ── Diagnostics: dump storage and function app config before publish ──────
    Write-Host ""
    Write-Host "── Pre-publish diagnostics ──" -ForegroundColor Yellow

    Write-Host "  AzureWebJobsStorage setting:" -ForegroundColor Yellow
    $connStr = az functionapp config appsettings list --resource-group $ResourceGroupName --name $FunctionAppName --query "[?name=='AzureWebJobsStorage'].value | [0]" -o tsv
    if ([string]::IsNullOrWhiteSpace($connStr)) {
        Write-Host "    NOT SET or empty!" -ForegroundColor Red
    } else {
        # Mask the key but show the account name and endpoints
        $parts = $connStr -split ";"
        foreach ($part in $parts) {
            if ($part -like "AccountKey=*") {
                Write-Host "    AccountKey=****" -ForegroundColor Gray
            } else {
                Write-Host "    $part" -ForegroundColor Gray
            }
        }
    }

    Write-Host "  Storage account connectivity test:" -ForegroundColor Yellow
    $accountName = ($connStr -split ";" | Where-Object { $_ -like "AccountName=*" }) -replace "AccountName=",""
    $accountKey = (($connStr -split ";" | Where-Object { $_ -like "AccountKey=*" }) -replace "AccountKey=","")
    if ($accountName) {
        Write-Host "    Account: $accountName" -ForegroundColor Gray
        Write-Host "    Key length: $($accountKey.Length) chars" -ForegroundColor Gray
        Write-Host "    Listing containers via az storage..." -ForegroundColor Yellow
        $containerResult = az storage container list --account-name $accountName --account-key $accountKey --query "[].name" -o tsv 2>&1
        Write-Host "    Exit code: $LASTEXITCODE" -ForegroundColor Gray
        Write-Host "    Output: $containerResult" -ForegroundColor Gray
    } else {
        Write-Host "    Could not parse AccountName from connection string" -ForegroundColor Red
    }

    Write-Host "  linuxFxVersion:" -ForegroundColor Yellow
    $fxVer = az functionapp config show --resource-group $ResourceGroupName --name $FunctionAppName --query "linuxFxVersion" -o tsv
    Write-Host "    $fxVer" -ForegroundColor Gray
    Write-Host "── End diagnostics ──" -ForegroundColor Yellow
    Write-Host ""

    # Run with --verbose so we can see the exact blob URL func is trying to use
    # Use --force to skip the worker runtime check — Terraform already manages
    # FUNCTIONS_WORKER_RUNTIME and linuxFxVersion correctly.
    func azure functionapp publish $FunctionAppName --dotnet-isolated --verbose
    # func may exit non-zero only due to a transient sync-triggers failure after a
    # successful deployment. Restart the app to force trigger sync instead.
    if ($LASTEXITCODE -ne 0) {
        Write-Host "func publish exited with code $LASTEXITCODE — restarting function app to force trigger sync..." -ForegroundColor Yellow
        az functionapp restart --name $FunctionAppName --resource-group $ResourceGroupName
        if ($LASTEXITCODE -ne 0) { throw "func publish and fallback restart both failed." }
        Write-Host "Function app restarted successfully." -ForegroundColor Green
    }

    # func publish has a locale bug on Windows that sets linuxFxVersion to
    # DOTNET-ISOLATED|80.0 instead of DOTNET-ISOLATED|8.0. Fix it after deploy.
    # Use cmd environment variables + --% stop-parsing token so PowerShell
    # does not interpret | as a pipe operator.
    Write-Host "  Correcting linuxFxVersion..." -ForegroundColor Yellow
    az functionapp config set --resource-group $ResourceGroupName --name $FunctionAppName --% --linux-fx-version "DOTNET-ISOLATED|8" --output none
    if ($LASTEXITCODE -ne 0) { Write-Host "  Warning: failed to correct linuxFxVersion" -ForegroundColor Red }
    else { Write-Host "  linuxFxVersion set to DOTNET-ISOLATED|8" -ForegroundColor Green }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host -ForegroundColor Green @'
 ______                _   _                             _                    _     _ _     _              _ 
|  ____|              | | (_)                           | |                  | |   | (_)   | |            | |
| |__ _   _ _ __   ___| |_ _  ___  _ __     ___ ___   __| | ___   _ __  _   _| |__ | |_ ___| |__   ___  __| |
|  __| | | | '_ \ / __| __| |/ _ \| '_ \   / __/ _ \ / _` |/ _ \ | '_ \| | | | '_ \| | / __| '_ \ / _ \/ _` |
| |  | |_| | | | | (__| |_| | (_) | | | | | (_| (_) | (_| |  __/ | |_) | |_| | |_) | | \__ \ | | |  __/ (_| |
|_|   \__,_|_| |_|\___|\__|_|\___/|_| |_|  \___\___/ \__,_|\___| | .__/ \__,_|_.__/|_|_|___/_| |_|\___|\__,_|
                                                                 | |                                         
                                                                 |_|                                         
'@
