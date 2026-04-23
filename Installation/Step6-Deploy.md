# Step 6 — Deploy

> **Performed by:** GitHub Organization Admin (or anyone with write access to the deployment repo)

In this step you run the **Deploy Full Stack** workflow, which provisions all Azure infrastructure and publishes the Fkh backend. The workflow is fully automated — once triggered, it takes roughly 15–25 minutes to complete.

---

## 6.1 — Run the Deploy Full Stack workflow

1. Go to your **deployment repository** on GitHub.
2. Click the **Actions** tab.
3. Select **Deploy Full Stack** from the workflow list on the left.
4. Click **Run workflow** → choose the `main` branch → click **Run workflow**.

The workflow calls the reusable `DeployFkhFullStack` workflow in your Fkh fork, which performs these steps automatically:

| Phase | What happens |
|-------|-------------|
| **Checkout** | Checks out both the deployment repo (for `deployment.tfvars`) and the Fkh fork (for Terraform modules and backend code) |
| **Parse tfvars** | Reads `tenant_id`, `subscription_id`, and `fkhDeploymentName` from your tfvars file |
| **Azure login** | Authenticates to Azure via OIDC using the deployment identity from Step 2 |
| **State storage** | Creates a resource group, storage account, and blob container for Terraform state (if they don't already exist) |
| **Bootstrap apply** | Creates AKS cluster, Container Registry, Function App, managed identity, and core role assignments |
| **Full apply** | Deploys remaining infrastructure — Kubernetes resources (namespace, SQL Server, services), Helm charts, and all remaining configuration |
| **Publish function** | Builds and publishes the .NET backend to the Azure Function App |
| **Sync secrets** | Writes `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ACR_LOGIN_SERVER`, and `DBS_STORAGE_ACCOUNT` as GitHub Secrets in the deployment repo (used by other workflows like `CreateImages`) |

## 6.2 — Monitor the run

Click into the running workflow to watch progress. If a step fails:

- **RBAC propagation timeout** — The workflow waits up to 120 seconds for the Storage Blob Data Contributor role to take effect on the state storage account. If this times out, re-run the workflow — the role assignment already exists and will be available on the next attempt.
- **Terraform bootstrap errors** — Usually caused by Azure resource name conflicts or quota limits. Check the error message for details. Name conflicts typically mean a previous partial deployment left resources behind; Terraform will adopt them on re-run.
- **func publish fails** — The workflow automatically restarts the Function App as a fallback. If it still fails, check that the .NET SDK version in the workflow matches the backend project's target framework.

> **Tip:** The workflow is idempotent. You can safely re-run it at any time — Terraform will only make changes where the actual state differs from the desired state.

## 6.3 — Verify the deployment

Once the workflow completes successfully:

1. **Azure Portal** — Navigate to the resource group `fkh-<deploymentName>` in your subscription. You should see:
   - An AKS cluster (`fkh-<name>-aks`)
   - A Container Registry (`fkh<name>acr`)
   - A Function App (`fkh-<name>-backend`)
   - Two storage accounts (one for the Function App, one for database backups)
   - A managed identity (`fkh-<name>-identity`)
   - Log Analytics workspace and Application Insights

2. **Function App health** — Open the Function App in the Azure Portal and go to **Functions**. You should see a list of functions (CreateContainer, StartContainer, StopContainer, etc.).

3. **GitHub Secrets** — Go to your deployment repo → **Settings → Secrets and variables → Actions**. Confirm that `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ACR_LOGIN_SERVER`, and `DBS_STORAGE_ACCOUNT` were created (these are synced automatically by the workflow).

## 6.4 — Re-deploying

You can re-run the **Deploy Full Stack** workflow at any time to:

- Apply configuration changes made to `deployment.tfvars`
- Pick up updates from a newer version of the Fkh fork
- Recover from a partial deployment

For **backend-only updates** (no infrastructure changes), use the **Update Backend** workflow instead — it's faster because it skips Terraform and only re-publishes the Function App code.

---

## What was created

### Azure resources

| Resource | Name pattern | Purpose |
|----------|-------------|---------|
| Resource group | `fkh-<name>` | Contains all Fkh resources |
| Resource group (state) | `fkh-<name>-state` | Contains Terraform state storage |
| AKS cluster | `fkh-<name>-aks` | Runs BC containers and SQL Server |
| Container Registry | `fkh<name>acr` | Stores BC Docker images |
| Function App | `fkh-<name>-backend` | API backend that manages containers |
| Managed Identity | `fkh-<name>-identity` | Used by the Function App for Azure and AKS access |
| Storage (function) | `fkh<name>func` | Function App internal storage |
| Storage (databases) | `fkh<name>dbs` | Database backup storage |
| Log Analytics | `fkh-<name>-logs` | Centralized logging |
| Application Insights | `fkh-<name>-insights` | Function App telemetry |

### GitHub Secrets (synced automatically)

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Managed identity client ID (for `CreateImages` workflow) |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `ACR_LOGIN_SERVER` | Container Registry login server FQDN |
| `DBS_STORAGE_ACCOUNT` | Database backup storage account name |

---

*Previous: [Step 5 — Configure Your Environment](Step5-ConfigureEnvironment.md)*
