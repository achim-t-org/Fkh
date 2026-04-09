using Fkh.Models;

namespace Fkh;

public static class FunctionCatalog
{
    public static List<FunctionDefinition> Functions { get; } = new()
    {
        new FunctionDefinition
        {
            Name = "CreatePod",
            Description = "Creates a pod using the provided artifact and admin credentials.",
            Route = "CreatePod",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name for the pod. Combined with the GitHub username to form the pod name.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "artifactUrl",
                    Type = "string",
                    Description = "Artifact URL used by the pod provisioning workflow.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminUsername",
                    Type = "string",
                    Description = "Administrator username for the pod.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminPassword",
                    Type = "string",
                    Description = "Administrator password for the pod.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the pod automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "cpu",
                    Type = "string",
                    Description = "CPU cores to request for the pod (e.g. '1', '0.5', '2').",
                    Required = false,
                    DefaultValue = "1"
                },
                new()
                {
                    Name = "memory",
                    Type = "string",
                    Description = "Memory to request for the pod (e.g. '4Gi', '8Gi').",
                    Required = false,
                    DefaultValue = "4Gi"
                },
                new()
                {
                    Name = "repo",
                    Type = "string",
                    Description = "Source repository (e.g. 'org/repo'). Stored as metadata on the pod.",
                    Required = false,
                    DefaultValue = null
                },
                new()
                {
                    Name = "project",
                    Type = "string",
                    Description = "AL-Go project name. Stored as metadata on the pod.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "RemovePod",
            Description = "Removes a pod and its database. The full resource name is derived from your GitHub username and the name you provide.",
            Route = "RemovePod",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the pod to remove (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StopPod",
            Description = "Stops a pod by scaling its deployment to 0 replicas.",
            Route = "StopPod",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the pod to stop (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StartPod",
            Description = "Starts a previously stopped pod by scaling its deployment to 1 replica.",
            Route = "StartPod",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the pod to start (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the pod automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "ListPods",
            Description = "Lists pods. By default lists only your own pods. Set 'all' to 'true' to list all pods.",
            Route = "ListPods",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "all",
                    Type = "boolean",
                    Description = "List all pods instead of only your own.",
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
            Name = "ListNodes",
            Description = "Lists Windows nodes in the Kubernetes cluster. Admin only.",
            Route = "ListNodes",
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
