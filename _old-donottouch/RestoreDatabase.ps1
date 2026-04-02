<#
.SYNOPSIS
    Restores a SQL database into the SQL Server running on the AKS cluster.

.DESCRIPTION
    This script assumes CreateKubernetesCluster.ps1 has already been run. It:
    1. Connects to the existing AKS cluster.
    2. Downloads a SQL database backup from a direct download URL.
    3. Restores it into the running SQL Server pod.

.PARAMETER ResourceGroupName
    The Azure resource group containing the AKS cluster.

.PARAMETER ClusterName
    The AKS cluster name.

.PARAMETER DatabaseBackupUrl
    A direct download URL for the .bak file to restore into SQL Server.

.PARAMETER DatabaseName
    Name to give the restored database. Defaults to 'fk-db'.
#>
param(
    [Parameter(Mandatory = $false)]
    [string] $ResourceGroupName = "fk-rg",

    [Parameter(Mandatory = $false)]
    [string] $ClusterName = "fk-aks",

    [Parameter(Mandatory = $false)]
    [string] $DatabaseBackupUrl = 'https://www.dropbox.com/scl/fi/wvegnqluiqjcr5s0cu2vr/Demo-Database-BC-24-0.bak?rlkey=vd53mcxhibp4vv5xkvxd3jwv5&dl=1',

    [Parameter(Mandatory = $false)]
    [string] $DatabaseName = "fk-db"
)

$ErrorActionPreference = "Stop"
$namespace = "app-workload"

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
# Verify SQL Server is running
# ============================================================================
Write-Host "Verifying SQL Server pod is running..." -ForegroundColor Cyan

$sqlPodName = kubectl get pods -n $namespace -l app=mssql -o jsonpath='{.items[0].metadata.name}' 2>&1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sqlPodName)) {
    throw "No SQL Server pod found in namespace '$namespace'. Run CreateKubernetesCluster.ps1 first."
}

$timeout = 300
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
    Write-Host "  Waiting for SQL Server pod to become ready... ($elapsed seconds elapsed)" -ForegroundColor Gray
}
if (-not $ready) {
    throw "SQL Server pod '$sqlPodName' is not ready after $timeout seconds. Check pod status: kubectl get pods -n $namespace"
}
Write-Host "SQL Server pod '$sqlPodName' is running and ready." -ForegroundColor Green

# ============================================================================
# Retrieve SA password from Kubernetes secret
# ============================================================================
Write-Host "Retrieving SA password from Kubernetes secret..." -ForegroundColor Cyan

$saPasswordBase64 = kubectl get secret mssql-secret -n $namespace -o jsonpath='{.data.sa-password}' 2>&1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($saPasswordBase64)) {
    throw "Failed to retrieve mssql-secret. Run CreateKubernetesCluster.ps1 first."
}
$saPasswordPlain = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($saPasswordBase64))

# ============================================================================
# Download database backup directly inside SQL Server pod
# ============================================================================
Write-Host "Downloading database backup inside SQL Server pod..." -ForegroundColor Cyan

$backupFileName = "database_$($DatabaseName).bak"

kubectl exec -n $namespace $sqlPodName -- wget -q -O /tmp/$backupFileName $DatabaseBackupUrl
if ($LASTEXITCODE -ne 0) { throw "Failed to download database backup." }

kubectl exec -n $namespace $sqlPodName -- ls -lh /tmp/$backupFileName

$restoreScript = @'
#!/bin/bash
set -e
SQLCMD=/opt/mssql-tools18/bin/sqlcmd

echo "Restoring database..."
RESTORE_OUTPUT=$($SQLCMD -S localhost -U sa -P '@@SA_PASSWORD@@' -C -b -Q "
DECLARE @dataLogical NVARCHAR(128), @logLogical NVARCHAR(128);

CREATE TABLE #FileList (
    LogicalName NVARCHAR(128), PhysicalName NVARCHAR(260), Type CHAR(1),
    FileGroupName NVARCHAR(128), Size BIGINT, MaxSize BIGINT, FileId INT,
    CreateLSN NUMERIC(25,0), DropLSN NUMERIC(25,0), UniqueId UNIQUEIDENTIFIER,
    ReadOnlyLSN NUMERIC(25,0), ReadWriteLSN NUMERIC(25,0), BackupSizeInBytes BIGINT,
    SourceBlockSize INT, FileGroupId INT, LogGroupGUID UNIQUEIDENTIFIER,
    DifferentialBaseLSN NUMERIC(25,0), DifferentialBaseGUID UNIQUEIDENTIFIER,
    IsReadOnly BIT, IsPresent BIT, TDEThumbprint VARBINARY(32), SnapshotUrl NVARCHAR(360)
);

INSERT INTO #FileList EXEC('RESTORE FILELISTONLY FROM DISK = ''/tmp/@@BACKUP_FILE@@''');

SELECT @dataLogical = LogicalName FROM #FileList WHERE Type = 'D';
SELECT @logLogical  = LogicalName FROM #FileList WHERE Type = 'L';

PRINT 'Logical data file: ' + @dataLogical;
PRINT 'Logical log file:  ' + @logLogical;

RESTORE DATABASE [@@DB_NAME@@]
FROM DISK = '/tmp/@@BACKUP_FILE@@'
WITH MOVE @dataLogical TO '/var/opt/mssql/data/@@DB_NAME@@.mdf',
     MOVE @logLogical  TO '/var/opt/mssql/log/@@DB_NAME@@_log.ldf',
     REPLACE;

DROP TABLE #FileList;
PRINT 'Database [@@DB_NAME@@] restored successfully.';
" 2>&1)

echo "$RESTORE_OUTPUT"

if echo "$RESTORE_OUTPUT" | grep -qi "terminating abnormally\|Msg [0-9]*, Level 1[6-9]\|Msg [0-9]*, Level 2[0-9]"; then
    echo "ERROR: Database restore failed!" >&2
    exit 1
fi

# Clean up backup file
rm -f /tmp/@@BACKUP_FILE@@
'@

# Replace placeholders with actual values and fix Windows line endings for Linux
$restoreScript = $restoreScript.Replace('@@SA_PASSWORD@@', $saPasswordPlain).Replace('@@BACKUP_FILE@@', $backupFileName).Replace('@@DB_NAME@@', $DatabaseName).Replace("`r", "")

$restoreScript | kubectl exec -i -n $namespace $sqlPodName -- /bin/bash
if ($LASTEXITCODE -ne 0) { throw "Failed to restore database '$DatabaseName'." }

# ============================================================================
# Summary
# ============================================================================
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Database Restore Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Cluster:            $ClusterName" -ForegroundColor Gray
Write-Host "  Namespace:          $namespace" -ForegroundColor Gray
Write-Host "  Database:           $DatabaseName" -ForegroundColor Gray
Write-Host "  SQL Server:         mssql-service.$namespace.svc.cluster.local:1433" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next step: Run DeployServiceTier.ps1 to deploy the Windows container." -ForegroundColor Yellow
