using System.Net.Http.Headers;
using System.Text.Json;
using FK8s.Models;

namespace FK8s.Services;

public class GitHubAuthService
{
    private readonly HttpClient _httpClient;

    // Reuse a single HttpClient across invocations (best practice in Azure Functions)
    public GitHubAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.github.com");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AksNodeProvisioner", "1.0"));
    }

    /// <summary>
    /// Validates the GitHub token and returns the authenticated username.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    public async Task<string?> GetAuthenticatedUsernameAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<GitHubUser>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return user?.Login;
    }

    /// <summary>
    /// Checks whether the given GitHub user is an active member of the specified team in the given org.
    /// Uses the user's own token — they can check their own membership with read:org scope.
    /// Returns true only on HTTP 200 with state == "active". All other responses (403, 404, etc.) return false.
    /// </summary>
    public async Task<bool> IsTeamMemberAsync(string token, string org, string team, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/orgs/{org}/teams/{team}/memberships/{username}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return false;

        var content = await response.Content.ReadAsStringAsync();
        var membership = JsonSerializer.Deserialize<GitHubTeamMembership>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Must be explicitly "active" — pending invitations don't count
        return membership?.State == "active";
    }
}
