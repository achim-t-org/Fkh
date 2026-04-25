# Step 2 — Create the Azure Deployment Identity

The deployment identity is the identity GitHub Actions uses to create and manage Fkh infrastructure in Azure. It authenticates with Azure through OIDC, so you do not need to create or store a client secret.

> **Already have a deployment identity?** You can reuse a Managed Identity or App Registration from another Fkh deployment. Add a federated credential for the new deployment repository, confirm the identity has the required subscription roles on the target subscription, and then continue to [Step 3 — Create the GitHub App](Step3-GitHubApp.md).

## What you will create

By the end of this step, you will have:

- A deployment identity, either a Managed Identity or an App Registration.
- A federated credential that allows GitHub Actions to authenticate as that identity.
- Azure subscription role assignments for the identity.
- The Client ID, Subscription ID, and Tenant ID needed later.

## Choose an identity type

Both options work with the same GitHub workflows. Choose the option that best fits your organization's governance model.

|  | Option A: Managed Identity | Option B: App Registration |
|---|---|---|
| Lives in | Azure resource group | Microsoft Entra ID |
| Created by | Azure Subscription Owner | Entra ID Administrator |
| Entra ID admin required immediately? | No, unless you enable AAD container authentication | Yes |
| Best when | Your organization restricts App Registration creation, or you prefer Azure Resource Manager resources | Your organization already standardizes on App Registrations |
| Cleanup | Delete the identity resource or resource group | Delete the App Registration |

## AAD container authentication note

Fkh can optionally let users sign in to Business Central containers with their Microsoft 365 accounts. This is controlled by the following setting in `deployment.tfvars`:

```hcl
enable_aad_container_auth = true
```

This feature is disabled by default.

When AAD container authentication is enabled, Terraform grants the `Application.ReadWrite.OwnedBy` Microsoft Graph permission to the Function App managed identity that is created during deployment. To grant that permission, the deployment identity you create in this step must have the **Privileged Role Administrator** directory role in Entra ID.

Use this rule:

| If `enable_aad_container_auth` is... | Then... |
|---|---|
| `false` | Skip the optional Entra ID directory role step. No Entra ID admin involvement is required for Managed Identity deployments. |
| `true` | Complete step A.5 or B.4 before running the deployment. |

You can enable AAD container authentication later by changing the setting, granting the directory role, and re-running the deployment workflow.

---

## Option A — Managed Identity

> **Performed by:** Azure Subscription Owner

Use this option if you want the deployment identity to be an Azure resource.

### A.1 — Create a resource group for the identity

The Managed Identity needs a resource group. This is separate from the Fkh workload resource group that Terraform creates later.

1. In the Azure Portal, open **Resource groups**.
2. Select **Create**.
3. Choose the target subscription.
4. Enter a resource group name, for example `fkh-deploy`.
5. Choose any region. It does not need to match the Fkh deployment region.
6. Select **Review + create**.
7. Select **Create**.

### A.2 — Create the Managed Identity

1. In the Azure Portal, open **Managed Identities**.
2. Select **Create**.
3. Choose the subscription.
4. Choose the resource group from A.1, for example `fkh-deploy`.
5. Choose a region.
6. Enter a name, for example `fkh-deploy-identity`.
7. Select **Review + create**.
8. Select **Create**.
9. Open the new identity and copy the **Client ID** from the **Overview** page.

### A.3 — Add the federated credential

This credential allows GitHub Actions to authenticate as the Managed Identity through OIDC.

1. Open the Managed Identity.
2. Go to **Settings** → **Federated credentials**.
3. Select **Add credential**.
4. For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
5. Enter the following values:

| Field | Value |
|---|---|
| Organization | Your GitHub organization, for example `my-company` |
| Repository | Your private deployment repository, for example `fkh-deploy-contoso` |
| Entity type | `Branch` |
| Based on selection | `main` |
| Name | `fkh-main-branch` |

6. Confirm that **Issuer**, **Subject identifier**, and **Audience** are populated automatically.
7. Select **Add**.

> **Important:** use the private deployment repository name, not `Fkh`. The deployment workflows run from the private deployment repository.

### A.4 — Assign subscription roles

The deployment identity needs two roles on the target Azure subscription.

1. In the Azure Portal, open **Subscriptions**.
2. Select the target subscription.
3. Go to **Access control (IAM)**.
4. Select **Add** → **Add role assignment**.
5. Select the **Privileged administrator roles** tab.
6. Select **Contributor**.
7. Select **Next**.
8. Select **Select members**.
9. Search for the Managed Identity, for example `fkh-deploy-identity`.
10. Select the identity, then select **Select**.
11. Select **Review + assign**.
12. Repeat the process for **User Access Administrator**.
13. On the **Conditions** tab for **User Access Administrator**, select **Allow user to assign all roles except privileged administrator roles**.

Fkh uses this permission to assign non-privileged roles such as `AcrPull` and `Storage Blob Data Contributor`.

### A.5 — Optional: grant Entra ID directory role

> **Performed by:** Entra ID Privileged Role Admin

Skip this section unless you will set `enable_aad_container_auth = true`.

1. In the Azure Portal, open **Microsoft Entra ID**.
2. Go to **Roles and administrators**.
3. Search for **Privileged Role Administrator**.
4. Open the role.
5. Select **Add assignments**.
6. Select **Select members**.
7. Search for the Managed Identity, for example `fkh-deploy-identity`.
8. Select the identity, then select **Select**.
9. Select **Next**.
10. Choose an **Active** assignment.
11. Select **Assign**.

This role allows the deployment identity to grant Microsoft Graph application permissions during Terraform runs. Azure subscription access is still controlled separately by the roles assigned in A.4.

### A.6 — Save your values

Record the following values for Step 5:

| Value | Where to find it |
|---|---|
| Client ID | Managed Identity → **Overview** |
| Subscription ID | Azure Portal → **Subscriptions** → target subscription |
| Tenant ID | Azure Portal → **Microsoft Entra ID** → **Overview** |

You can now continue to [Step 3 — Create the GitHub App](Step3-GitHubApp.md).

---

## Option B — App Registration

> **Performed by:** Entra ID Privileged Role Admin and Azure Subscription Owner

Use this option if your organization prefers deployment identities to live in Microsoft Entra ID.

### B.1 — Create the App Registration

> **Performed by:** Entra ID Privileged Role Admin

1. In the Azure Portal, open **Microsoft Entra ID**.
2. Go to **App registrations**.
3. Select **New registration**.
4. Enter a name, for example `fkh-deploy`.
5. Keep **Supported account types** set to the default single-tenant option.
6. Leave **Redirect URI** empty.
7. Select **Register**.
8. On the overview page, copy the **Application (client) ID**.

### B.2 — Add the federated credential

> **Performed by:** Entra ID Privileged Role Admin

1. Open the App Registration.
2. Go to **Certificates & secrets** → **Federated credentials**.
3. Select **Add credential**.
4. For **Federated credential scenario**, select **GitHub Actions deploying Azure resources**.
5. Enter the following values:

| Field | Value |
|---|---|
| Organization | Your GitHub organization, for example `my-company` |
| Repository | Your private deployment repository, for example `fkh-deploy-contoso` |
| Entity type | `Branch` |
| Based on selection | `main` |
| Name | `fkh-main-branch` |

6. Confirm that **Issuer**, **Subject identifier**, and **Audience** are populated automatically.
7. Select **Add**.

> **Important:** use the private deployment repository name, not `Fkh`. The deployment workflows run from the private deployment repository.

### B.3 — Assign subscription roles

> **Performed by:** Azure Subscription Owner

The Entra ID admin should provide the App Registration name or Client ID to the Azure Subscription Owner.

1. In the Azure Portal, open **Subscriptions**.
2. Select the target subscription.
3. Go to **Access control (IAM)**.
4. Select **Add** → **Add role assignment**.
5. Select the **Privileged administrator roles** tab.
6. Select **Contributor**.
7. Select **Next**.
8. Select **Select members**.
9. Search for the App Registration, for example `fkh-deploy`.
10. Select it, then select **Select**.
11. Select **Review + assign**.
12. Repeat the process for **User Access Administrator**.
13. On the **Conditions** tab for **User Access Administrator**, select **Allow user to assign all roles except privileged administrator roles**.

Fkh uses this permission to assign non-privileged roles such as `AcrPull` and `Storage Blob Data Contributor`.

### B.4 — Optional: grant Entra ID directory role

> **Performed by:** Entra ID Privileged Role Admin

Skip this section unless you will set `enable_aad_container_auth = true`.

1. In the Azure Portal, open **Microsoft Entra ID**.
2. Go to **Roles and administrators**.
3. Search for **Privileged Role Administrator**.
4. Open the role.
5. Select **Add assignments**.
6. Select **Select members**.
7. Search for the App Registration, for example `fkh-deploy`.
8. Select it, then select **Select**.
9. Select **Next**.
10. Choose an **Active** assignment.
11. Select **Assign**.

Because the Entra ID admin creates the App Registration in B.1, they can usually complete this optional step at the same time.

### B.5 — Save your values

Record the following values for Step 5:

| Value | Where to find it |
|---|---|
| Client ID | App Registration → **Overview** → **Application (client) ID** |
| Subscription ID | Azure Portal → **Subscriptions** → target subscription |
| Tenant ID | Azure Portal → **Microsoft Entra ID** → **Overview**, or App Registration → **Overview** → **Directory (tenant) ID** |

You can now continue to [Step 3 — Create the GitHub App](Step3-GitHubApp.md).

---

## Step summary

| Task | Managed Identity | App Registration |
|---|---|---|
| Create identity | Azure Subscription Owner | Entra ID Admin |
| Add federated credential | Azure Subscription Owner | Entra ID Admin |
| Assign Azure subscription roles | Azure Subscription Owner | Azure Subscription Owner |
| Grant Entra ID directory role | Optional; only for AAD container authentication | Optional; only for AAD container authentication |
| Values to save | Client ID, Subscription ID, Tenant ID | Client ID, Subscription ID, Tenant ID |

---

*Previous: [Step 1 — Create the Private Deployment Repository](Step1-DeploymentRepo.md)*  
*Next: [Step 3 — Create the GitHub App](Step3-GitHubApp.md)*
