# Installing Fkh

This guide walks you through installing Fkh step by step using the GitHub Actions workflows in your private deployment repository. No local tools are required — everything runs in the cloud.

Before you begin, make sure you understand the roles involved and have the right people available.

## Roles

An Fkh installation touches three systems — an **Azure subscription**, a **GitHub organization**, and **Microsoft Entra ID** (Azure AD). Depending on your organization, one person may cover all three roles, or you may need to coordinate between several people.

### Azure Subscription Owner

This person owns (or has **Owner** role on) the Azure subscription where Fkh will be deployed.

They are responsible for:

- Creating or providing the target Azure subscription.
- Assigning the **Contributor** and **User Access Administrator** roles to the deployment identity (App Registration or Managed Identity) that the GitHub workflows will use.
- Approving any Azure policy exemptions if the subscription is governed by organizational policies.

> **Why Owner?** Assigning privileged roles like *Contributor* and *User Access Administrator* on a subscription requires the **Owner** role. A lesser role is not sufficient.

### GitHub Organization Administrator

This person must be able to create repositories in the GitHub organization.

They are responsible for:

- Creating a **private deployment repository** that holds your organization-specific configuration, secrets, and caller workflows. This is all you need — forking the Fkh repository is **not required**. The deployment workflows reference the public Fkh repository directly.
- Creating a **GitHub App** in the organization (requires organization owner access).
- Installing the GitHub App on the deployment repository.
- Configuring **GitHub Secrets and Variables** in the deployment repository settings.

> **Why a separate deployment repo?** The deployment repository is private because it contains your organization-specific configuration (Azure subscription IDs, GitHub org/team names) and is where GitHub Secrets are stored. The deployment workflows call reusable workflows in the public Freddy-DK/Fkh repository — no fork needed. The GitHub App is used by the backend to dispatch image-build workflows and by the deployment workflow to sync secrets.

> **Optional: fork Fkh.** If you want to contribute to Fkh (bug fixes, new features, etc.), you can fork the Fkh repository into your organization and point the deployment workflows at your fork instead. This lets you test changes before submitting pull requests back to the upstream repository.

### Entra ID Privileged Role Administrator

This person must be able to grant Microsoft Graph application permissions to a Managed Identity (or App Registration).

They are responsible for:

- Granting the deployment identity the **Privileged Role Administrator** directory role in Entra ID so that Terraform can assign the `Application.ReadWrite.OwnedBy` Graph permission to the Function's managed identity during deployment.

> **Why this role?** When a user requests AAD authentication on a container, the Fkh backend automatically creates a dedicated Entra ID App Registration for that container. The `Application.ReadWrite.OwnedBy` Graph permission on the Function's managed identity is what makes this possible — it allows the backend to create and manage only the app registrations it owns. The deployment identity needs the directory role so Terraform can grant that Graph permission during infrastructure provisioning.

> **Who can do this?** Typically someone with the **Privileged Role Administrator**, **Global Administrator**, or **Application Administrator** role in your Entra ID tenant. The exact role depends on your organization's Entra ID configuration.

### Role summary

| Role | System | Minimum permission required |
|------|--------|-----------------------------|
| Azure Subscription Owner | Azure | **Owner** on the target subscription |
| GitHub Organization Admin | GitHub | Ability to create repos, GitHub Apps, and PATs in the org |
| Entra ID Privileged Role Admin | Microsoft Entra ID | **Privileged Role Administrator** directory role (to grant `Application.ReadWrite.OwnedBy` to the Function identity) |

> **Tip:** In smaller organizations one person often fills all three roles. In larger enterprises these are typically three different people — plan for a short coordination meeting before you start.

---

*Next step: [Step 1 — Create the Private Deployment Repository](Step1-DeploymentRepo.md)*
