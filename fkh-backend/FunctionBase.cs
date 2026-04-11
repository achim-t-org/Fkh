using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Fkh.Models;
using Fkh.Services;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fkh;

/// <summary>
/// Base class for all Fkh Azure Functions.
/// Handles GitHub token extraction, identity validation, team membership check,
/// and uniform HTTP response shaping so individual functions stay minimal.
/// </summary>
public abstract class FunctionBase
{
    private static readonly List<OrgTeamConfig> AllowedOrgTeams = LoadOrgTeamConfig("ALLOWED_ORG_TEAMS");
    private static readonly List<OrgTeamConfig> AdminOrgTeams = LoadOrgTeamConfig("ADMIN_ORG_TEAMS", required: false);
    private static readonly GitHubOidcService OidcService = new();

    // ── Brute-force protection ───────────────────────────────────────────────────
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan BlockWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, FailedAttemptRecord> FailedAttempts = new();

    private sealed class FailedAttemptRecord
    {
        public int Count;
        public DateTime WindowStart = DateTime.UtcNow;
    }

    private static string GetClientIp(HttpRequestData req)
    {
        // Azure Functions behind a load balancer forwards the real IP in X-Forwarded-For
        if (req.Headers.TryGetValues("X-Forwarded-For", out var xff))
        {
            var first = xff.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                // X-Forwarded-For can be "client, proxy1, proxy2" — take the first
                var ip = first.Split(',')[0].Trim();
                // Strip port if present (e.g. "1.2.3.4:12345")
                var colonIdx = ip.LastIndexOf(':');
                if (colonIdx > 0 && !ip.Contains(']')) // avoid stripping IPv6
                    ip = ip[..colonIdx];
                return ip;
            }
        }
        return req.Url.Host;
    }

    private static bool IsIpBlocked(string ip)
    {
        if (!FailedAttempts.TryGetValue(ip, out var record))
            return false;

        // If the window has expired, reset
        if (DateTime.UtcNow - record.WindowStart > BlockWindow)
        {
            FailedAttempts.TryRemove(ip, out _);
            return false;
        }

        return record.Count >= MaxFailedAttempts;
    }

    private static void RecordFailedAttempt(string ip)
    {
        FailedAttempts.AddOrUpdate(ip,
            _ => new FailedAttemptRecord { Count = 1 },
            (_, existing) =>
            {
                if (DateTime.UtcNow - existing.WindowStart > BlockWindow)
                {
                    // Window expired — start fresh
                    existing.Count = 1;
                    existing.WindowStart = DateTime.UtcNow;
                }
                else
                {
                    Interlocked.Increment(ref existing.Count);
                }
                return existing;
            });
    }

    private static void ClearFailedAttempts(string ip)
    {
        FailedAttempts.TryRemove(ip, out _);
    }

    /// <summary>
    /// Authenticates the caller, authorises via GitHub team membership, then
    /// delegates to <paramref name="aksOperation"/> and returns its result as JSON.
    /// </summary>
    protected async Task<HttpResponseData> ExecuteAsync(
        HttpRequestData req,
        ILogger logger,
        GitHubAuthService gitHub,
        string operationName,
        Func<Dictionary<string, string>, Task<string>> aksOperation)
    {
        var clientIp = GetClientIp(req);

        // ── Step 0: Check IP block list ───────────────────────────────────────────────
        if (IsIpBlocked(clientIp))
        {
            logger.LogWarning("Blocked request from {IP} — too many failed auth attempts.", clientIp);
            return Respond(req, HttpStatusCode.Forbidden, "Too many failed attempts. Try again later.");
        }

        // ── Step 1: Extract Bearer token ─────────────────────────────────────────────
        if (!req.Headers.TryGetValues("Authorization", out var authValues))
        {
            RecordFailedAttempt(clientIp);
            return Respond(req, HttpStatusCode.Unauthorized, "Missing Authorization header.");
        }

        var authHeader = authValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            RecordFailedAttempt(clientIp);
            return Respond(req, HttpStatusCode.Unauthorized, "Authorization header must be a Bearer token.");
        }

        var function = FunctionCatalog.GetRequired(operationName);
        var token = authHeader["Bearer ".Length..].Trim();

        // ── Step 2 & 3: Authenticate and authorize ───────────────────────────────────
        string username;
        var isAdmin = false;

        if (GitHubOidcService.IsOidcToken(token))
        {
            // OIDC path: GitHub Actions workflow token
            var repository = await OidcService.ValidateTokenAsync(token);
            if (repository is null)
            {
                logger.LogWarning("OIDC token validation failed or repository not in allow-list.");
                RecordFailedAttempt(clientIp);
                return Respond(req, HttpStatusCode.Forbidden,
                    "OIDC token invalid or repository not authorized. Check ALLOWED_OIDC_REPOS configuration.");
            }

            username = repository.Replace('/', '_');
            logger.LogInformation("Received {Operation} request from OIDC caller: {Repository} (username: {Username})", operationName, repository, username);
        }
        else
        {
            // User token path: GitHub PAT / user token
            var ghUsername = await gitHub.GetAuthenticatedUsernameAsync(token);
            if (ghUsername is null)
            {
                logger.LogWarning("Invalid or expired GitHub token received.");
                RecordFailedAttempt(clientIp);
                return Respond(req, HttpStatusCode.Unauthorized, "Invalid or expired GitHub token.");
            }

            username = ghUsername;
            logger.LogInformation("Received {Operation} request from GitHub user: {Username}", operationName, username);

            // Check admin teams first
            var authorized = false;
            foreach (var orgTeam in AdminOrgTeams)
            {
                if (await gitHub.IsTeamMemberAsync(token, orgTeam.Org, orgTeam.Team, username))
                {
                    logger.LogInformation(
                        "User {Username} authorized as admin via org={Org} team={Team}",
                        username, orgTeam.Org, orgTeam.Team);
                    authorized = true;
                    isAdmin = true;
                    break;
                }
            }

            // If not admin, check regular member teams
            if (!authorized)
            {
                foreach (var orgTeam in AllowedOrgTeams)
                {
                    if (await gitHub.IsTeamMemberAsync(token, orgTeam.Org, orgTeam.Team, username))
                    {
                        logger.LogInformation(
                            "User {Username} authorized via org={Org} team={Team}",
                            username, orgTeam.Org, orgTeam.Team);
                        authorized = true;
                        break;
                    }
                }
            }

            if (!authorized)
            {
                logger.LogWarning("User {Username} is not a member of any authorized team.", username);
                RecordFailedAttempt(clientIp);
                return Respond(req, HttpStatusCode.Forbidden, "You are not a member of an authorized team.");
            }
        }

        // ── Step 4: Parse and validate request parameters ───────────────────────────
        var parametersResult = await ParseAndValidateParametersAsync(req, function);
        if (!parametersResult.Success)
        {
            return Respond(req, HttpStatusCode.BadRequest, parametersResult.ErrorMessage!);
        }
        // Auth succeeded — clear any prior failed attempts for this IP
        ClearFailedAttempts(clientIp);

        // Inject the authenticated GitHub username so services can use it
        parametersResult.Parameters!["_githubUsername"] = username;
        parametersResult.Parameters!["_isAdmin"] = isAdmin.ToString();

        // Resolve artifact shorthand (e.g. "///us/latest") to a full URL
        if (parametersResult.Parameters.TryGetValue("artifactUrl", out var rawArtifact)
            && !string.IsNullOrWhiteSpace(rawArtifact)
            && !rawArtifact.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var segments = ($"{rawArtifact}/////").Split('/');
                var storageAccount = segments[0];
                var artifactType = string.IsNullOrEmpty(segments[1]) ? "Sandbox" : segments[1];
                var version = segments[2];
                var country = string.IsNullOrEmpty(segments[3]) ? "us" : segments[3];
                var selectStr = string.IsNullOrEmpty(segments[4]) ? "latest" : segments[4];

                var type = Enum.Parse<BcArtifacts.ArtifactType>(artifactType, ignoreCase: true);
                var select = Enum.Parse<BcArtifacts.ArtifactSelect>(selectStr, ignoreCase: true);

                var resolved = (await BcArtifacts.BcArtifactHelper.GetBcArtifactUrlAsync(
                    type: type, country: country, version: version,
                    select: select, storageAccount: storageAccount)).FirstOrDefault();

                if (string.IsNullOrEmpty(resolved))
                    return Respond(req, HttpStatusCode.BadRequest, $"No artifacts found for '{rawArtifact}'.");

                logger.LogInformation("Resolved artifact shorthand '{Raw}' to '{Resolved}'", rawArtifact, resolved);
                parametersResult.Parameters["artifactUrl"] = resolved;
            }
            catch (Exception ex)
            {
                return Respond(req, HttpStatusCode.BadRequest, $"Failed to resolve artifact '{rawArtifact}': {ex.Message}");
            }
        }

        // ── Step 5: Execute AKS operation ────────────────────────────────────────────
        try
        {
            var result = await aksOperation(parametersResult.Parameters!);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = result });
            return response;
        }
        catch (RetryAfterException retryEx)
        {
            logger.LogInformation("Operation {Operation} requested retry in {Seconds}s: {Message}",
                operationName, retryEx.RetryAfterSeconds, retryEx.Message);
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            response.Headers.Add("Retry-After", retryEx.RetryAfterSeconds.ToString());
            await response.WriteAsJsonAsync(new { message = retryEx.Message, retryAfterSeconds = retryEx.RetryAfterSeconds });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute {Operation} for user {Username}.", operationName, username);
            var detail = $"[{ex.GetType().Name}] {ex.Message}";
            if (ex.InnerException is { } inner)
                detail += $"\n  Inner: [{inner.GetType().Name}] {inner.Message}";
            // Include first few frames to identify the call site
            var frames = ex.StackTrace?.Split('\n').Take(5);
            if (frames != null)
                detail += "\n" + string.Join("\n", frames);
            return Respond(req, HttpStatusCode.InternalServerError, detail);
        }
    }

    private static async Task<ParameterValidationResult> ParseAndValidateParametersAsync(
        HttpRequestData req,
        FunctionDefinition function)
    {
        string raw;
        using (var reader = new StreamReader(req.Body))
        {
            raw = await reader.ReadToEndAsync();
        }

        FunctionInvokeRequest invokeRequest;
        if (string.IsNullOrWhiteSpace(raw))
        {
            invokeRequest = new FunctionInvokeRequest
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        else
        {
            try
            {
                invokeRequest = JsonSerializer.Deserialize<FunctionInvokeRequest>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new FunctionInvokeRequest();
            }
            catch
            {
                return ParameterValidationResult.Fail(
                    "Invalid request body. Expected JSON object: { \"parameters\": { \"key\": \"value\" } }");
            }
        }

        var incoming = invokeRequest.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (incoming.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            incoming = new Dictionary<string, string>(incoming, StringComparer.OrdinalIgnoreCase);
        }
        var allowedNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Extract _prefixed internal parameters before validation (e.g. _timezone)
        var internalParams = incoming.Where(kv => kv.Key.StartsWith('_')).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in internalParams.Keys)
            incoming.Remove(key);

        var unknown = incoming.Keys.Where(k => !allowedNames.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            return ParameterValidationResult.Fail(
                $"Unknown parameters for {function.Name}: {string.Join(", ", unknown)}.");
        }

        var validated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in function.Parameters)
        {
            incoming.TryGetValue(parameter.Name, out var value);
            value = string.IsNullOrWhiteSpace(value) ? parameter.DefaultValue : value;

            if (parameter.Required && string.IsNullOrWhiteSpace(value))
            {
                return ParameterValidationResult.Fail(
                    $"Missing required parameter '{parameter.Name}' for {function.Name}.");
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                validated[parameter.Name] = value;
            }
        }

        // Re-add internal parameters (e.g. _timezone) so services can access them
        foreach (var kv in internalParams)
            validated[kv.Key] = kv.Value;

        return ParameterValidationResult.Ok(validated);
    }

    private static HttpResponseData Respond(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        response.WriteString(message);
        return response;
    }

    private static List<OrgTeamConfig> LoadOrgTeamConfig(string envVarName, bool required = true)
    {
        var raw = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required)
                throw new InvalidOperationException(
                    $"{envVarName} app setting is missing. " +
                    "Expected JSON array, e.g. [{\"Org\":\"my-org\",\"Team\":\"Fkh-members\"}]");
            return [];
        }

        return JsonSerializer.Deserialize<List<OrgTeamConfig>>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Failed to parse {envVarName}.");
    }

    private sealed class ParameterValidationResult
    {
        public bool Success { get; init; }
        public Dictionary<string, string>? Parameters { get; init; }
        public string? ErrorMessage { get; init; }

        public static ParameterValidationResult Ok(Dictionary<string, string> parameters)
            => new() { Success = true, Parameters = parameters };

        public static ParameterValidationResult Fail(string message)
            => new() { Success = false, ErrorMessage = message };
    }
}
