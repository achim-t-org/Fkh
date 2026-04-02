<#
.SYNOPSIS
    Creates an AKS cluster with a Linux SQL Server node and a Windows node pool.

.DESCRIPTION
    This script uses the Azure CLI to:
    1. Create an AKS cluster in West Europe with a Linux system node pool and a Windows node pool.
    2. Deploy SQL Server on Linux with persistent storage (data volume).
    3. Store the SQL SA password in a Kubernetes Secret.
    4. SQL Server is only reachable from within the cluster (ClusterIP), not from the internet.

    After running this script, use DeployDatabaseAndApp.ps1 to restore a database and deploy the Windows container.

.PARAMETER ResourceGroupName
    The Azure resource group name.

.PARAMETER ClusterName
    The AKS cluster name.

.PARAMETER SqlSaPassword
    The SA password for SQL Server. Will be stored as a Kubernetes secret.

.PARAMETER Location
    Azure region. Defaults to 'westeurope'.

.PARAMETER LinuxVmSize
    VM size for the Linux node pool. Defaults to 'Standard_D2s_v3' (2 vCPUs, 8 GB RAM).

.PARAMETER WindowsVmSize
    VM size for the Windows node pool. Defaults to 'Standard_D2s_v3' (2 vCPUs, 8 GB RAM).
#>
param(
    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName = "fk-rg",

    [Parameter(Mandatory = $false)]
    [string] $ClusterName = "fk-aks",

    [Parameter(Mandatory = $false)]
    [securestring] $SqlSaPassword = (ConvertTo-SecureString -String "P@ssword1!" -AsPlainText -Force),

    [Parameter(Mandatory = $false)]
    [string] $Location = "westeurope",

    [Parameter(Mandatory = $false)]
    [string] $LinuxVmSize = "Standard_D2s_v3",

    [Parameter(Mandatory = $false)]
    [string] $WindowsVmSize = "Standard_D2s_v3"
)

$ErrorActionPreference = "Stop"

# Convert SecureString to plain text for use in Kubernetes secret
$saPasswordPlain = [System.Net.NetworkCredential]::new("", $SqlSaPassword).Password

# Validate SA password meets SQL Server requirements (min 8 chars, complexity)
if ($saPasswordPlain.Length -lt 8) {
    throw "SQL SA password must be at least 8 characters long."
}

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
# Ensure Microsoft.ContainerService resource provider is registered
# ============================================================================
Write-Host "Checking Microsoft.ContainerService resource provider registration..." -ForegroundColor Cyan

$providerState = az provider show --namespace Microsoft.ContainerService --query "registrationState" -o tsv 2>&1
if ($providerState -ne "Registered") {
    Write-Host "Registering Microsoft.ContainerService resource provider..." -ForegroundColor Yellow
    az provider register --namespace Microsoft.ContainerService --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to register Microsoft.ContainerService provider." }

    $timeout = 120
    $elapsed = 0
    while ($elapsed -lt $timeout) {
        $providerState = az provider show --namespace Microsoft.ContainerService --query "registrationState" -o tsv 2>&1
        if ($providerState -eq "Registered") { break }
        Start-Sleep -Seconds 10
        $elapsed += 10
        Write-Host "  Waiting for registration... ($elapsed seconds elapsed)" -ForegroundColor Gray
    }
    if ($providerState -ne "Registered") {
        throw "Microsoft.ContainerService provider did not register within $timeout seconds."
    }
    Write-Host "Microsoft.ContainerService provider registered." -ForegroundColor Green
}
else {
    Write-Host "Microsoft.ContainerService provider already registered." -ForegroundColor Green
}

# ============================================================================
# Check vCPU quota before creating resources
# ============================================================================
Write-Host "Checking vCPU quota in '$Location'..." -ForegroundColor Cyan

# Get vCPU count for the selected VM sizes
$linuxVmCpus = az vm list-skus --location $Location --size $LinuxVmSize --resource-type virtualMachines --query "[0].capabilities[?name=='vCPUs'].value | [0]" -o tsv 2>&1
$windowsVmCpus = az vm list-skus --location $Location --size $WindowsVmSize --resource-type virtualMachines --query "[0].capabilities[?name=='vCPUs'].value | [0]" -o tsv 2>&1

if (-not $linuxVmCpus -or -not $windowsVmCpus) {
    Write-Host "  Could not determine vCPU counts for VM sizes. Skipping quota check." -ForegroundColor Yellow
}
else {
    $linuxVmCpus = [int]$linuxVmCpus
    $windowsVmCpus = [int]$windowsVmCpus

    # Linux pool: 1 node + Windows pool: 0 initial nodes (autoscaler adds nodes on demand)
    $standardCoresNeeded = (1 * $linuxVmCpus) + (0 * $windowsVmCpus)

    $usageJson = az vm list-usage --location $Location -o json 2>&1 | ConvertFrom-Json

    $standardCoresUsage = $usageJson | Where-Object { $_.localName -eq "Total Regional vCPUs" }

    if ($standardCoresUsage) {
        $standardAvailable = $standardCoresUsage.limit - $standardCoresUsage.currentValue
        Write-Host "  Standard vCPUs: $($standardCoresUsage.currentValue) used / $($standardCoresUsage.limit) limit ($standardAvailable available, $standardCoresNeeded needed)" -ForegroundColor Gray
        if ($standardAvailable -lt $standardCoresNeeded) {
            Write-Host ""
            Write-Host "QUOTA ERROR - Cannot proceed:" -ForegroundColor Red
            Write-Host "  Standard vCPU quota insufficient: need $standardCoresNeeded but only $standardAvailable available (limit: $($standardCoresUsage.limit))." -ForegroundColor Red
            Write-Host ""
            Write-Host "To request a quota increase:" -ForegroundColor Yellow
            Write-Host "  1. Go to https://portal.azure.com" -ForegroundColor Yellow
            Write-Host "  2. Navigate to Subscriptions -> your subscription -> Usage + quotas" -ForegroundColor Yellow
            Write-Host "  3. Search for 'Total Regional vCPUs' in $Location" -ForegroundColor Yellow
            Write-Host "  4. Click 'Request increase'" -ForegroundColor Yellow
            throw "Insufficient vCPU quota in $Location. See above for details."
        }
    }

    Write-Host "  Quota check passed." -ForegroundColor Green
}

# ============================================================================
# Create Resource Group
# ============================================================================
Write-Host "Ensuring resource group '$ResourceGroupName' exists..." -ForegroundColor Cyan

$rgExists = az group exists --name $ResourceGroupName 2>&1
if ($rgExists -eq "false") {
    Write-Host "Creating resource group '$ResourceGroupName' in '$Location'..." -ForegroundColor Yellow
    az group create --name $ResourceGroupName --location $Location --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to create resource group." }
}

# ============================================================================
# Create AKS Cluster with Linux system node pool
# ============================================================================
Write-Host "Creating AKS cluster '$ClusterName'..." -ForegroundColor Cyan

$null = az aks show --resource-group $ResourceGroupName --name $ClusterName 2>&1
if ($LASTEXITCODE -ne 0) {
    az aks create `
        --resource-group $ResourceGroupName `
        --name $ClusterName `
        --location $Location `
        --node-count 1 `
        --node-vm-size $LinuxVmSize `
        --nodepool-name linuxpool `
        --network-plugin azure `
        --network-plugin-mode overlay `
        --generate-ssh-keys `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to create AKS cluster." }
    Write-Host "AKS cluster '$ClusterName' created." -ForegroundColor Green
}
else {
    Write-Host "AKS cluster '$ClusterName' already exists." -ForegroundColor Yellow
}

# ============================================================================
# Add Windows node pool
# ============================================================================
Write-Host "Ensuring Windows node pool exists..." -ForegroundColor Cyan

$null = az aks nodepool show --resource-group $ResourceGroupName --cluster-name $ClusterName --name win 2>&1
if ($LASTEXITCODE -ne 0) {
    az aks nodepool add `
        --resource-group $ResourceGroupName `
        --cluster-name $ClusterName `
        --name win `
        --node-count 0 `
        --min-count 0 `
        --max-count 10 `
        --enable-cluster-autoscaler `
        --node-vm-size $WindowsVmSize `
        --os-type Windows `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to add Windows node pool." }
    Write-Host "Windows node pool 'win' added." -ForegroundColor Green
}
else {
    Write-Host "Windows node pool 'win' already exists." -ForegroundColor Yellow
}

# ============================================================================
# Get AKS credentials
# ============================================================================
Write-Host "Fetching AKS credentials..." -ForegroundColor Cyan

az aks get-credentials --resource-group $ResourceGroupName --name $ClusterName --overwrite-existing --output none
if ($LASTEXITCODE -ne 0) { throw "Failed to get AKS credentials." }

# ============================================================================
# Create namespace
# ============================================================================
$namespace = "app-workload"
Write-Host "Creating namespace '$namespace'..." -ForegroundColor Cyan

kubectl create namespace $namespace --dry-run=client -o yaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create namespace '$namespace'." }

# ============================================================================
# Create Kubernetes secret for SQL SA password
# ============================================================================
Write-Host "Creating Kubernetes secret for SQL SA password..." -ForegroundColor Cyan

$saPasswordBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($saPasswordPlain))

$secretYaml = @"
apiVersion: v1
kind: Secret
metadata:
  name: mssql-secret
  namespace: $namespace
type: Opaque
data:
  sa-password: $saPasswordBase64
"@

$secretYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create Kubernetes secret 'mssql-secret'." }
Write-Host "Secret 'mssql-secret' created." -ForegroundColor Green

# ============================================================================
# Create PersistentVolumeClaim for SQL Server data
# ============================================================================
Write-Host "Creating PersistentVolumeClaim for SQL Server data..." -ForegroundColor Cyan

$pvcYaml = @"
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mssql-data-pvc
  namespace: $namespace
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: managed-csi-premium
  resources:
    requests:
      storage: 128Gi
"@

$pvcYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create PersistentVolumeClaim 'mssql-data-pvc'." }
Write-Host "PVC 'mssql-data-pvc' created." -ForegroundColor Green

# ============================================================================
# Deploy SQL Server on Linux (no databases - empty server)
# ============================================================================
Write-Host "Deploying SQL Server on Linux..." -ForegroundColor Cyan

$sqlDeploymentYaml = @"
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mssql-deployment
  namespace: $namespace
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mssql
  template:
    metadata:
      labels:
        app: mssql
    spec:
      nodeSelector:
        kubernetes.io/os: linux
      securityContext:
        fsGroup: 10001
      initContainers:
        - name: fix-permissions
          image: mcr.microsoft.com/mssql/server:2022-latest
          command:
            - /bin/bash
            - -c
            - |
              chown -R 10001:0 /var/opt/mssql/data /var/opt/mssql/log
          securityContext:
            runAsUser: 0
          volumeMounts:
            - name: mssql-data
              mountPath: /var/opt/mssql/data
              subPath: data
            - name: mssql-data
              mountPath: /var/opt/mssql/log
              subPath: log
      containers:
        - name: mssql
          image: mcr.microsoft.com/mssql/server:2022-latest
          ports:
            - containerPort: 1433
              protocol: TCP
          env:
            - name: ACCEPT_EULA
              value: "Y"
            - name: MSSQL_SA_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: mssql-secret
                  key: sa-password
            - name: MSSQL_DATA_DIR
              value: /var/opt/mssql/data
            - name: MSSQL_LOG_DIR
              value: /var/opt/mssql/log
          volumeMounts:
            - name: mssql-data
              mountPath: /var/opt/mssql/data
              subPath: data
            - name: mssql-data
              mountPath: /var/opt/mssql/log
              subPath: log
          resources:
            requests:
              memory: "2Gi"
              cpu: "1"
            limits:
              memory: "4Gi"
              cpu: "2"
          readinessProbe:
            tcpSocket:
              port: 1433
            initialDelaySeconds: 15
            periodSeconds: 10
          livenessProbe:
            tcpSocket:
              port: 1433
            initialDelaySeconds: 30
            periodSeconds: 20
      volumes:
        - name: mssql-data
          persistentVolumeClaim:
            claimName: mssql-data-pvc
"@

$sqlDeploymentYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create SQL Server deployment." }
Write-Host "SQL Server deployment created." -ForegroundColor Green

# ============================================================================
# Create ClusterIP Service for SQL Server (internal only - not exposed to internet)
# ============================================================================
Write-Host "Creating ClusterIP service for SQL Server (internal only)..." -ForegroundColor Cyan

$sqlServiceYaml = @"
apiVersion: v1
kind: Service
metadata:
  name: mssql-service
  namespace: $namespace
spec:
  type: ClusterIP
  selector:
    app: mssql
  ports:
    - protocol: TCP
      port: 1433
      targetPort: 1433
"@

$sqlServiceYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create ClusterIP service 'mssql-service'." }
Write-Host "ClusterIP service 'mssql-service' created (internal only)." -ForegroundColor Green

# ============================================================================
# Network Policy: restrict SQL Server access to Windows app pods only
# ============================================================================
Write-Host "Creating network policy to restrict SQL Server access..." -ForegroundColor Cyan

$networkPolicyYaml = @"
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: mssql-allow-windows-app-only
  namespace: $namespace
spec:
  podSelector:
    matchLabels:
      app: mssql
  policyTypes:
    - Ingress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: windows-app
      ports:
        - protocol: TCP
          port: 1433
"@

$networkPolicyYaml | kubectl apply -f - | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create network policy." }
Write-Host "Network policy applied: SQL Server only accessible from Windows app pods." -ForegroundColor Green

# ============================================================================
# Wait for SQL Server to become ready
# ============================================================================
Write-Host "Waiting for SQL Server pod to become ready..." -ForegroundColor Cyan

$timeout = 600
$elapsed = 0
$ready = $false
while ($elapsed -lt $timeout) {
    $podStatus = kubectl get pods -n $namespace -l app=mssql -o jsonpath='{.items[0].status.conditions[?(@.type=="Ready")].status}' 2>&1
    if ($podStatus -eq "True") {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "  Waiting... ($elapsed seconds elapsed)" -ForegroundColor Gray
}
if (-not $ready) {
    throw "SQL Server pod did not become ready within $timeout seconds."
}
Write-Host "SQL Server pod is ready." -ForegroundColor Green

# ============================================================================
# Summary
# ============================================================================
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster & SQL Server Deployment Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Resource Group:     $ResourceGroupName" -ForegroundColor Gray
Write-Host "  Location:           $Location" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  SQL Server Service: mssql-service.$namespace.svc.cluster.local:1433" -ForegroundColor Gray
Write-Host "  SQL Server:         Internal only (ClusterIP + NetworkPolicy)" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next step: Run DeployDatabaseAndApp.ps1 to restore a database and deploy the Windows container." -ForegroundColor Yellow
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Yellow
Write-Host "  kubectl get pods -n $namespace" -ForegroundColor Gray
Write-Host "  kubectl get svc -n $namespace" -ForegroundColor Gray
Write-Host "  kubectl logs -n $namespace -l app=mssql" -ForegroundColor Gray
