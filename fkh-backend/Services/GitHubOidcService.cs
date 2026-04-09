using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fkh.Services;

/// <summary>
/// Validates GitHub Actions OIDC tokens and extracts the repository identity.
/// </summary>
public class GitHubOidcService
{
    private const string GitHubOidcIssuer = "https://token.actions.githubusercontent.com";

    private static readonly List<string> AllowedOidcRepos = LoadAllowedRepos();
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> ConfigManager = new(
        $"{GitHubOidcIssuer}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever());

    /// <summary>
    /// Returns true if the token looks like a JWT (OIDC), false if it looks like a GitHub PAT/user token.
    /// </summary>
    public static bool IsOidcToken(string token) => token.StartsWith("eyJ", StringComparison.Ordinal);

    /// <summary>
    /// Validates a GitHub Actions OIDC token and returns the repository claim (e.g. "Freddy-DK/MyRepo").
    /// Returns null if validation fails or the repo is not in the allow-list.
    /// </summary>
    public async Task<string?> ValidateTokenAsync(string token)
    {
        if (AllowedOidcRepos.Count == 0)
            return null;

        var config = await ConfigManager.GetConfigurationAsync(CancellationToken.None);

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = GitHubOidcIssuer,
            IssuerSigningKeys = config.SigningKeys,
            ValidateAudience = false,  // audience is caller-defined; we validate repo instead
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var handler = new JwtSecurityTokenHandler();
        try
        {
            handler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwt = (JwtSecurityToken)validatedToken;

            var repository = jwt.Claims.FirstOrDefault(c => c.Type == "repository")?.Value;
            if (string.IsNullOrEmpty(repository))
                return null;

            // Case-insensitive match: GitHub repo names are case-insensitive
            var isAllowed = AllowedOidcRepos.Any(r =>
                string.Equals(r, repository, StringComparison.OrdinalIgnoreCase));

            return isAllowed ? repository : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private static List<string> LoadAllowedRepos()
    {
        var raw = Environment.GetEnvironmentVariable("ALLOWED_OIDC_REPOS");
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
    }
}
