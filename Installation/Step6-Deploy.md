# Step 6 — Deploy

> **Performed by:** GitHub Organization Administrator, or anyone with write access to the deployment repository

In this step you run the **Deploy Full Stack** workflow. The workflow provisions the Azure infrastructure and publishes the Fkh backend.

The workflow is fully automated after you start it.

## Before you start

Confirm that the previous steps are complete:

| Requirement | Where it was configured |
|---|---|
| Private deployment repository exists | Step 1 |
| Deployment identity exists and has Azure roles | Step 2 |
| GitHub App exists and is installed on the deployment repository | Step 3 |
| GitHub teams are created or selected | Step 4 |
| `deployment.tfvars` is committed to `main` | Step 5 |
| Required GitHub Secrets exist | Step 5 |

---

## 6.1 — Run the Deploy Full Stack workflow

1. Open your private deployment repository in GitHub.
2. Go to the **Actions** tab.
3. In the workflow list, select **Deploy Full Stack**.
4. Select **Run workflow**.
5. Choose the `main` branch.
6. Select **Run workflow**.

## What the workflow does

The workflow calls the reusable `DeployFkhFullStack` workflow from your Fkh repository or fork.

| Phase | What happens |
|---|---|
| Checkout | Checks out the deployment repository and the Fkh repository |
| Parse tfvars | Reads `tenant_id`, `subscription_id`, and `fkhDeploymentName` from `deployment.tfvars` |
| Azure login | Authenticates to Azure through OIDC using the deployment identity from Step 2 |
| State storage | Creates Terraform state resource group, storage account, and blob container if needed |
| Bootstrap apply | Creates core Azure resources such as AKS, Container Registry, Function App, managed identity, and role assignments |
| Full apply | Deploys remaining infrastructure, including Kubernetes resources, SQL Server, services, Helm charts, and configuration |
| Publish function | Builds and publishes the .NET backend to the Azure Function App |
| Sync secrets | Writes generated deployment values back to the deployment repository as GitHub Secrets |

The synced secrets include:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
ACR_LOGIN_SERVER
DBS_STORAGE_ACCOUNT
```

These are used by other workflows, such as `CreateImages`.

---

## 6.2 — Monitor the workflow

Open the running workflow in GitHub Actions to follow progress.

If the run fails, use the table below to triage common issues.

| Symptom | Likely cause | What to do |
|---|---|---|
| RBAC propagation timeout | Azure role assignment has not propagated yet | Re-run the workflow. The role assignment already exists and is usually available on the next run. |
| Terraform bootstrap error | Resource name conflict, quota limit, or partially created resource | Read the Terraform error. If resources were partially created by a previous run, re-running often lets Terraform adopt or reconcile them. |
| Function publish failure | Function App publishing or SDK mismatch issue | The workflow tries to restart the Function App automatically. If it still fails, check that the .NET SDK version in the workflow matches the backend target framework. |

> **Safe to re-run:** the workflow is idempotent. You can re-run it at any time. Terraform only changes resources when the actual state differs from the desired state.

---

## 6.3 — Verify the deployment

After the workflow completes successfully, verify the deployment in Azure and GitHub.

### Azure resources

In the Azure Portal, open the resource group named:

```text
fkh-<deploymentName>
```

You should see resources similar to these:

| Resource | Name pattern |
|---|---|
| AKS cluster | `fkh-<name>-aks` |
| Container Registry | `fkh<name>acr` |
| Function App | `fkh-<name>-backend` |
| Function App storage account | `fkh<name>func` |
| Database backup storage account | `fkh<name>dbs` |
| Managed Identity | `fkh-<name>-identity` |
| Log Analytics workspace | `fkh-<name>-logs` |
| Application Insights | `fkh-<name>-insights` |

### Function App health

1. Open the Function App in the Azure Portal.
2. Go to **Functions**.
3. Confirm that functions such as `CreateContainer`, `StartContainer`, and `StopContainer` are listed.

### Synced GitHub Secrets

In the deployment repository, go to:

```text
Settings → Secrets and variables → Actions
```

Confirm that these secrets were created automatically:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
ACR_LOGIN_SERVER
DBS_STORAGE_ACCOUNT
```

---

## 6.4 — Re-deploy later

Run **Deploy Full Stack** again when you need to:

- Apply changes from `deployment.tfvars`.
- Pick up updates from a newer version of the Fkh repository or fork.
- Recover from a partial deployment.

For backend-only changes, use the **Update Backend** workflow instead. It is faster because it skips Terraform and only republishes the Function App code.

---

## What was created

### Azure resources

| Resource | Name pattern | Purpose |
|---|---|---|
| Resource group | `fkh-<name>` | Contains the Fkh workload resources |
| Resource group for state | `fkh-<name>-state` | Contains Terraform state storage |
| AKS cluster | `fkh-<name>-aks` | Runs Business Central containers and SQL Server |
| Container Registry | `fkh<name>acr` | Stores Business Central Docker images |
| Function App | `fkh-<name>-backend` | API backend that manages containers |
| Managed Identity | `fkh-<name>-identity` | Used by the Function App for Azure and AKS access |
| Storage account for Function App | `fkh<name>func` | Internal Function App storage |
| Storage account for databases | `fkh<name>dbs` | Database backup storage |
| Log Analytics workspace | `fkh-<name>-logs` | Centralized logging |
| Application Insights | `fkh-<name>-insights` | Function App telemetry |

### GitHub Secrets synced by the workflow

| Secret | Purpose |
|---|---|
| `AZURE_CLIENT_ID` | Managed identity Client ID used by the `CreateImages` workflow |
| `AZURE_TENANT_ID` | Azure tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `ACR_LOGIN_SERVER` | Container Registry login server FQDN |
| `DBS_STORAGE_ACCOUNT` | Database backup storage account name |

## Installation complete

Fkh is now deployed. Use the deployment repository workflows for future updates, image builds, and backend deployments.

---

*Previous: [Step 5 — Configure Your Environment](Step5-ConfigureEnvironment.md)*
