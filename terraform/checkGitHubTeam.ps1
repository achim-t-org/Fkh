<#
.SYNOPSIS
    Pre-apply check: imports an existing GitHub team into Terraform state if needed.

.DESCRIPTION
    Run this before 'terraform apply' when the GitHub team may already exist.

    Behaviour:
      - Team does NOT exist in GitHub  → does nothing; apply will create it.
      - Team exists in GitHub but NOT in Terraform state → imports team (and
        any listed members) so apply manages them without a conflict error.
      - Team already in Terraform state → does nothing; apply will diff/update.

.PARAMETER VarFile
    Path to the customer .tfvars file, e.g. customers/customer-a.tfvars

.PARAMETER GithubToken
    GitHub personal access token with read:org scope.
    Defaults to the TF_VAR_github_token environment variable.

.EXAMPLE
    .\checkGitHubTeam.ps1 -VarFile customers/customer-a.tfvars
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

# ── Parse org and team name from the tfvars file ──────────────────────────────

function Get-TfVar([string] $file, [string] $key) {
    $line = Get-Content $file | Where-Object { $_ -match "^\s*$key\s*=" } | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -replace "^\s*$key\s*=\s*`"?([^`"#]+)`"?.*", '$1').Trim()
}

$githubOrg      = Get-TfVar $VarFile "github_org"
$githubTeamName = Get-TfVar $VarFile "github_team_name"

if (-not $githubOrg)      { throw "github_org not found in $VarFile" }
if (-not $githubTeamName) { throw "github_team_name not found in $VarFile" }

$teamSlug = $githubTeamName.ToLower() -replace '[^a-z0-9]', '-'

Write-Host "Checking GitHub team '$teamSlug' in org '$githubOrg'..." -ForegroundColor Cyan

# ── Check if team exists via GitHub API ───────────────────────────────────────

$headers = @{
    Authorization = "Bearer $GithubToken"
    Accept        = "application/vnd.github+json"
}

try {
    $team = Invoke-RestMethod `
        -Uri "https://api.github.com/orgs/$githubOrg/teams/$teamSlug" `
        -Headers $headers `
        -Method Get `
        -ErrorAction Stop
}
catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        Write-Host "  Team does not exist. 'terraform apply' will create it." -ForegroundColor Green
        exit 0
    }
    throw
}

Write-Host "  Team exists (id: $($team.id)). Checking Terraform state..." -ForegroundColor Yellow

# ── Check if team is already in Terraform state ───────────────────────────────

$stateList = terraform state list 2>&1
$teamInState = $stateList | Where-Object { $_ -eq "github_team.provisioners" }

if ($teamInState) {
    Write-Host "  Team already in Terraform state. No import needed." -ForegroundColor Green
} else {
    Write-Host "  Importing github_team.provisioners (id: $($team.id))..." -ForegroundColor Yellow
    terraform import "-var-file=$VarFile" github_team.provisioners $team.id
    if ($LASTEXITCODE -ne 0) { throw "Failed to import GitHub team." }
    Write-Host "  Team imported." -ForegroundColor Green
}

# ── Import any team memberships that already exist ────────────────────────────

$members = terraform show -json 2>$null | ConvertFrom-Json |
    Select-Object -ExpandProperty values -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty root_module -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty resources -ErrorAction SilentlyContinue |
    Where-Object { $_.address -like "github_team_membership.members*" } |
    ForEach-Object { $_.values.username }

# Parse member list from tfvars (rough parse for quoted strings inside the list)
$raw = Get-Content $VarFile -Raw
if ($raw -match '(?s)github_team_members\s*=\s*\[([^\]]+)\]') {
    $memberBlock = $Matches[1]
    $tfvarsMembers = [regex]::Matches($memberBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
} else {
    $tfvarsMembers = @()
}

foreach ($username in $tfvarsMembers) {
    $addr = "github_team_membership.members[`"$username`"]"
    $importId = "$($team.id):$username"

    if ($members -contains $username) {
        Write-Host "  Membership '$username' already in state. Skipping." -ForegroundColor Green
        continue
    }

    # Check if user is actually a member of the team in GitHub
    try {
        Invoke-RestMethod `
            -Uri "https://api.github.com/orgs/$githubOrg/teams/$teamSlug/members/$username" `
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

Write-Host ""
Write-Host "GitHub team check complete. Run 'terraform apply -var-file=$VarFile' to proceed." -ForegroundColor Cyan
