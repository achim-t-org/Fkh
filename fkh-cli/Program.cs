using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string Help = """
FKH CLI

Usage:
    fkh <command> [--key "value" ...]

Options:
    --key "value"       Provide a parameter value (discovered from GetFunctionCatalog)
    -h, --help          Show help
    --version           Show version

Configuration (checked in order):
  1. FKH_BACKEND_URL environment variable
  2. ~/.fkh/settings.json   (recommended for dotnet tool install)
  3. fkh.settings.json next to the executable

    {
        "backendUrl": "https://fkh-<org>-backend.azurewebsites.net/api"
    }

Authentication:
  Uses 'gh auth token' by default (or GH_TOKEN/GITHUB_TOKEN if set).
""";

try
{
    if (args.Contains("--version"))
    {
        var version = typeof(FunctionCatalogResponse).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(FunctionCatalogResponse).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        Console.WriteLine(version);
        return 0;
    }

    var settings = LoadSettings();
    var wantsHelp = args.Length == 0 || args.Contains("-h") || args.Contains("--help");
    FunctionCatalogResponse catalog;

    try
    {
        catalog = await GetFunctionCatalogAsync(settings.BackendUrl);
    }
    catch
    {
        Console.WriteLine(Help);
        Console.WriteLine();
        Console.WriteLine("Could not fetch function metadata. Configure fkh.settings.json and try again.");
        return 0;
    }

    if (wantsHelp)
    {
        PrintUsage(catalog);
        return 0;
    }

    ParsedArgs parsed;
    FunctionDefinition function;
    try
    {
        parsed = ParseArgs(args, catalog);
        function = catalog.Functions.First(f =>
            string.Equals(f.Name, parsed.Command, StringComparison.OrdinalIgnoreCase));
        EnsureRequiredParameters(function, parsed.Parameters);
    }
    catch (InvalidOperationException ex) when (IsUsageError(ex.Message))
    {
        Console.Error.WriteLine(ex.Message);
        Console.WriteLine();
        PrintUsage(catalog);
        return 1;
    }

    var endpoint = ResolveEndpoint(function.Route, settings);
    var token = GetGitHubToken();

    Console.WriteLine($"Calling {endpoint}");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    using var client = new HttpClient();
    while (true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new FunctionInvokeRequest { Parameters = parsed.Parameters }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        var message = TryGetMessage(body) ?? body;

        if (response.StatusCode == System.Net.HttpStatusCode.Accepted
            && response.Headers.TryGetValues("Retry-After", out var retryValues)
            && int.TryParse(retryValues.FirstOrDefault(), out var retrySeconds)
            && retrySeconds > 0)
        {
            Console.WriteLine(message);
            Console.WriteLine($"Retrying in {retrySeconds} seconds... (Ctrl+C to cancel)");
            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cts.Token);
            continue;
        }

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine(message);
            return 0;
        }

        Console.Error.WriteLine($"Request failed ({(int)response.StatusCode}): {message}");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static ParsedArgs ParseArgs(string[] args, FunctionCatalogResponse catalog)
{
    if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
    {
        return new ParsedArgs { ShowHelp = true };
    }

    var command = args[0].ToLowerInvariant();
    var function = catalog.Functions.FirstOrDefault(f =>
        string.Equals(f.Name, command, StringComparison.OrdinalIgnoreCase));
    if (function is null)
    {
        throw new InvalidOperationException($"Unsupported command: {command}");
    }

    var parsed = new ParsedArgs { Command = function.Name };

    // Build a set of boolean parameter names for this function (flags, no value needed)
    var booleanParams = new HashSet<string>(
        function.Parameters.Where(p => string.Equals(p.Type, "boolean", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name),
        StringComparer.OrdinalIgnoreCase);

    for (var i = 1; i < args.Length; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown argument: {arg}");
        }

        var key = arg[2..];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Parameter name cannot be empty after '--'.");
        }

        if (booleanParams.Contains(key))
        {
            // Boolean flag — presence means true, no value expected
            parsed.Parameters[key] = "true";
        }
        else
        {
            i++;
            if (i >= args.Length)
            {
                throw new InvalidOperationException($"Missing value for --{key}");
            }

            parsed.Parameters[key] = args[i];
        }
    }

    return parsed;
}

static string ResolveEndpoint(string route, CliSettings settings)
{
    var backendUrl = settings.BackendUrl;
    if (string.IsNullOrWhiteSpace(backendUrl))
    {
        throw new InvalidOperationException(
            "No function endpoint configured. Set FKH_BACKEND_URL or create ~/.fkh/settings.json with a backendUrl property.");
    }

    backendUrl = backendUrl.TrimEnd('/');
    return $"{backendUrl}/{route}";
}

static async Task<FunctionCatalogResponse> GetFunctionCatalogAsync(string? backendUrl)
{
    if (string.IsNullOrWhiteSpace(backendUrl))
    {
        throw new InvalidOperationException(
            "No function endpoint configured. Set FKH_BACKEND_URL or create ~/.fkh/settings.json with a backendUrl property.");
    }

    using var client = new HttpClient();
    var functionsUrl = $"{backendUrl.TrimEnd('/')}/functions";
    var response = await client.GetAsync(functionsUrl);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Failed to fetch functions ({(int)response.StatusCode}): {body}");
    }

    var catalog = JsonSerializer.Deserialize<FunctionCatalogResponse>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (catalog is null || catalog.Functions.Count == 0)
    {
        throw new InvalidOperationException("Function catalog is empty or invalid.");
    }

    return catalog;
}

static void EnsureRequiredParameters(FunctionDefinition function, Dictionary<string, string> parameters)
{
    var knownNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unknown = parameters.Keys.Where(k => !knownNames.Contains(k)).ToList();
    if (unknown.Count > 0)
    {
        throw new InvalidOperationException(
            $"Unknown parameters for {function.Name}: {string.Join(", ", unknown)}");
    }

    foreach (var parameter in function.Parameters)
    {
        if (!parameters.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            if (parameter.Required)
            {
                // Auto-detect public IP for parameters named 'ip'
                string? detectedDefault = null;
                if (string.Equals(parameter.Name, "ip", StringComparison.OrdinalIgnoreCase))
                {
                    detectedDefault = DetectPublicIp();
                }

                var prompt = $"Enter {parameter.Name}";
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    prompt += $" ({parameter.Description})";
                }
                if (detectedDefault is not null)
                {
                    prompt += $" [{detectedDefault}]";
                }
                prompt += ": ";

                var secret = parameter.Name.Contains("password", StringComparison.OrdinalIgnoreCase);
                value = ReadValueWithDefault(prompt, secret, detectedDefault);
            }
            else if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                value = parameter.DefaultValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[parameter.Name] = value;
        }
    }
}

static string ReadValueWithDefault(string prompt, bool secret, string? defaultValue)
{
    while (true)
    {
        Console.Write(prompt);
        string? value;
        if (secret)
        {
            value = ReadSecret();
            Console.WriteLine();
        }
        else
        {
            value = Console.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(value) && defaultValue is not null)
        {
            return defaultValue;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        Console.WriteLine("Value is required.");
    }
}

static string? DetectPublicIp()
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = client.GetStringAsync("https://api.ipify.org?format=text").GetAwaiter().GetResult();
        var ip = response.Trim();
        return string.IsNullOrWhiteSpace(ip) ? null : ip;
    }
    catch
    {
        return null;
    }
}

static bool IsUsageError(string message)
{
    return message.StartsWith("Unsupported command:", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Unknown argument:", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Missing value for --", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Parameter name cannot be empty", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Unknown parameters for", StringComparison.OrdinalIgnoreCase);
}

static string ReadSecret()
{
    var builder = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (builder.Length > 0)
            {
                builder.Length--;
            }
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            builder.Append(key.KeyChar);
        }
    }

    return builder.ToString();
}

static void PrintUsage(FunctionCatalogResponse catalog)
{
    Console.WriteLine(Help);
    Console.WriteLine();
    Console.WriteLine("Available commands:");

    foreach (var function in catalog.Functions)
    {
        Console.WriteLine($"  {function.Name.ToLowerInvariant()}");
        Console.WriteLine($"    {function.Description}");

        foreach (var parameter in function.Parameters)
        {
            var requiredText = parameter.Required ? "required" : "optional";
            var defaultText = string.IsNullOrWhiteSpace(parameter.DefaultValue)
                ? string.Empty
                : $", default='{parameter.DefaultValue}'";

            Console.WriteLine(
                $"    --{parameter.Name} <{parameter.Type}> [{requiredText}{defaultText}] - {parameter.Description}");
        }
    }
}

static string GetGitHubToken()
{
    var token = Environment.GetEnvironmentVariable("GH_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
    {
        return token;
    }

    token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
    {
        return token;
    }

    var psi = new ProcessStartInfo
    {
        FileName = "gh",
        ArgumentList = { "auth", "token" },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 'gh'.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            "Could not get GitHub token from 'gh auth token'. Run 'gh auth login' first. " +
            (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $"Details: {stderr.Trim()}"));
    }

    token = stdout.Trim();
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("'gh auth token' returned an empty token.");
    }

    return token;
}

static string? TryGetMessage(string responseBody)
{
    if (string.IsNullOrWhiteSpace(responseBody))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
            messageElement.ValueKind == JsonValueKind.String)
        {
            return messageElement.GetString();
        }
    }
    catch
    {
        // Plain text response.
    }

    return null;
}

static CliSettings LoadSettings()
{
    // 1. Environment variable takes priority
    var envUrl = Environment.GetEnvironmentVariable("FKH_BACKEND_URL");
    if (!string.IsNullOrWhiteSpace(envUrl))
    {
        return new CliSettings { BackendUrl = envUrl };
    }

    // 2. User profile settings (~/.fkh/settings.json) — works for dotnet tool installs
    var userSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".fkh", "settings.json");
    var loaded = TryLoadSettingsFile(userSettingsPath);
    if (loaded is not null) return loaded;

    // 3. Settings file next to the executable
    var localSettingsPath = Path.Combine(AppContext.BaseDirectory, "fkh.settings.json");
    return TryLoadSettingsFile(localSettingsPath) ?? new CliSettings();
}

static CliSettings? TryLoadSettingsFile(string path)
{
    if (!File.Exists(path)) return null;
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<CliSettings>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
}

sealed class ParsedArgs
{
    public bool ShowHelp { get; init; }
    public string? Command { get; init; }
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class CliSettings
{
    public string? BackendUrl { get; init; }
}

sealed class FunctionCatalogResponse
{
    [JsonPropertyName("functions")]
    public List<FunctionDefinition> Functions { get; init; } = new();
}

sealed class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<FunctionParameterDefinition> Parameters { get; init; } = new();
}

sealed class FunctionParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; init; }
}

sealed class FunctionInvokeRequest
{
    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}