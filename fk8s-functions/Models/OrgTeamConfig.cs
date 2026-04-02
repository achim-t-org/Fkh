namespace FK8s.Models;

/// <summary>
/// Represents one org/team pair from configuration.
/// In local.settings.json / App Settings, configure as:
///   "AllowedOrgTeams": "[{\"Org\":\"my-company\",\"Team\":\"FK8s-members\"},{\"Org\":\"customer-a\",\"Team\":\"FK8s-members\"}]"
/// </summary>
public record OrgTeamConfig(string Org, string Team);
