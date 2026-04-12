using System.Text.RegularExpressions;
using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Azure.Storage.Blobs;
using Fkh.Models;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Fkh.Services;

public class FkhCreateContainer : FkhServiceBase
{
    private readonly GitHubAppTokenService _gitHubAppTokenService;

    public FkhCreateContainer(ILogger<FkhCreateContainer> logger, GitHubAppTokenService gitHubAppTokenService) : base(logger)
    {
        _gitHubAppTokenService = gitHubAppTokenService;
    }

    public async Task<object> CreateContainerAsync(Dictionary<string, string> parameters)
    {
        var name = parameters["name"];
        var artifactUrl = parameters["artifactUrl"];
        var adminUsername = parameters["adminUsername"];
        var adminPassword = parameters["adminPassword"];
        var githubUsername = parameters["_githubUsername"];
        var cpuRequest = parameters.TryGetValue("cpu", out var cpu) ? cpu : "500m";
        var memoryRequest = parameters.TryGetValue("memory", out var mem) ? mem : "3Gi";
        var repo = parameters.TryGetValue("repo", out var r) ? r : null;
        var project = parameters.TryGetValue("project", out var p) ? p : null;
        var useSpot = parameters.TryGetValue("spot", out var spotValue)
            && string.Equals(spotValue, "true", StringComparison.OrdinalIgnoreCase);
        var bakSasUrl = parameters.TryGetValue("databaseBackupSasUrl", out var bak) ? bak : null;

        var imageTag = GetImageTag(artifactUrl);
        var fullImage = $"{AcrLoginServer}/{AcrRepository}:{imageTag}";
        var appName = SanitizeAppName($"{githubUsername}-{name}");
        var databaseName = appName;

        if (!Regex.IsMatch(databaseName, @"^[a-zA-Z0-9_-]+$"))
            throw new ArgumentException("Name contains invalid characters. Only letters, digits, hyphens, and underscores are allowed.");

        Logger.LogInformation("Checking ACR for image {Image}", fullImage);
        await EnsureImageExistsAsync(imageTag, fullImage, artifactUrl);

        Logger.LogInformation("Image found. Ensuring a Windows node is ready...");
        var client = await GetKubernetesClientAsync();
        await EnsureWindowsNodeReadyAsync(client, useSpot);
        await CleanupPlaceholderPodAsync(client, useSpot);

        Logger.LogInformation("Creating Kubernetes resources for {AppName}...", appName);

        var deploymentName = $"{appName}-deployment";
        var serviceName = $"{appName}-service";
        var secretName = $"{appName}-secret";

        // ── Fail if deployment already exists ────────────────────────────────
        await EnsureDeploymentDoesNotExistAsync(client, deploymentName);

        // ── Check database does not exist, download backup, and restore via k8s exec ─
        var sasUrl = !string.IsNullOrWhiteSpace(bakSasUrl)
            ? bakSasUrl
            : await GetDatabaseBackupSasUrlAsync(imageTag);
        await EnsureDatabaseDoesNotExistAsync(client, databaseName);
        await RestoreDatabaseViaExecAsync(client, sasUrl, databaseName);

        // ── Get SQL disk usage after restore ─────────────────────────────────
        var diskInfo = await GetSqlDiskUsageAsync(client);

        // ── Create Kubernetes resources ──────────────────────────────────────
        await CreateAdminSecretAsync(client, secretName, adminPassword);

        var dnsLabel = appName;
        var publicDnsName = $"{dnsLabel}.{AksLocation}.cloudapp.azure.com";

        await CreateDeploymentAsync(client, deploymentName, appName, fullImage, adminUsername, secretName, publicDnsName, databaseName, cpuRequest, memoryRequest, repo, project, useSpot);
        await CreateLoadBalancerServiceAsync(client, serviceName, appName, dnsLabel);

        // Set auto-stop annotation if requested
        string? autoStopInfo = null;
        parameters.TryGetValue("autostop", out var autoStopValue);
        parameters.TryGetValue("_timezone", out var autoStopTz);
        var autoStop = ParseAutoStop(autoStopValue, autoStopTz);
        if (autoStop is not null)
        {
            await SetAutoStopAnnotationAsync(client, deploymentName, autoStop.Value.StopAt);
            autoStopInfo = $"{autoStop.Value.StopAt:yyyy-MM-dd HH:mm} UTC ({autoStop.Value.Description})";
        }

        Logger.LogInformation("Deployment {Deployment} and service {Service} created in namespace {Namespace}",
            deploymentName, serviceName, Namespace);

        return new
        {
            Message = "Container created.",
            Deployment = deploymentName,
            Service = serviceName,
            Image = fullImage,
            Fqdn = publicDnsName,
            WebClient = $"{publicDnsName}/BC?tenant=default",
            Database = databaseName,
            SqlDisk = diskInfo,
            AutoStop = autoStopInfo,
        };
    }

    private async Task EnsureDeploymentDoesNotExistAsync(Kubernetes client, string deploymentName)
    {
        try
        {
            await client.ReadNamespacedDeploymentAsync(deploymentName, Namespace);
            throw new InvalidOperationException(
                $"A deployment named '{deploymentName}' already exists in namespace '{Namespace}'. Please choose a different name or remove the existing container first.");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Expected — deployment does not exist
        }
    }

    private async Task EnsureDatabaseDoesNotExistAsync(Kubernetes client, string databaseName)
    {
        var podName = await FindMssqlPodAsync(client);
        var script = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -h -1 -W " +
            $"-Q \"SET NOCOUNT ON; IF DB_ID(N'{databaseName}') IS NOT NULL PRINT N'EXISTS' ELSE PRINT N'NOTEXISTS'\"";
        var result = await ExecInMssqlPodAsync(client, podName, script);
        if (result.Stdout.Contains("EXISTS") && !result.Stdout.Contains("NOTEXISTS"))
        {
            throw new InvalidOperationException(
                $"A database named '{databaseName}' already exists on the SQL server. Please choose a different name or remove the existing container first.");
        }
    }

    private async Task<string> GetDatabaseBackupSasUrlAsync(string imageTag)
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var blobServiceClient = new BlobServiceClient(
            new Uri($"https://{DbsStorageAccountName}.blob.core.windows.net"), credential);
        var containerClient = blobServiceClient.GetBlobContainerClient("cronus");
        var blobClient = containerClient.GetBlobClient(imageTag);

        if (!await blobClient.ExistsAsync())
        {
            throw new InvalidOperationException(
                $"Database backup blob '{imageTag}' not found in container 'cronus' on storage account '{DbsStorageAccountName}'.");
        }

        // Generate a user-delegation SAS valid for 1 hour
        var delegationKey = await blobServiceClient.GetUserDelegationKeyAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

        var sasBuilder = new Azure.Storage.Sas.BlobSasBuilder
        {
            BlobContainerName = "cronus",
            BlobName = imageTag,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
        };
        sasBuilder.SetPermissions(Azure.Storage.Sas.BlobSasPermissions.Read);

        var blobUriBuilder = new Azure.Storage.Blobs.BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(delegationKey, blobServiceClient.AccountName)
        };

        Logger.LogInformation("Generated SAS URL for blob '{BlobName}'.", imageTag);
        return blobUriBuilder.ToUri().ToString();
    }

    private async Task RestoreDatabaseViaExecAsync(Kubernetes client, string sasUrl, string databaseName)
    {
        Logger.LogInformation("Restoring database '{DatabaseName}' via k8s exec...", databaseName);
        var podName = await FindMssqlPodAsync(client);

        // Step 1: Download the backup file into the mssql pod
        Logger.LogInformation("Downloading database backup to MSSQL pod...");
        var downloadScript = $"wget -O '/var/opt/mssql/data/{databaseName}.bak' '{sasUrl}' 2>&1 && echo 'DOWNLOAD_OK'";
        var downloadResult = await ExecInMssqlPodAsync(client, podName, downloadScript);
        if (!downloadResult.Stdout.Contains("DOWNLOAD_OK"))
            throw new InvalidOperationException($"Failed to download database backup for '{databaseName}'. {downloadResult}");

        // Step 2: Restore database (get logical file names + restore in one T-SQL batch via dynamic SQL)
        Logger.LogInformation("Restoring database from backup file...");
        var restoreSql = string.Join(" ",
            "SET NOCOUNT ON;",
            "DECLARE @dl nvarchar(128), @ll nvarchar(128);",
            "CREATE TABLE #f (LogicalName nvarchar(128), PhysicalName nvarchar(260), [Type] char(1),",
            "FileGroupName nvarchar(128), [Size] numeric(20,0), MaxSize numeric(20,0),",
            "FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0),",
            "UniqueId uniqueidentifier, ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0),",
            "BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int,",
            "LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0),",
            "DifferentialBaseGUID uniqueidentifier, IsReadOnly bit, IsPresent bit,",
            "TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360));",
            $"INSERT INTO #f EXEC(N'RESTORE FILELISTONLY FROM DISK=N''/var/opt/mssql/data/{databaseName}.bak''');",
            "SELECT @dl=LogicalName FROM #f WHERE [Type]=N'D';",
            "SELECT @ll=LogicalName FROM #f WHERE [Type]=N'L';",
            "DROP TABLE #f;",
            "DECLARE @q char(1)=CHAR(39);",
            $"DECLARE @s nvarchar(max)=N'RESTORE DATABASE [{databaseName}] FROM DISK=N'+@q+N'/var/opt/mssql/data/{databaseName}.bak'+@q+",
            $"N' WITH MOVE N'+@q+@dl+@q+N' TO N'+@q+N'/var/opt/mssql/data/{databaseName}.mdf'+@q+",
            $"N', MOVE N'+@q+@ll+@q+N' TO N'+@q+N'/var/opt/mssql/log/{databaseName}_log.ldf'+@q+",
            "N', REPLACE';",
            "EXEC sp_executesql @s;",
            "PRINT N'RESTORE_COMPLETE'"
        );
        var restoreScript = $"{SqlcmdPath} -S localhost -U sa -P \"$MSSQL_SA_PASSWORD\" -C -b -Q \"{restoreSql}\"";
        var restoreResult = await ExecInMssqlPodAsync(client, podName, restoreScript);
        if (!restoreResult.Stdout.Contains("RESTORE_COMPLETE"))
            throw new InvalidOperationException($"Failed to restore database '{databaseName}'. {restoreResult}");

        // Step 3: Clean up the backup file
        await ExecInMssqlPodAsync(client, podName, $"rm -f '/var/opt/mssql/data/{databaseName}.bak'");
        Logger.LogInformation("Database '{DatabaseName}' restored successfully.", databaseName);
    }

    private async Task<string> GetSqlDiskUsageAsync(Kubernetes client)
    {
        try
        {
            var podName = await FindMssqlPodAsync(client);
            var result = await ExecInMssqlPodAsync(client, podName, "df -h /var/opt/mssql/data | tail -1 | awk '{print $3 \" used / \" $2 \" total (\" $5 \" full)\"}'");
            var info = result.Stdout.Trim();
            return string.IsNullOrEmpty(info) ? "unknown" : info;
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task EnsureImageExistsAsync(string tag, string fullImage, string artifactUrl)
    {
#pragma warning disable CS0618
        var credential = new ManagedIdentityCredential(ClientId);
#pragma warning restore CS0618
        var client = new ContainerRegistryClient(new Uri($"https://{AcrLoginServer}"), credential);

        try
        {
            var artifact = client.GetArtifact(AcrRepository, tag);
            await artifact.GetManifestPropertiesAsync();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            Logger.LogInformation("Image not found in ACR. Triggering createImages workflow for {ArtifactUrl}...", artifactUrl);
            await _gitHubAppTokenService.TriggerCreateImagesWorkflowAsync(artifactUrl);
            throw new RetryAfterException(
                $"Image does not exist yet: {fullImage}. The createImages workflow has been triggered automatically.",
                retryAfterSeconds: 300);
        }
    }

    private async Task CreateAdminSecretAsync(Kubernetes client, string secretName, string adminPassword)
    {
        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta { Name = secretName, NamespaceProperty = Namespace },
            Type = "Opaque",
            Data = new Dictionary<string, byte[]>
            {
                ["password"] = System.Text.Encoding.UTF8.GetBytes(adminPassword)
            }
        };

        try { await client.DeleteNamespacedSecretAsync(secretName, Namespace); } catch { /* may not exist */ }
        await client.CreateNamespacedSecretAsync(secret, Namespace);
    }

    private async Task CreateDeploymentAsync(
        Kubernetes client, string deploymentName, string appName, string fullImage,
        string adminUsername, string secretName, string publicDnsName, string databaseName,
        string cpuRequest, string memoryRequest, string? repo, string? project, bool useSpot)
    {
        var annotations = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(repo))
            annotations["fkh/repo"] = repo;
        if (!string.IsNullOrWhiteSpace(project))
            annotations["fkh/project"] = project;

        var nodeSelector = new Dictionary<string, string>
        {
            ["kubernetes.io/os"] = "windows"
        };
        if (useSpot)
        {
            nodeSelector["kubernetes.azure.com/scalesetpriority"] = "spot";
        }

        var tolerations = new List<V1Toleration>();
        if (useSpot)
        {
            tolerations.Add(new V1Toleration
            {
                Key = "kubernetes.azure.com/scalesetpriority",
                OperatorProperty = "Equal",
                Value = "spot",
                Effect = "NoSchedule"
            });
        }

        var deployment = new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                Name = deploymentName,
                NamespaceProperty = Namespace,
                Annotations = annotations.Count > 0 ? annotations : null
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = appName }
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = appName,
                            ["app-type"] = "windows-servicetier"
                        }
                    },
                    Spec = new V1PodSpec
                    {
                        NodeSelector = nodeSelector,
                        Tolerations = tolerations.Count > 0 ? tolerations : null,
                        Affinity = new V1Affinity
                        {
                            PodAntiAffinity = new V1PodAntiAffinity
                            {
                                PreferredDuringSchedulingIgnoredDuringExecution = new List<V1WeightedPodAffinityTerm>
                                {
                                    new()
                                    {
                                        Weight = 100,
                                        PodAffinityTerm = new V1PodAffinityTerm
                                        {
                                            LabelSelector = new V1LabelSelector
                                            {
                                                MatchLabels = new Dictionary<string, string>
                                                {
                                                    ["app-type"] = "windows-servicetier"
                                                }
                                            },
                                            TopologyKey = "kubernetes.io/hostname"
                                        }
                                    }
                                }
                            }
                        },
                        Containers = new List<V1Container>
                        {
                            new()
                            {
                                Name = appName,
                                Image = fullImage,
                                Ports = new List<V1ContainerPort>
                                {
                                    new() { ContainerPort = 80 }, new() { ContainerPort = 443 }, new() { ContainerPort = 7047 }, new() { ContainerPort = 7048 }, new() { ContainerPort = 7049 },
                                },
                                Env = BuildEnvVars(adminUsername, secretName, publicDnsName, databaseName),
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity(cpuRequest),
                                        ["memory"] = new ResourceQuantity(memoryRequest)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        await client.CreateNamespacedDeploymentAsync(deployment, Namespace);
    }

    private List<V1EnvVar> BuildEnvVars(string adminUsername, string secretName, string publicDnsName, string databaseName)
    {
        return new List<V1EnvVar>
        {
            new() { Name = "accept_eula", Value = "Y" },
            new() { Name = "username", Value = adminUsername },
            new()
            {
                Name = "password",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector { Name = secretName, Key = "password" }
                }
            },
            new() { Name = "publicDnsName", Value = publicDnsName },
            new() { Name = "contactEMailForLetsEncrypt", Value = ContactEmail },
            new() { Name = "folders", Value = FoldersValue },
            new()
            {
                Name = "databasePassword",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector { Name = "mssql-secret", Key = "sa-password" }
                }
            },
            new() { Name = "databaseUsername", Value = "sa" },
            new() { Name = "databaseServer", Value = $"mssql-service.{Namespace}.svc.cluster.local" },
            new() { Name = "databaseInstance", Value = "" },
            new() { Name = "databaseName", Value = databaseName },
        };
    }

    private async Task CreateLoadBalancerServiceAsync(Kubernetes client, string serviceName, string appName, string dnsLabel)
    {
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = Namespace,
                Annotations = new Dictionary<string, string>
                {
                    ["service.beta.kubernetes.io/azure-dns-label-name"] = dnsLabel
                }
            },
            Spec = new V1ServiceSpec
            {
                Type = "LoadBalancer",
                Selector = new Dictionary<string, string> { ["app"] = appName },
                Ports = new List<V1ServicePort>
                {
                    new() { Name = "http", Port = 80, TargetPort = 80 },
                    new() { Name = "https", Port = 443, TargetPort = 443 },
                    new() { Name = "odata", Port = 7047, TargetPort = 7047 },
                    new() { Name = "soap", Port = 7048, TargetPort = 7048 },
                    new() { Name = "dev", Port = 7049, TargetPort = 7049 },
                }
            }
        };

        await client.CreateNamespacedServiceAsync(service, Namespace);
    }
}
