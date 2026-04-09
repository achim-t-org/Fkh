<#
.SYNOPSIS
    Pre-apply check: imports existing GitHub teams into Terraform state if needed.

.DESCRIPTION
    Run this before 'terraform apply' when the GitHub teams may already exist.

    Checks both the provisioners team (github_team_name) and the admins team
    (github_admin_team_name).

    Behaviour per team:
      - Team does NOT exist in GitHub  → does nothing; apply will create it.
      - Team exists in GitHub but NOT in Terraform state → imports team (and
        any listed members) so apply manages them without a conflict error.
      - Team already in Terraform state → does nothing; apply will diff/update.

.PARAMETER VarFile
    Path to the organization .tfvars file, e.g. organizations/my-org.tfvars

.PARAMETER GithubToken
    GitHub personal access token with read:org scope.
    Defaults to the TF_VAR_github_token environment variable.

.EXAMPLE
    .\checkGitHubTeam.ps1 -VarFile organizations/my-org.tfvars
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $VarFile,

    [Parameter(Mandatory = $false)]
    [string] $GithubToken = $env:TF_VAR_github_token
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GithubToken)) {
    throw "GitHub token is required. Pass -GithubToken or set TF_VAR_github_token."
}

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-TfVar([string] $file, [string] $key) {
    $line = Get-Content $file | Where-Object { $_ -match "^\s*$key\s*=" } | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -replace "^\s*$key\s*=\s*`"?([^`"#]+)`"?.*", '$1').Trim()
}

$headers = @{
    Authorization = "Bearer $GithubToken"
    Accept        = "application/vnd.github+json"
}

function Import-GitHubTeamIfNeeded {
    param(
        [string] $GithubOrg,
        [string] $TeamName,
        [string] $TfTeamResource,       # e.g. "github_team.provisioners"
        [string] $TfMembershipResource,  # e.g. "github_team_membership.members"
        [string] $TfVarsMembersKey       # e.g. "github_team_members"
    )

    $teamSlug = $TeamName.ToLower() -replace '[^a-z0-9]', '-'
    Write-Host "Checking GitHub team '$teamSlug' in org '$GithubOrg'..." -ForegroundColor Cyan

    # ── Check if team exists via GitHub API ───────────────────────────────────
    try {
        $team = Invoke-RestMethod `
            -Uri "https://api.github.com/orgs/$GithubOrg/teams/$teamSlug" `
            -Headers $headers `
            -Method Get `
            -ErrorAction Stop
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "  Team does not exist. 'terraform apply' will create it." -ForegroundColor Green
            return
        }
        throw
    }

    Write-Host "  Team exists (id: $($team.id)). Checking Terraform state..." -ForegroundColor Yellow

    # ── Check if team is already in Terraform state ───────────────────────────
    $stateList = terraform state list 2>&1
    $teamInState = $stateList | Where-Object { $_ -eq $TfTeamResource }

    if ($teamInState) {
        Write-Host "  Team already in Terraform state. No import needed." -ForegroundColor Green
    } else {
        Write-Host "  Importing $TfTeamResource (id: $($team.id))..." -ForegroundColor Yellow
        terraform import "-var-file=$VarFile" $TfTeamResource $team.id
        if ($LASTEXITCODE -ne 0) { throw "Failed to import GitHub team '$TeamName'." }
        Write-Host "  Team imported." -ForegroundColor Green
    }

    # ── Import any team memberships that already exist ────────────────────────
    $stateMembers = terraform show -json 2>$null | ConvertFrom-Json |
        Select-Object -ExpandProperty values -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty root_module -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty resources -ErrorAction SilentlyContinue |
        Where-Object { $_.address -like "$TfMembershipResource*" } |
        ForEach-Object { $_.values.username }

    # Parse member list from tfvars
    $raw = Get-Content $VarFile -Raw
    if ($raw -match "(?s)$TfVarsMembersKey\s*=\s*\[([^\]]+)\]") {
        $memberBlock = $Matches[1]
        $tfvarsMembers = [regex]::Matches($memberBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    } else {
        $tfvarsMembers = @()
    }

    foreach ($username in $tfvarsMembers) {
        $addr = "$TfMembershipResource[`"$username`"]"
        $importId = "$($team.id):$username"

        if ($stateMembers -contains $username) {
            Write-Host "  Membership '$username' already in state. Skipping." -ForegroundColor Green
            continue
        }

        # Check if user is actually a member of the team in GitHub
        try {
            Invoke-RestMethod `
                -Uri "https://api.github.com/orgs/$GithubOrg/teams/$teamSlug/members/$username" `
                -Headers $headers `
                -Method Get `
                -ErrorAction Stop | Out-Null

            Write-Host "  Importing membership: $username..." -ForegroundColor Yellow
            terraform import "-var-file=$VarFile" $addr $importId
            if ($LASTEXITCODE -ne 0) { throw "Failed to import membership for '$username'." }
            Write-Host "  Imported membership: $username." -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Response.StatusCode -eq 404) {
                Write-Host "  '$username' is not yet a member. Apply will add them." -ForegroundColor Gray
            } else {
                throw
            }
        }
    }
}

# ── Parse org from tfvars ─────────────────────────────────────────────────────

$githubOrg = Get-TfVar $VarFile "github_org"
if (-not $githubOrg) { throw "github_org not found in $VarFile" }

# ── Check provisioners team ──────────────────────────────────────────────────

$githubTeamName = Get-TfVar $VarFile "github_team_name"
if (-not $githubTeamName) { $githubTeamName = "Fkh-members" }

Import-GitHubTeamIfNeeded `
    -GithubOrg $githubOrg `
    -TeamName $githubTeamName `
    -TfTeamResource "github_team.provisioners" `
    -TfMembershipResource "github_team_membership.members" `
    -TfVarsMembersKey "github_team_members"

# ── Check admins team ────────────────────────────────────────────────────────

$githubAdminTeamName = Get-TfVar $VarFile "github_admin_team_name"
if (-not $githubAdminTeamName) { $githubAdminTeamName = "Fkh-admins" }

Import-GitHubTeamIfNeeded `
    -GithubOrg $githubOrg `
    -TeamName $githubAdminTeamName `
    -TfTeamResource "github_team.admins" `
    -TfMembershipResource "github_team_membership.admin_members" `
    -TfVarsMembersKey "github_admin_team_members"

Write-Host ""
Write-Host "GitHub team check complete. Run 'terraform apply -var-file=$VarFile' to proceed." -ForegroundColor Cyan
