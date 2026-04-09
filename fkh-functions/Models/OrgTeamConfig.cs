namespace Fkh.Models;

/// <summary>
/// Represents one org/team pair from configuration.
/// In local.settings.json / App Settings, configure as:
///   "AllowedOrgTeams": "[{\"Org\":\"my-company\",\"Team\":\"FKH-members\"},{\"Org\":\"customer-a\",\"Team\":\"FKH-members\"}]"
/// </summary>
public record OrgTeamConfig(string Org, string Team);
