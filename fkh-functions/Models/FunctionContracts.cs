namespace Fkh.Models;

public sealed class FunctionParameterDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public bool Required { get; init; }
    public string? DefaultValue { get; init; }
}

public sealed class FunctionDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Route { get; init; }
    public required List<FunctionParameterDefinition> Parameters { get; init; }
}

public sealed class FunctionCatalogResponse
{
    public required List<FunctionDefinition> Functions { get; init; }
}

public sealed class FunctionInvokeRequest
{
    public Dictionary<string, string>? Parameters { get; init; }
}

/// <summary>
/// Throw from a service operation to signal the caller should retry after a delay.
/// FunctionBase catches this and returns HTTP 202 with a Retry-After header.
/// </summary>
public sealed class RetryAfterException : Exception
{
    public int RetryAfterSeconds { get; }

    public RetryAfterException(string message, int retryAfterSeconds)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
