# Azure Setup (Path A — GitHub Actions)

The deployment identity allows the Fkh workflows to deploy and manage the entire Fkh Kubernetes cluster and backend. You can use either an **App Registration** or a **User-Assigned Managed Identity** — both work with OIDC (passwordless) authentication from GitHub Actions.

| Option | Best when |
|--------|-----------|
| **Option 1 — App Registration** | You have permission to create App Registrations in Entra ID, or your org already uses them. |
| **Option 2 — Managed Identity** | Your org restricts App Registration creation, or you prefer keeping everything inside Azure Resource Manager. |

Both options result in a **client ID** that you store in the `AZURE_DEPLOY_CLIENT_ID` GitHub secret. The workflows don't need to know which type of identity is behind the client ID.

---

## Required Permissions

The person performing this step needs these permissions:

**For role assignments on the subscription** (both options):
- **Owner** — can assign roles to others, or
- **User Access Administrator** — specifically grants the ability to assign roles

Regular Contributor alone is not enough, since it can't manage role assignments.

**Additionally for Option 1 (App Registration)**:
- **Application Administrator** or **Cloud Application Administrator** role in Microsoft Entra ID
- Or the legacy **Global Administrator** role (broader than needed)

**Additionally for Option 2 (Managed Identity)**:
- **Owner** or **Contributor** on the subscription (or on the resource group you create) to create the identity resource

---

## Option 1 — App Registration

1. Create an **App Registration** in Azure AD (Azure Portal → Microsoft Entra ID → App registrations → New registration).
2. Add a **federated credential** for GitHub Actions OIDC:
   - In the App Registration, go to **Certificates & secrets** → **Federated credentials** → **Add credential**.
   - For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
   - Fill in:
     - **Organization**: `<your-github-org>`
     - **Repository**: `Fkh`
     - **Entity type**: `Branch`
     - **Based on selection**: `main`
     - **Name**: `fkh-main-branch`
   - The **Issuer**, **Subject identifier**, and **Audience** fields are auto-populated. Click **Add**.
3. Assign roles on the target Azure subscription:
   - Go to **Subscriptions** → your subscription → **Access control (IAM)** → **Add** → **Add role assignment**.
   - Select the **Privileged administrator roles** tab.
   - Select **Contributor** → **Next** → **Select members** → search for your App Registration → **Select** → **Review + assign**.
   - Repeat for **User Access Administrator** (also on the **Privileged administrator roles** tab).
   - On the **Conditions** tab, select **Allow user to assign all roles except privileged administrator roles** (the recommended default). Fkh only assigns non-privileged roles (AcrPull, Storage Blob Data Contributor, etc.).
4. Save the **Application (client) ID** from the App Registration overview page.

> If your org uses custom roles, you need: create/manage resource groups, AKS clusters, Function Apps, storage accounts, container registries, managed identities, role assignments, and Log Analytics workspaces.

---

## Option 2 — Managed Identity

A Managed Identity lives inside a resource group, so you'll create a small resource group as part of this step. This is separate from the resource group that Terraform creates for the Fkh workload.

### Step 1 — Create the Managed Identity

1. Go to **Azure Portal** → search for **Managed Identities** → **Create**.
2. Select your subscription.
3. For **Resource group**, click **Create new** and enter a name, e.g. `fkh-deploy`. Click **OK**.
4. Choose any region (it doesn't have to match where Fkh deploys).
5. Enter a name, e.g. `fkh-deploy-identity`.
6. Click **Review + create** → **Create**.
7. Once created, open the identity and note the **Client ID** from the **Overview** page — you'll need it later.

### Step 2 — Add a federated credential for GitHub Actions OIDC

1. In the Managed Identity, go to **Settings** → **Federated credentials** → **Add credential**.
2. For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
3. Fill in:
   - **Organization**: `<your-github-org>`
   - **Repository**: `Fkh`
   - **Entity type**: `Branch`
   - **Based on selection**: `main`
   - **Name**: `fkh-main-branch`
4. The **Issuer**, **Subject identifier**, and **Audience** fields are auto-populated. Click **Add**.

### Step 3 — Assign roles on the target subscription

1. Go to **Subscriptions** → your subscription → **Access control (IAM)** → **Add** → **Add role assignment**.
2. Select the **Privileged administrator roles** tab.
3. Select **Contributor** → **Next** → **Select members** → search for your Managed Identity → **Select** → **Review + assign**.
4. Repeat for **User Access Administrator** (also on the **Privileged administrator roles** tab).
   - On the **Conditions** tab, select **Allow user to assign all roles except privileged administrator roles** (the recommended default). Fkh only assigns non-privileged roles (AcrPull, Storage Blob Data Contributor, etc.).

### Step 4 — Save the client ID

Go back to the Managed Identity → **Overview** and copy the **Client ID**. This value goes into the `AZURE_DEPLOY_CLIENT_ID` GitHub secret.

---

## Values to Save

You'll need these values for [Configure Your Environment](ConfigureEnvironment.md):

| Value | Where to find it |
|-------|-----------------|
| **Subscription ID** | Azure Portal → Subscriptions |
| **Tenant ID** | Azure Portal → Microsoft Entra ID → Overview |
| **Region** | Pick one: `westeurope`, `eastus`, `swedencentral`, etc. |
| **Client ID** | App Registration overview page (Option 1) or Managed Identity overview page (Option 2) |

## Next Step

→ [Create the GitHub App](GitHubApp.md)
