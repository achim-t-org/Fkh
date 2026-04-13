using Fkh.Models;

namespace Fkh;

public static class FunctionCatalog
{
    public static List<FunctionDefinition> Functions { get; } = new()
    {
        new FunctionDefinition
        {
            Name = "CreateContainer",
            Description = "Creates a container using the provided artifact and admin credentials.",
            Route = "CreateContainer",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name for the container. Combined with the GitHub username to form the container name.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "artifactUrl",
                    Type = "string",
                    Description = "Artifact URL used by the container provisioning workflow.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminUsername",
                    Type = "string",
                    Description = "Administrator username for the container.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminPassword",
                    Type = "string",
                    Description = "Administrator password for the container.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useDatabase",
                    Type = "string",
                    Description = "Database to restore. Can be a SAS URL to a .bak file, or 'name/version' referencing an uploaded database (use 'latest' for the most recent version).",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "When to auto-stop the container. Use '<n>h' for hours from now (e.g. '4h') or a time of day in UTC (e.g. '18:00' or '6PM'). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "cpu",
                    Type = "string",
                    Description = "CPU cores to request for the container (e.g. '500m', '1', '2').",
                    Required = false,
                    DefaultValue = "500m"
                },
                new()
                {
                    Name = "memory",
                    Type = "string",
                    Description = "Memory to request for the container (e.g. '3Gi', '4Gi', '8Gi').",
                    Required = false,
                    DefaultValue = "3Gi"
                },
                new()
                {
                    Name = "repo",
                    Type = "string",
                    Description = "Source repository (e.g. 'org/repo'). Stored as metadata on the container.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "project",
                    Type = "string",
                    Description = "AL-Go project name. Stored as metadata on the container.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "spot",
                    Type = "boolean",
                    Description = "Place the container on a Spot (preemptible) VM for lower cost. The container may be evicted if Azure reclaims capacity.",
                    Required = false,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "moveAllAppsToDevScope",
                    Type = "boolean",
                    Description = "Move all published apps to dev scope after database restore. Sets Published As to Dev and clears uninstalled app records.",
                    Required = false,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "RemoveContainer",
            Description = "Removes a container and its database. The full resource name is derived from your GitHub username and the name you provide.",
            Route = "RemoveContainer",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to remove (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StopContainer",
            Description = "Stops a container by scaling its deployment to 0 replicas.",
            Route = "StopContainer",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to stop (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StopAllContainers",
            Description = "Stops all running containers in the cluster by scaling their deployments to 0 replicas (admin only).",
            Route = "StopAllContainers",
            Parameters = new List<FunctionParameterDefinition>()
        },
        new FunctionDefinition
        {
            Name = "StartContainer",
            Description = "Starts a previously stopped container by scaling its deployment to 1 replica.",
            Route = "StartContainer",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to start (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "When to auto-stop the container. Use '<n>h' for hours from now (e.g. '4h') or a time of day in UTC (e.g. '18:00' or '6PM'). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "ExtendAutoStop",
            Description = "Extends the auto-stop time of a running container by 2 hours.",
            Route = "ExtendAutoStop",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to extend auto-stop for.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "ListContainers",
            Description = "Lists containers. By default lists only your own containers. Set 'all' to 'true' to list all containers.",
            Route = "ListContainers",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "all",
                    Type = "boolean",
                    Description = "List all containers instead of only your own.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "AllowSqlAccess",
            Description = "Opens external SQL Server access for your IP address. Creates a temporary LoadBalancer service and network policy.",
            Route = "AllowSqlAccess",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "ip",
                    Type = "string",
                    Description = "Your public IP address (e.g. 203.0.113.10). VSIX and CLI auto-detect this.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "hours",
                    Type = "string",
                    Description = "Hours to keep SQL access open (e.g. '2'). Access is auto-revoked after this period.",
                    Required = false,
                    DefaultValue = "2"
                },
                new()
                {
                    Name = "mySqlPassword",
                    Type = "string",
                    Description = "If set, creates a SQL login for your GitHub username with this password and grants db_owner on all your databases.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "RevokeSqlAccess",
            Description = "Revokes your external SQL Server access immediately, removing the LoadBalancer service and network policy.",
            Route = "RevokeSqlAccess",
            Parameters = new List<FunctionParameterDefinition>()
        },
        new FunctionDefinition
        {
            Name = "CreateImage",
            Description = "Creates (builds) an image in the Azure Container Registry from a BC artifact URL. By default returns immediately after triggering the build workflow.",
            Route = "CreateImage",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "artifactUrl",
                    Type = "string",
                    Description = "Artifact URL used to build the image.",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "RemoveImage",
            Description = "Removes an image (tag or entire repository) from the Azure Container Registry and its associated database backup.",
            Route = "RemoveImage",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "repository",
                    Type = "string",
                    Description = "Repository name in the container registry (e.g. 'businesscentral').",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "tag",
                    Type = "string",
                    Description = "Specific tag to remove. If omitted, the entire repository is removed.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "ListImages",
            Description = "Lists available images in the Azure Container Registry.",
            Route = "ListImages",
            Parameters = new List<FunctionParameterDefinition>()
        },
        new FunctionDefinition
        {
            Name = "ListVMs",
            Description = "Lists Windows VMs in the Kubernetes cluster. Admin only.",
            Route = "ListVMs",
            Parameters = new List<FunctionParameterDefinition>()
        },
        new FunctionDefinition
        {
            Name = "WaitForContainer",
            Description = "Waits for a container to be ready. Polls until the container is running and ready for connections, or fails if the container enters an error state.",
            Route = "WaitForContainer",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to wait for (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "GetContainerLog",
            Description = "Gets logs from a container.",
            Route = "GetContainerLog",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to get logs from.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "tail",
                    Type = "string",
                    Description = "Number of lines to retrieve from the end of the log.",
                    Required = false,
                    DefaultValue = "500"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "GetContainerEventLog",
            Description = "Downloads the Windows Application event log from a container as an .evtx file.",
            Route = "GetContainerEventLog",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to download the event log from.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "InvokeSqlCmd",
            Description = "Executes a SQL statement against a container's database.",
            Route = "InvokeSqlCmd",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container whose database to run the SQL against.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "query",
                    Type = "string",
                    Description = "The SQL statement to execute.",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "InvokeScript",
            Description = "Invokes a PowerShell 7 (pwsh) script inside a running Business Central container.",
            Route = "InvokeScript",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to run the script in.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "command",
                    Type = "string",
                    Description = "PowerShell script to execute in the container. Use this or --file, not both.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "scriptFile",
                    Type = "file",
                    Description = "Path to a PowerShell script file (.ps1) to execute in the container. Use this or --command, not both.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "PublishApp",
            Description = "Publishes a .app file to a running Business Central container. The app is copied into the container and installed via Publish-NAVApp.",
            Route = "PublishApp",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the container to publish the app to.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "useNameAsIs",
                    Type = "boolean",
                    Description = "Use the name as-is without prefixing with your GitHub username. Name may contain hyphens. (admin only)",
                    Required = false,
                    AdminOnly = true,
                    DefaultValue = "false"
                },
                new()
                {
                    Name = "appFile",
                    Type = "file",
                    Description = "Path to the .app file to publish.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "syncMode",
                    Type = "string",
                    Description = "Sync mode for the app (Add, ForceSync, Clean, Development).",
                    Required = false,
                    DefaultValue = "Add"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "GetDatabaseUploadSas",
            Description = "Returns a SAS URL for uploading database backups to blob storage. Admin only.",
            Route = "GetDatabaseUploadSas",
            Hidden = true,
            AdminOnly = true,
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "containerName",
                    Type = "string",
                    Description = "Blob container name to get upload access to.",
                    Required = false,
                    DefaultValue = "databases"
                }
            }
        },
        new FunctionDefinition
        {
            Name = "Status",
            Description = "Returns system status including Kubernetes nodes, BC containers, SQL, storage, quotas, and security. Admin only.",
            Route = "Status",
            Hidden = true,
            AdminOnly = true,
            Parameters = new List<FunctionParameterDefinition>()
        }
    };

    public static FunctionDefinition GetRequired(string functionName)
    {
        var function = Functions.FirstOrDefault(f =>
            string.Equals(f.Name, functionName, StringComparison.OrdinalIgnoreCase));

        if (function is null)
        {
            throw new InvalidOperationException($"Function '{functionName}' is not registered in FunctionCatalog.");
        }

        return function;
    }
}
