<#
.SYNOPSIS
    Stops a service tier temporarily by deallocating its Windows node VM.

.DESCRIPTION
    This script scales the deployment to 0 replicas, cordons and drains the
    Windows node, then deallocates the underlying VMSS instance.

    Deallocating (instead of deleting) preserves the VM disk and Docker image
    cache, so restarting with StartServiceTier.ps1 is much faster — no need to
    re-pull the container image.

    A deallocated VM incurs no compute cost (only disk storage, which is minimal).
    The deployment, service, and secret remain intact.

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
# Find which node the pod is running on
# ============================================================================
Write-Host "Finding node for deployment '$deploymentName'..." -ForegroundColor Cyan

$nodeName = kubectl get pods -n $namespace -l "app=$appName" -o jsonpath='{.items[0].spec.nodeName}' 2>&1
if ([string]::IsNullOrWhiteSpace($nodeName)) {
    throw "No running pod found for '$appName'. Is the service tier already stopped?"
}
Write-Host "  Pod is running on node '$nodeName'." -ForegroundColor Gray

# Save the node name as an annotation on the deployment so StartServiceTier can find it
kubectl annotate deployment $deploymentName -n $namespace "stoppedOnNode=$nodeName" --overwrite 2>&1 | Out-Null

# ============================================================================
# Scale deployment to 0
# ============================================================================
Write-Host "Scaling deployment '$deploymentName' to 0 replicas..." -ForegroundColor Cyan

kubectl scale deployment $deploymentName -n $namespace --replicas=0
if ($LASTEXITCODE -ne 0) { throw "Failed to scale deployment '$deploymentName'." }
Write-Host "Deployment '$deploymentName' scaled to 0." -ForegroundColor Green

# Wait for pod to terminate
Start-Sleep -Seconds 5

# ============================================================================
# Cordon and drain the node
# ============================================================================
Write-Host "Cordoning and draining node '$nodeName'..." -ForegroundColor Cyan

kubectl cordon $nodeName 2>&1 | Out-Null
kubectl drain $nodeName --ignore-daemonsets --delete-emptydir-data --force --timeout=120s 2>&1 | Out-Null

# Prevent the cluster autoscaler from deleting this node while it is deallocated
kubectl annotate node $nodeName "cluster-autoscaler.kubernetes.io/scale-down-disabled=true" --overwrite 2>&1 | Out-Null
Write-Host "  Node '$nodeName' cordoned and drained." -ForegroundColor Green

# ============================================================================
# Deallocate the VMSS instance (stop VM, keep disk)
# ============================================================================
Write-Host "Deallocating VM for node '$nodeName'..." -ForegroundColor Cyan

$mcResourceGroup = az aks show --resource-group $ResourceGroupName --name $ClusterName --query "nodeResourceGroup" -o tsv 2>&1
if ($LASTEXITCODE -ne 0) { throw "Failed to get node resource group." }

$vmssName = az vmss list --resource-group $mcResourceGroup --query "[?contains(name,'win')].name | [0]" -o tsv 2>&1
if ([string]::IsNullOrWhiteSpace($vmssName)) { throw "Could not find Windows VMSS in '$mcResourceGroup'." }

$instances = az vmss list-instances --resource-group $mcResourceGroup --name $vmssName --query "[].{name:osProfile.computerName, instanceId:instanceId}" -o json 2>&1 | ConvertFrom-Json
$instance = $instances | Where-Object { $nodeName -match $_.name -or $_.name -match ($nodeName -replace 'aks-win-', '') }

if (-not $instance) { throw "Could not find VMSS instance for node '$nodeName'." }

az vmss deallocate --resource-group $mcResourceGroup --name $vmssName --instance-ids $instance.instanceId --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to deallocate VMSS instance." }
Write-Host "  VM deallocated (disk preserved, no compute cost)." -ForegroundColor Green

# Save VMSS info as annotations for StartServiceTier
kubectl annotate deployment $deploymentName -n $namespace "stoppedVmssRg=$mcResourceGroup" --overwrite 2>&1 | Out-Null
kubectl annotate deployment $deploymentName -n $namespace "stoppedVmssName=$vmssName" --overwrite 2>&1 | Out-Null
kubectl annotate deployment $deploymentName -n $namespace "stoppedInstanceId=$($instance.instanceId)" --overwrite 2>&1 | Out-Null

# ============================================================================
# Summary
# ============================================================================
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Service Tier Stopped (VM Deallocated)" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  Deployment:         $deploymentName (0 replicas)" -ForegroundColor Gray
Write-Host "  Node:               $nodeName (cordoned, VM deallocated)" -ForegroundColor Gray
Write-Host "  VM cost:            None (only disk storage)" -ForegroundColor Gray
Write-Host "  Docker image cache: Preserved" -ForegroundColor Gray
Write-Host "  To restart:         .\StartServiceTier.ps1 -DnsNameLabel $DnsNameLabel" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Cyan
