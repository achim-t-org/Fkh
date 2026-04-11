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
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the container automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
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
                }
            }
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
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the container automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
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
                    DefaultValue = "false"
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
            Name = "GetContainerLogs",
            Description = "Gets logs from a container.",
            Route = "GetContainerLogs",
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
                    Name = "tail",
                    Type = "string",
                    Description = "Number of lines to retrieve from the end of the log.",
                    Required = false,
                    DefaultValue = "500"
                }
            }
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
