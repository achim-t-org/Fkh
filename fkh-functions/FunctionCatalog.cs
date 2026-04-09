using FKH.Models;

namespace FKH;

public static class FunctionCatalog
{
    public static List<FunctionDefinition> Functions { get; } = new()
    {
        new FunctionDefinition
        {
            Name = "CreateNode",
            Description = "Creates a node using the provided artifact and admin credentials.",
            Route = "CreateNode",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name for the node. Combined with the GitHub username to form the container name.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "artifactUrl",
                    Type = "string",
                    Description = "Artifact URL used by the node provisioning workflow.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminUsername",
                    Type = "string",
                    Description = "Administrator username for the node/workload.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "adminPassword",
                    Type = "string",
                    Description = "Administrator password for the node/workload.",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the node automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "RemoveNode",
            Description = "Removes a node and its database. The full resource name is derived from your GitHub username and the name you provide.",
            Route = "RemoveNode",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the node to remove (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StopNode",
            Description = "Stops a node by scaling its deployment to 0 replicas.",
            Route = "StopNode",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the node to stop (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "StartNode",
            Description = "Starts a previously stopped node by scaling its deployment to 1 replica.",
            Route = "StartNode",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    Description = "Name of the node to start (same name used when creating it).",
                    Required = true,
                    DefaultValue = null
                },
                new()
                {
                    Name = "autostop",
                    Type = "string",
                    Description = "Hours after which the node automatically stops (e.g. '2' for 2 hours). Leave empty for no auto-stop.",
                    Required = false,
                    DefaultValue = null
                }
            }
        },
        new FunctionDefinition
        {
            Name = "ListNodes",
            Description = "Lists nodes. By default lists only your own nodes. Set 'all' to 'true' to list all nodes.",
            Route = "ListNodes",
            Parameters = new List<FunctionParameterDefinition>
            {
                new()
                {
                    Name = "all",
                    Type = "boolean",
                    Description = "List all nodes instead of only your own.",
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
