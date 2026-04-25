# Step 4 — Set Up GitHub Teams for Authorization

> **Performed by:** 🔵 GitHub Organization Admin

> **Already have teams from another Fkh deployment?** You can reuse the same GitHub teams across multiple deployments — just reference the existing team names in your new deployment's `deployment.tfvars` (step 4.4). You do not need to create new teams. Skip ahead to [step 4.4](#44--update-deploymenttfvars).

Fkh uses GitHub team membership to control who can provision containers and who gets admin access. In this step you create the teams manually in your GitHub organization and add members.

Terraform does **not** create or manage these teams — it only reads the team names from `deployment.tfvars` and passes them to the Azure Function backend for authorization checks.

---

## 4.1 Create the member team

1. Go to **https://github.com/orgs/YOUR-ORG/new-team**
2. **Team name:** `Fkh-members` (or any name you prefer)
3. **Visibility:** Visible or Secret — your choice
4. Click **Create team**
5. Add members who should be able to provision BC containers

## 4.2 Create the admin team (optional)

If you want a separate admin tier with elevated privileges:

1. Go to **https://github.com/orgs/YOUR-ORG/new-team**
2. **Team name:** `Fkh-admins` (or any name you prefer)
3. Click **Create team**
4. Add members who should have admin access

Admin team members automatically get normal access too — you don't need to add them to both teams.

## 4.3 Cross-organization access (optional)

You can grant access to teams in **any** GitHub organization — the team names don't need to match. Just add each org/team pair to `allowed_org_teams` in your `deployment.tfvars`:

```hcl
allowed_org_teams = [
  { org = "my-company",   team = "Fkh-members" },
  { org = "partner-org",  team = "BC-developers" }
]
```

The admin of each organization is responsible for creating the team and managing its members in their own org.

## 4.4 Update deployment.tfvars

Open `config/deployment.tfvars` in your deployment repo and set the team references to match the teams you just created:

```hcl
# Member teams — users in these teams can provision containers
allowed_org_teams = [
  { org = "my-company", team = "Fkh-members" }
]

# Admin teams — members get admin access (and also have normal access)
admin_org_teams = [
  { org = "my-company", team = "Fkh-admins" }
]
```

> **Important:** The `org` and `team` values in `deployment.tfvars` are **case-sensitive** and must match the organization and team names in GitHub **exactly**.

---

*Previous: [Step 3 — Create the GitHub App](Step3-GitHubApp.md)*
*Next: [Step 5 — Configure Your Environment](Step5-ConfigureEnvironment.md)*
