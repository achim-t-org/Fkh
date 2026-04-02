using System.Net;
using System.Text.Json;
using FK8s.Models;
using FK8s.Services;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FK8s;

/// <summary>
/// Base class for all FK8s Azure Functions.
/// Handles GitHub token extraction, identity validation, team membership check,
/// and uniform HTTP response shaping so individual functions stay minimal.
/// </summary>
public abstract class FunctionBase
{
    private static readonly List<OrgTeamConfig> AllowedOrgTeams = LoadOrgTeamConfig();

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
        var function = FunctionCatalog.GetRequired(operationName);

        // ── Step 1: Extract Bearer token ─────────────────────────────────────────────
        if (!req.Headers.TryGetValues("Authorization", out var authValues))
            return Respond(req, HttpStatusCode.Unauthorized, "Missing Authorization header.");

        var authHeader = authValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return Respond(req, HttpStatusCode.Unauthorized, "Authorization header must be a Bearer token.");

        var token = authHeader["Bearer ".Length..].Trim();

        // ── Step 2: Validate GitHub identity ─────────────────────────────────────────
        var username = await gitHub.GetAuthenticatedUsernameAsync(token);
        if (username is null)
        {
            logger.LogWarning("Invalid or expired GitHub token received.");
            return Respond(req, HttpStatusCode.Unauthorized, "Invalid or expired GitHub token.");
        }

        logger.LogInformation("Received {Operation} request from GitHub user: {Username}", operationName, username);

        // ── Step 3: Check team membership ────────────────────────────────────────────
        var authorized = false;
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

        if (!authorized)
        {
            logger.LogWarning("User {Username} is not a member of any authorized team.", username);
            return Respond(req, HttpStatusCode.Forbidden, "You are not a member of an authorized team.");
        }

        // ── Step 4: Parse and validate request parameters ───────────────────────────
        var parametersResult = await ParseAndValidateParametersAsync(req, function);
        if (!parametersResult.Success)
        {
            return Respond(req, HttpStatusCode.BadRequest, parametersResult.ErrorMessage!);
        }

        // ── Step 5: Execute AKS operation ────────────────────────────────────────────
        try
        {
            var result = await aksOperation(parametersResult.Parameters!);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = result });
            return response;
        }
        catch (NotImplementedException ex)
        {
            logger.LogError(ex, "AKS operation not yet implemented.");
            return Respond(req, HttpStatusCode.InternalServerError, "AKS operation is not yet implemented.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute {Operation} for user {Username}.", operationName, username);
            return Respond(req, HttpStatusCode.InternalServerError, "An error occurred while executing the operation.");
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
        var allowedNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        return ParameterValidationResult.Ok(validated);
    }

    private static HttpResponseData Respond(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        response.WriteString(message);
        return response;
    }

    private static List<OrgTeamConfig> LoadOrgTeamConfig()
    {
        var raw = Environment.GetEnvironmentVariable("ALLOWED_ORG_TEAMS");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "ALLOWED_ORG_TEAMS app setting is missing. " +
                "Expected JSON array, e.g. [{\"Org\":\"my-org\",\"Team\":\"FK8s-members\"}]");

        return JsonSerializer.Deserialize<List<OrgTeamConfig>>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse ALLOWED_ORG_TEAMS.");
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
