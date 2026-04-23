# Step 2 — Create the Azure Deployment Identity

The deployment identity is what GitHub Actions uses to deploy and manage all Fkh infrastructure in your Azure subscription. It authenticates using OIDC (OpenID Connect) — no passwords or client secrets are involved.

You have two options for the identity type. Both result in a **Client ID** that you will store as a GitHub secret later. The GitHub workflows do not care which type you chose — they work identically.

## Choosing between Managed Identity and App Registration

| | Managed Identity | App Registration |
|---|---|---|
| **Lives in** | An Azure resource group (Azure Resource Manager) | Microsoft Entra ID (Azure AD) |
| **Created by** | Azure Subscription Owner | Entra ID Administrator |
| **Entra ID admin required?** | **No** — unless you want AAD authentication for containers (see below) | **Yes** — from the start |
| **Best when** | Your org restricts App Registration creation, or you want to keep everything inside Azure RM | You already use App Registrations, or your org prefers this pattern |
| **Cleanup** | Delete the resource group | Delete the App Registration in Entra ID |

### A note on AAD authentication for containers

Fkh can optionally create per-container Entra ID App Registrations so users sign in with their Microsoft 365 accounts instead of username/password. This feature is **disabled by default** and controlled by the `enable_aad_container_auth` setting in your `.tfvars` configuration file:

```hcl
enable_aad_container_auth = true   # set to true to enable AAD authentication for containers
```

When enabled, Terraform grants the `Application.ReadWrite.OwnedBy` Microsoft Graph permission to the **Function's managed identity** — a separate identity that Terraform creates automatically during deployment, not the deployment identity you are setting up now.

To grant this Graph permission, the **deployment identity** must itself have the **Privileged Role Administrator** directory role in Entra ID. Without it, the Terraform resource will fail.

**If you leave `enable_aad_container_auth = false`** (the default), the Graph permission is not assigned, no Entra ID directory role is needed on the deployment identity, and the deployment will succeed without any Entra ID admin involvement. You can always enable it later by setting the variable to `true`, having the Entra ID admin grant the directory role (see steps A.5 / B.4 below), and re-running the deployment.

**If you set `enable_aad_container_auth = true`**, you must also complete the optional Entra ID directory role step (A.5 or B.4) before running the deployment workflow.

---

## Option A — Managed Identity

> **Who does this:** Azure Subscription Owner

This option keeps everything inside Azure Resource Manager. No Entra ID admin involvement is needed.

### A.1 — Create a resource group for the deployment identity

The Managed Identity needs a resource group to live in. This is **separate** from the resource group Terraform will create for the Fkh workload.

1. Go to **Azure Portal** → search for **Resource groups** → **Create**.
2. Select your target subscription.
3. Enter a name, e.g. `fkh-deploy`.
4. Choose any region (it does not have to match where Fkh will be deployed).
5. Click **Review + create** → **Create**.

### A.2 — Create the Managed Identity

1. Go to **Azure Portal** → search for **Managed Identities** → **Create**.
2. Select your subscription.
3. Select the resource group you just created (`fkh-deploy`).
4. Choose the same region.
5. Enter a name, e.g. `fkh-deploy-identity`.
6. Click **Review + create** → **Create**.
7. Once created, open the identity and note the **Client ID** from the **Overview** page.

### A.3 — Add a federated credential for GitHub Actions

This allows GitHub Actions to authenticate as this identity using OIDC — no secrets needed.

1. In the Managed Identity, go to **Settings** → **Federated credentials** → **Add credential**.
2. For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
3. Fill in:
   - **Organization**: your GitHub organization name (e.g. `my-company`)
   - **Repository**: the name of your **private deployment repository** from Step 1 (e.g. `fkh-deploy`) — **not** `Fkh`
   - **Entity type**: Branch
   - **Based on selection**: `main`
   - **Name**: `fkh-main-branch`
4. The **Issuer**, **Subject identifier**, and **Audience** fields are auto-populated. Click **Add**.

> **Important:** The repository must be your private deployment repo, not the Fkh fork. The deployment workflows run from the deployment repo, so the OIDC token issued by GitHub Actions will carry that repo's name in the subject claim.

### A.4 — Assign roles on the subscription

The deployment identity needs two roles on the target subscription so Terraform can create and manage all infrastructure.

1. Go to **Subscriptions** → select your subscription → **Access control (IAM)** → **Add** → **Add role assignment**.
2. Select the **Privileged administrator roles** tab.
3. Select **Contributor** → click **Next**.
4. Click **Select members** → search for `fkh-deploy-identity` → select it → click **Select**.
5. Click **Review + assign**.
6. Repeat the above for **User Access Administrator**:
   - On the **Conditions** tab, select **Allow user to assign all roles except privileged administrator roles**. Fkh only assigns non-privileged roles (AcrPull, Storage Blob Data Contributor, etc.).

### A.5 — (Optional) Grant Entra ID directory role for AAD container authentication

> **Performed by:** Entra ID Privileged Role Admin

> **Skip this step** if you do not need AAD authentication for containers. You can always come back and do this later.

During deployment, Terraform will need to grant the `Application.ReadWrite.OwnedBy` Graph permission to the Function's managed identity. To do that, the deployment identity needs an Entra ID directory role.

1. Go to **Azure Portal** → **Microsoft Entra ID** → **Roles and administrators**.
2. Search for **Privileged Role Administrator** and click on it.
3. Click **Add assignments** → **Select members**.
4. Search for `fkh-deploy-identity` (your Managed Identity) → select it → click **Select**.
5. Click **Next** → choose **Active** assignment → click **Assign**.

> **Scope:** This role allows the deployment identity to grant Graph application permissions during Terraform runs. It does not give it access to your Azure subscription resources — that is handled by the Contributor and User Access Administrator roles you assigned in A.4.

### A.6 — Save your values

Record these values — you will need them when configuring GitHub secrets and the environment file.

| Value | Where to find it |
|-------|-----------------|
| **Client ID** | Managed Identity → Overview |
| **Subscription ID** | Azure Portal → Subscriptions → your subscription |
| **Tenant ID** | Azure Portal → Microsoft Entra ID → Overview |

> **Done.** You have created the deployment identity. Continue to [Step 3 — Create the GitHub App](Step3-GitHubApp.md).

---

## Option B — App Registration

> **Who does this:** Steps B.1–B.2 are performed by the **Entra ID Privileged Role Admin**. Steps B.3–B.4 are performed by the **Azure Subscription Owner**. If one person holds both roles, they complete all steps.

This option creates the identity in Microsoft Entra ID rather than in Azure Resource Manager.

### B.1 — Create the App Registration

> **Performed by:** Entra ID Privileged Role Admin

1. Go to **Azure Portal** → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. Enter a name, e.g. `fkh-deploy`.
3. For **Supported account types**, keep the default (single tenant).
4. Leave **Redirect URI** blank.
5. Click **Register**.
6. On the overview page, note the **Application (client) ID**.

### B.2 — Add a federated credential for GitHub Actions

> **Performed by:** Entra ID Privileged Role Admin

1. In the App Registration, go to **Certificates & secrets** → **Federated credentials** → **Add credential**.
2. For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
3. Fill in:
   - **Organization**: your GitHub organization name (e.g. `my-company`)
   - **Repository**: the name of your **private deployment repository** from Step 1 (e.g. `fkh-deploy`) — **not** `Fkh`
   - **Entity type**: Branch
   - **Based on selection**: `main`
   - **Name**: `fkh-main-branch`
4. The **Issuer**, **Subject identifier**, and **Audience** fields are auto-populated. Click **Add**.

> **Important:** The repository must be your private deployment repo, not the Fkh fork. The deployment workflows run from the deployment repo, so the OIDC token issued by GitHub Actions will carry that repo's name in the subject claim.

### B.3 — Assign roles on the subscription

> **Performed by:** Azure Subscription Owner

The Entra ID admin must provide the name or Client ID of the App Registration created in B.1 so the Subscription Owner can find it when assigning roles.

1. Go to **Subscriptions** → select your subscription → **Access control (IAM)** → **Add** → **Add role assignment**.
2. Select the **Privileged administrator roles** tab.
3. Select **Contributor** → click **Next**.
4. Click **Select members** → search for `fkh-deploy` (the App Registration name) → select it → click **Select**.
5. Click **Review + assign**.
6. Repeat the above for **User Access Administrator**:
   - On the **Conditions** tab, select **Allow user to assign all roles except privileged administrator roles**. Fkh only assigns non-privileged roles (AcrPull, Storage Blob Data Contributor, etc.).

### B.4 — (Optional) Grant Entra ID directory role for AAD container authentication

> **Performed by:** Entra ID Privileged Role Admin

> **Skip this step** if you do not need AAD authentication for containers. You can always come back and do this later.

During deployment, Terraform will need to grant the `Application.ReadWrite.OwnedBy` Graph permission to the Function's managed identity. To do that, the deployment identity must itself hold an Entra ID directory role that allows granting Graph application permissions.

1. Go to **Azure Portal** → **Microsoft Entra ID** → **Roles and administrators**.
2. Search for **Privileged Role Administrator** and click on it.
3. Click **Add assignments** → **Select members**.
4. Search for `fkh-deploy` (your App Registration) → select it → click **Select**.
5. Click **Next** → choose **Active** assignment → click **Assign**.

> **Note:** Since the Entra ID admin already created the App Registration in B.1, they can do this step at the same time — no separate coordination needed.

### B.5 — Save your values

Record these values — you will need them when configuring GitHub secrets and the environment file.

| Value | Where to find it |
|-------|-----------------|
| **Client ID** | App Registration → Overview → Application (client) ID |
| **Subscription ID** | Azure Portal → Subscriptions → your subscription |
| **Tenant ID** | Azure Portal → Microsoft Entra ID → Overview (or App Registration → Overview → Directory (tenant) ID) |

> **Done.** You have created the deployment identity. Continue to [Step 3 — Create the GitHub App](Step3-GitHubApp.md).

---

## Summary

| Step | Option A (Managed Identity) | Option B (App Registration) |
|------|-----------------------------|-----------------------------|
| Create identity | Azure Subscription Owner creates Managed Identity in a resource group | Entra ID Admin creates App Registration in Entra ID |
| Federated credential | Azure Subscription Owner adds OIDC credential | Entra ID Admin adds OIDC credential |
| Subscription roles | Azure Subscription Owner assigns Contributor + User Access Administrator | Azure Subscription Owner assigns Contributor + User Access Administrator |
| Entra ID directory role | Optional — only if AAD container auth is desired (step A.5) | Optional — only if AAD container auth is desired (step B.4) |
| Entra ID admin needed? | Only if AAD container auth is desired | Yes, from the start (creates the App Registration); directory role is a natural addition |
| Values to save | Client ID, Subscription ID, Tenant ID | Client ID, Subscription ID, Tenant ID |

---

*Previous: [Step 1 — Create the Private Deployment Repository](Step1-DeploymentRepo.md)*
*Next: [Step 3 — Create the GitHub App](Step3-GitHubApp.md)*
