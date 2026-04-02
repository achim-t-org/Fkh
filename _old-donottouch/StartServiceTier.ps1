<#
.SYNOPSIS
    Starts a previously stopped service tier by starting its deallocated Windows node VM.

.DESCRIPTION
    This script reads the VMSS instance info saved by StopServiceTier.ps1, starts the
    deallocated VM, waits for the Kubernetes node to become Ready, uncordons it, and
    scales the deployment back to 1 replica.

    Because the VM disk is preserved (including the Docker image cache), the container
    starts much faster than a fresh deployment — no image pull needed.

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
# Read saved VMSS info from deployment annotations
# ============================================================================
Write-Host "Reading stop info from deployment annotations..." -ForegroundColor Cyan

$nodeName = kubectl get deployment $deploymentName -n $namespace -o jsonpath='{.metadata.annotations.stoppedOnNode}' 2>&1
$mcResourceGroup = kubectl get deployment $deploymentName -n $namespace -o jsonpath='{.metadata.annotations.stoppedVmssRg}' 2>&1
$vmssName = kubectl get deployment $deploymentName -n $namespace -o jsonpath='{.metadata.annotations.stoppedVmssName}' 2>&1
$instanceId = kubectl get deployment $deploymentName -n $namespace -o jsonpath='{.metadata.annotations.stoppedInstanceId}' 2>&1

if ([string]::IsNullOrWhiteSpace($nodeName) -or [string]::IsNullOrWhiteSpace($instanceId)) {
    throw "No stop info found on deployment '$deploymentName'. Was StopServiceTier.ps1 used to stop it?"
}

Write-Host "  Node: $nodeName, VMSS: $vmssName, Instance: $instanceId" -ForegroundColor Gray

# ============================================================================
# Start the deallocated VMSS instance
# ============================================================================
Write-Host "Starting VM instance '$instanceId' in VMSS '$vmssName'..." -ForegroundColor Cyan

az vmss start --resource-group $mcResourceGroup --name $vmssName --instance-ids $instanceId --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to start VMSS instance." }
Write-Host "  VM start command issued." -ForegroundColor Green

# ============================================================================
# Wait for Kubernetes node to become Ready
# ============================================================================
Write-Host "Waiting for node '$nodeName' to become Ready..." -ForegroundColor Cyan

$timeout = 300
$elapsed = 0
$nodeReady = $false
while ($elapsed -lt $timeout) {
    $status = kubectl get node $nodeName -o jsonpath='{.status.conditions[?(@.type=="Ready")].status}' 2>&1
    if ($status -eq "True") {
        $nodeReady = $true
        Write-Host "  Node '$nodeName' is Ready." -ForegroundColor Green
        break
    }
    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "  Waiting... ($elapsed seconds elapsed)" -ForegroundColor Gray
}

if (-not $nodeReady) {
    throw "Node '$nodeName' did not become Ready within $timeout seconds."
}

# ============================================================================
# Uncordon the node and remove autoscaler protection
# ============================================================================
Write-Host "Uncordoning node '$nodeName'..." -ForegroundColor Cyan

kubectl uncordon $nodeName 2>&1 | Out-Null
kubectl annotate node $nodeName "cluster-autoscaler.kubernetes.io/scale-down-disabled-" --overwrite 2>&1 | Out-Null
Write-Host "  Node '$nodeName' uncordoned and ready for scheduling." -ForegroundColor Green

# ============================================================================
# Scale deployment back to 1
# ============================================================================
Write-Host "Scaling deployment '$deploymentName' to 1 replica..." -ForegroundColor Cyan

kubectl scale deployment $deploymentName -n $namespace --replicas=1
if ($LASTEXITCODE -ne 0) { throw "Failed to scale deployment '$deploymentName'." }
Write-Host "Deployment '$deploymentName' scaled to 1." -ForegroundColor Green

# ============================================================================
# Wait for pod to be running
# ============================================================================
Write-Host "Waiting for pod to start..." -ForegroundColor Cyan

$timeout = 300
$elapsed = 0
$phase = ""
while ($elapsed -lt $timeout) {
    $phase = kubectl get pods -n $namespace -l "app=$appName" -o jsonpath='{.items[0].status.phase}' 2>&1
    if ($phase -eq "Running") {
        Write-Host "  Pod is running." -ForegroundColor Green
        break
    }
    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "  Waiting... ($elapsed seconds elapsed, status: $phase)" -ForegroundColor Gray
}

if ($phase -ne "Running") {
    Write-Host "  Pod not yet running after $timeout seconds. Check status with:" -ForegroundColor Yellow
    Write-Host "    kubectl get pods -n $namespace -l app=$appName" -ForegroundColor Yellow
}

# ============================================================================
# Clean up annotations and show public IP
# ============================================================================
kubectl annotate deployment $deploymentName -n $namespace "stoppedOnNode-" "stoppedVmssRg-" "stoppedVmssName-" "stoppedInstanceId-" --overwrite 2>&1 | Out-Null

$publicIp = kubectl get svc $serviceName -n $namespace -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>&1
$location = az aks show --resource-group $ResourceGroupName --name $ClusterName --query "location" -o tsv 2>&1
$fqdn = "$DnsNameLabel.$location.cloudapp.azure.com"

Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Service Tier Started" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  Deployment:         $deploymentName" -ForegroundColor Gray
Write-Host "  Node:               $nodeName" -ForegroundColor Gray
Write-Host "  FQDN:              $fqdn" -ForegroundColor Gray
if (-not [string]::IsNullOrWhiteSpace($publicIp) -and $publicIp -ne "<no value>") {
    Write-Host "  Public IP:          $publicIp" -ForegroundColor Gray
}
Write-Host "============================================================" -ForegroundColor Cyan
