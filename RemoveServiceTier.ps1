<#
.SYNOPSIS
    Removes a service tier (Windows container) previously deployed by DeployServiceTier.ps1.

.DESCRIPTION
    This script removes the Kubernetes deployment, service, and secret created by
    DeployServiceTier.ps1 for a given DnsNameLabel.

    Resources removed:
    - Deployment: windows-app-<DnsNameLabel>-deployment
    - Service:    windows-app-<DnsNameLabel>-service
    - Secret:     windows-app-<DnsNameLabel>-secret

.PARAMETER ResourceGroupName
    The Azure resource group containing the AKS cluster.

.PARAMETER ClusterName
    The AKS cluster name.

.PARAMETER DnsNameLabel
    The DNS name label used when deploying the service tier.
    Defaults to the ClusterName.
#>
param(
    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName = "fk-rg",

    [Parameter(Mandatory = $false)]
    [string] $ClusterName = "fk-aks",

    [Parameter(Mandatory = $false)]
    [string] $DnsNameLabel = ""
)

if ([string]::IsNullOrWhiteSpace($DnsNameLabel)) {
    $DnsNameLabel = $ClusterName.ToLower()
}

$ErrorActionPreference = "Stop"
$namespace = "app-workload"
$appName = "windows-app-$DnsNameLabel"
$deploymentName = "$appName-deployment"
$serviceName = "$appName-service"
$secretName = "$appName-secret"

# ============================================================================
# Verify prerequisites
# ============================================================================
Write-Host "Verifying prerequisites..." -ForegroundColor Cyan

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI (az) is not installed. Install it from https://aka.ms/installazurecli"
}

if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    throw "kubectl is not installed. Install it via: az aks install-cli"
}

# ============================================================================
# Get AKS credentials
# ============================================================================
Write-Host "Fetching AKS credentials for '$ClusterName'..." -ForegroundColor Cyan

az aks get-credentials --resource-group $ResourceGroupName --name $ClusterName --overwrite-existing --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to get AKS credentials." }

# ============================================================================
# Remove resources
# ============================================================================
Write-Host "Removing service '$serviceName'..." -ForegroundColor Cyan
kubectl delete svc $serviceName -n $namespace --ignore-not-found | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Failed to remove service '$serviceName'." }

Write-Host "Removing deployment '$deploymentName'..." -ForegroundColor Cyan
kubectl delete deployment $deploymentName -n $namespace --ignore-not-found | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Failed to remove deployment '$deploymentName'." }

Write-Host "Removing secret '$secretName'..." -ForegroundColor Cyan
kubectl delete secret $secretName -n $namespace --ignore-not-found | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Failed to remove secret '$secretName'." }

# ============================================================================
# Summary
# ============================================================================
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Service Tier Removal Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  Deployment:         $deploymentName" -ForegroundColor Gray
Write-Host "  Service:            $serviceName" -ForegroundColor Gray
Write-Host "  Secret:             $secretName" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
