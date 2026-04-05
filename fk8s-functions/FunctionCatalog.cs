using FK8s.Models;

namespace FK8s;

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
