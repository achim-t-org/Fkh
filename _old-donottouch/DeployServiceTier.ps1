<#
.SYNOPSIS
    Deploys a Windows container (service tier) on the AKS cluster with a public IP and DNS name.

.DESCRIPTION
    This script assumes CreateKubernetesCluster.ps1 and RestoreDatabase.ps1 have already been run. It:
    1. Connects to the existing AKS cluster.
    2. Deploys a Windows container with database connection env variables.
    3. Creates a LoadBalancer service with a public IP and DNS name.
    4. Exposes ports 80, 443, 7047, 7048, 7049.

    Each call deploys exactly one service tier. Multiple calls with different DnsNameLabel
    values will create additional deployments and services without affecting existing ones.

    The Windows container receives these environment variables:
    - publicDnsName     : The public FQDN pointing to this pod
    - databaseServer    : The internal cluster DNS for SQL Server
    - databaseName      : The name of the restored database
    - databasePassword  : The SA password (from Kubernetes secret)
    - Any additional env variables specified via -AdditionalEnvVariables

.PARAMETER ResourceGroupName
    The Azure resource group containing the AKS cluster.

.PARAMETER ClusterName
    The AKS cluster name.

.PARAMETER WindowsImage
    The Docker image to run on the Windows node (e.g. 'myregistry.azurecr.io/myapp:latest').

.PARAMETER DatabaseName
    Name of the database to connect to. Defaults to 'fk-db'.

.PARAMETER Username
    Username for the BC service tier.

.PARAMETER Password
    Password for the BC service tier. Will be stored as a Kubernetes secret.

.PARAMETER DnsNameLabel
    DNS name label for the public IP. The full FQDN will be <label>.<region>.cloudapp.azure.com.
    Defaults to the ClusterName.

.PARAMETER AdditionalEnvVariables
    Optional hashtable of additional environment variables to pass to the Windows container.

.PARAMETER ContactEMailForLetsEncrypt
    Contact email for Let's Encrypt certificate generation. Defaults to 'fk@freddy.dk'.
#>
param(
    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName = "fk-rg",

    [Parameter(Mandatory = $false)]
    [string] $ClusterName = "fk-aks",

    [Parameter(Mandatory = $false)]
    [string] $WindowsImage = "acrfreddydk.azurecr.io/my:test",

    [Parameter(Mandatory = $false)]
    [string] $DatabaseName = "fk-db",

    [Parameter(Mandatory = $false)]
    [string] $Username = "admin",

    [Parameter(Mandatory = $false)]
    [securestring] $Password = (ConvertTo-SecureString -String "P@ssword1" -AsPlainText -Force),

    [Parameter(Mandatory = $false)]
    [string] $DnsNameLabel = "",

    [Parameter(Mandatory = $false)]
    [hashtable] $AdditionalEnvVariables = @{},

    [Parameter(Mandatory = $false)]
    [string] $ContactEMailForLetsEncrypt = "fk@freddy.dk"
)

if ([string]::IsNullOrWhiteSpace($DnsNameLabel)) {
    $DnsNameLabel = $ClusterName.ToLower()
}

$ErrorActionPreference = "Stop"
$namespace = "app-workload"
$appName = "windows-app-$DnsNameLabel"
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

# Determine the region for FQDN
$location = az aks show --resource-group $ResourceGroupName --name $ClusterName --query "location" -o tsv 2>&1
if ($LASTEXITCODE -ne 0) { throw "Failed to get AKS cluster location." }
$fqdn = "$DnsNameLabel.$location.cloudapp.azure.com"

# ============================================================================
# Create Kubernetes secret for BC password
# ============================================================================
Write-Host "Creating Kubernetes secret '$secretName'..." -ForegroundColor Cyan

$passwordPlain = [System.Net.NetworkCredential]::new("", $Password).Password
$passwordBase64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($passwordPlain))

$secretYaml = @"
apiVersion: v1
kind: Secret
metadata:
  name: $secretName
  namespace: $namespace
type: Opaque
data:
  password: $passwordBase64
"@

$secretYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create Kubernetes secret '$secretName'." }
Write-Host "Secret '$secretName' created." -ForegroundColor Green

# ============================================================================
# Deploy Windows container
# ============================================================================
Write-Host "Deploying Windows container '$appName' with image '$WindowsImage'..." -ForegroundColor Cyan

# Build additional environment variable YAML entries
$additionalEnvYaml = ""
foreach ($key in $AdditionalEnvVariables.Keys) {
    $value = $AdditionalEnvVariables[$key]
    $additionalEnvYaml += @"

            - name: $key
              value: "$value"
"@
}

$windowsDeploymentYaml = @"
apiVersion: apps/v1
kind: Deployment
metadata:
  name: $appName-deployment
  namespace: $namespace
spec:
  replicas: 1
  selector:
    matchLabels:
      app: $appName
  template:
    metadata:
      labels:
        app: $appName
        app-type: windows-servicetier
    spec:
      nodeSelector:
        kubernetes.io/os: windows
      affinity:
        podAntiAffinity:
          requiredDuringSchedulingIgnoredDuringExecution:
            - labelSelector:
                matchExpressions:
                  - key: app-type
                    operator: In
                    values:
                      - windows-servicetier
              topologyKey: "kubernetes.io/hostname"
      containers:
        - name: $appName
          image: $WindowsImage
          ports:
            - containerPort: 80
              protocol: TCP
            - containerPort: 443
              protocol: TCP
            - containerPort: 7047
              protocol: TCP
            - containerPort: 7048
              protocol: TCP
            - containerPort: 7049
              protocol: TCP
            - containerPort: 5986
              protocol: TCP
          env:
            - name: accept_eula
              value: "Y"
            - name: publicDnsName
              value: "$fqdn"
            - name: username
              value: "$Username"
            - name: password
              valueFrom:
                secretKeyRef:
                  name: $secretName
                  key: password
            - name: databasePassword
              valueFrom:
                secretKeyRef:
                  name: mssql-secret
                  key: sa-password
            - name: databaseUsername
              value: "sa"
            - name: databaseServer
              value: "mssql-service.$namespace.svc.cluster.local"
            - name: databaseInstance
              value: ""
            - name: databaseName
              value: "$DatabaseName"
            - name: contactEMailForLetsEncrypt
              value: "$ContactEMailForLetsEncrypt"
            - name: folders
              value: "c:\\run\\my=https://github.com/Freddy-DK/ContainerScripts/archive/refs/heads/main.zip\\ContainerScripts-main"$additionalEnvYaml
"@

$windowsDeploymentYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create deployment '$appName-deployment'." }
Write-Host "Deployment '$appName-deployment' created." -ForegroundColor Green

# ============================================================================
# Create LoadBalancer Service for Windows container (public access)
# ============================================================================
Write-Host "Creating LoadBalancer service '$serviceName'..." -ForegroundColor Cyan

$windowsServiceYaml = @"
apiVersion: v1
kind: Service
metadata:
  name: $serviceName
  namespace: $namespace
  annotations:
    service.beta.kubernetes.io/azure-dns-label-name: "$DnsNameLabel"
spec:
  type: LoadBalancer
  selector:
    app: $appName
  ports:
    - name: http
      protocol: TCP
      port: 80
      targetPort: 80
    - name: https
      protocol: TCP
      port: 443
      targetPort: 443
    - name: odata
      protocol: TCP
      port: 7047
      targetPort: 7047
    - name: soap
      protocol: TCP
      port: 7048
      targetPort: 7048
    - name: dev
      protocol: TCP
      port: 7049
      targetPort: 7049
    - name: psremoting
      protocol: TCP
      port: 5986
      targetPort: 5986
"@

$windowsServiceYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create LoadBalancer service '$serviceName'." }
Write-Host "LoadBalancer service '$serviceName' created." -ForegroundColor Green

# ============================================================================
# Wait for public IP assignment
# ============================================================================
Write-Host "Waiting for public IP assignment..." -ForegroundColor Cyan

$timeout = 300
$elapsed = 0
$publicIp = ""
while ($elapsed -lt $timeout) {
    $publicIp = kubectl get svc $serviceName -n $namespace -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>&1
    if (-not [string]::IsNullOrWhiteSpace($publicIp) -and $publicIp -ne "<no value>") {
        break
    }

    # Check for errors in service events (e.g. InvalidDomainNameLabel)
    $events = kubectl get events -n $namespace --field-selector "involvedObject.name=$serviceName" -o json 2>&1 | ConvertFrom-Json
    $failedEvent = $events.items | Where-Object { $_.type -eq "Warning" -and $_.message -match "RESPONSE 400|Error syncing|InvalidDomainNameLabel|error" } | Select-Object -Last 1
    if ($failedEvent) {
        Write-Host ""
        Write-Host "ERROR: LoadBalancer service failed:" -ForegroundColor Red
        Write-Host "  $($failedEvent.message)" -ForegroundColor Red
        throw "Failed to create public IP for service '$serviceName'. See error above."
    }

    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "  Waiting... ($elapsed seconds elapsed)" -ForegroundColor Gray
}

if ([string]::IsNullOrWhiteSpace($publicIp) -or $publicIp -eq "<no value>") {
    Write-Host "  Public IP not yet assigned. Check later with: kubectl get svc $serviceName -n $namespace" -ForegroundColor Yellow
}
else {
    Write-Host "  Public IP: $publicIp" -ForegroundColor Green
}

# ============================================================================
# Summary
# ============================================================================
$containerName = kubectl get pod -n $namespace -l app=$appName -o jsonpath='{.items[0].metadata.name}'
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Service Tier Deployment Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  App Name:           $appName" -ForegroundColor Gray
Write-Host "  Deployment:         $appName-deployment" -ForegroundColor Gray
Write-Host "  Container Name:     $containerName" -ForegroundColor Gray
Write-Host "  Windows Image:      $WindowsImage" -ForegroundColor Gray
Write-Host "  Public IP:          $publicIp" -ForegroundColor Gray
Write-Host "  FQDN:               $fqdn" -ForegroundColor Gray
Write-Host "  Ports:              80, 443, 5986, 7047, 7048, 7049" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Windows container env variables:" -ForegroundColor Yellow
Write-Host "  publicDnsName    = $fqdn" -ForegroundColor Gray
Write-Host "  databaseServer   = mssql-service.$namespace.svc.cluster.local" -ForegroundColor Gray
Write-Host "  databaseName     = $DatabaseName" -ForegroundColor Gray
Write-Host "  databasePassword = (from Kubernetes secret)" -ForegroundColor Gray
foreach ($key in $AdditionalEnvVariables.Keys) {
    Write-Host "  $key = $($AdditionalEnvVariables[$key])" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Yellow
Write-Host "  kubectl get pods -n $namespace" -ForegroundColor Gray
Write-Host "  kubectl get svc -n $namespace" -ForegroundColor Gray
Write-Host "  kubectl logs -n $namespace -l app=$appName --tail -1" -ForegroundColor Gray
Write-Host "  kubectl exec -it -n $namespace $containerName -- powershell" -ForegroundColor Gray
Write-Host ""
Write-Host "PowerShell Remoting (SSL):" -ForegroundColor Yellow
Write-Host "  `$cred = Get-Credential" -ForegroundColor Gray
Write-Host "  `$session = New-PSSession -ComputerName $fqdn -Port 5986 -Credential `$cred -UseSSL -Authentication Basic -SessionOption (New-PSSessionOption -SkipCACheck)" -ForegroundColor Gray
Write-Host "  Enter-PSSession -Session `$session" -ForegroundColor Gray
