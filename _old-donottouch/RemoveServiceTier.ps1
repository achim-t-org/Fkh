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
# Remove the now-idle Windows node (scale down immediately)
# ============================================================================
Write-Host "Checking for empty Windows nodes to remove..." -ForegroundColor Cyan

# Get all Windows nodes in the 'win' node pool
$winNodes = kubectl get nodes -l "kubernetes.io/os=windows,agentpool=win" -o jsonpath='{.items[*].metadata.name}' 2>&1
if (-not [string]::IsNullOrWhiteSpace($winNodes)) {
    $nodeNames = $winNodes -split '\s+'
    foreach ($nodeName in $nodeNames) {
        # Check if this node has any running pods (excluding daemonsets)
        $podCount = kubectl get pods --all-namespaces --field-selector "spec.nodeName=$nodeName,status.phase=Running" -o json 2>&1 | ConvertFrom-Json
        $nonDaemonPods = $podCount.items | Where-Object {
            $ownerKind = $_.metadata.ownerReferences | ForEach-Object { $_.kind }
            $ownerKind -notcontains "DaemonSet"
        }
        if ($nonDaemonPods.Count -eq 0) {
            Write-Host "  Node '$nodeName' has no workload pods. Removing..." -ForegroundColor Yellow
            kubectl cordon $nodeName 2>&1 | Out-Null
            kubectl drain $nodeName --ignore-daemonsets --delete-emptydir-data --force --timeout=120s 2>&1 | Out-Null
            kubectl delete node $nodeName 2>&1 | Out-Null

            # Delete the underlying VM from the VMSS (Azure node pool)
            $mcResourceGroup = az aks show --resource-group $ResourceGroupName --name $ClusterName --query "nodeResourceGroup" -o tsv 2>&1
            $vmssName = az vmss list --resource-group $mcResourceGroup --query "[?contains(name,'win')].name | [0]" -o tsv 2>&1
            if (-not [string]::IsNullOrWhiteSpace($vmssName)) {
                # Find the VMSS instance ID for this node
                $instances = az vmss list-instances --resource-group $mcResourceGroup --name $vmssName --query "[].{name:osProfile.computerName, instanceId:instanceId}" -o json 2>&1 | ConvertFrom-Json
                $instance = $instances | Where-Object { $nodeName -match $_.name -or $_.name -match ($nodeName -replace 'aks-win-', '') }
                if ($instance) {
                    az vmss delete-instances --resource-group $mcResourceGroup --name $vmssName --instance-ids $instance.instanceId --output none 2>&1
                    Write-Host "  VM instance removed from scale set." -ForegroundColor Green
                }
            }
            Write-Host "  Node '$nodeName' removed." -ForegroundColor Green
        }
        else {
            Write-Host "  Node '$nodeName' still has $($nonDaemonPods.Count) workload pod(s). Keeping." -ForegroundColor Gray
        }
    }
}
else {
    Write-Host "  No Windows nodes found." -ForegroundColor Gray
}

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
