# Fkh Architecture

Fkh is a **GitHub-authenticated AKS container provisioner** that allows authorized GitHub team members to create on-demand Business Central environments on Azure Kubernetes Service — directly from VS Code or a CLI — without requiring Azure credentials.

## High-Level Overview

```mermaid
graph LR
    subgraph Clients
        VSIX["VS Code Extension"]
        CLI["CLI Tool"]
    end

    FUNC["Fkh Backend<br/>(Azure Function)"]
    GH["GitHub API"]

    subgraph AKS["AKS Cluster"]
        direction TB
        SQL["SQL Server<br/>(Linux)"]
        BC["BC Containers<br/>(Windows)"]
        SPOT["BC Containers<br/>(Windows Spot)"]
    end

    ACR["Container<br/>Registry"]
    BLOB["Blob Storage<br/>(.bak files)"]

    VSIX & CLI -->|"GitHub token"| FUNC
    FUNC -->|"validate user"| GH
    FUNC -->|"manage containers"| AKS
    FUNC -->|"check/pull images"| ACR
    FUNC -->|"database backups"| BLOB
    BC & SPOT -->|"TCP 1433"| SQL
```

## Request Flow

```mermaid
graph LR
    A["User"] -->|"POST /api/CreateContainer<br/>Bearer: github_token"| B["FunctionBase<br/>(auth + validate)"]
    B -->|"GET /user"| C["GitHub API"]
    B --> D["FkhCreateContainer"]
    D --> E["Check ACR image"]
    D --> F["Restore DB"]
    D --> G["Create K8s resources"]
    E -->|"missing?"| H["Trigger GitHub Actions<br/>(build image)"]
```

## Infrastructure Layout

```mermaid
graph TB
    subgraph RG["Resource Group: fkh-&lt;org&gt;"]
        subgraph AKS["AKS Cluster"]
            LP["Linux Pool<br/>(SQL Server, persisted)"]
            WP["Windows Pool<br/>(BC containers)"]
            SP["Windows Spot Pool<br/>(optional, cheaper)"]
        end
        FA["Function App<br/>(Consumption)"]
        ACR["Container Registry"]
        MI["Managed Identity"]
        DBS["Storage: DBS<br/>(database backups)"]
        FST["Storage: Func<br/>(runtime state)"]
        MON["Log Analytics<br/>+ App Insights"]
    end

    GH["GitHub<br/>(Teams + App)"]

    FA -->|"Managed Identity"| AKS & ACR & DBS
    MI -.-> FA
    GH -.->|"auth"| FA
```

## Component Descriptions

### User Interfaces

| Component | Path | Description |
|-----------|------|-------------|
| **VS Code Extension** | `fkh-vsix/` | Registers commands to create/remove containers. Uses VS Code's built-in GitHub auth to obtain a Bearer token and calls the Function App API. Fetches the function catalog for dynamic parameter prompts. Auto-detects public IP for SQL access. Parameter defaults can be set via `fkh.<Function>.<param>` settings. |
| **CLI Tool** | `fkh-cli/` | Standalone .NET executable (`fkh.exe`). Reads GitHub token from `GH_TOKEN`, `GITHUB_TOKEN`, or `gh auth token`. Interactively prompts for parameters with masked password input. Auto-detects public IP for SQL access. |

### Azure Functions Backend

| Component | Path | Description |
|-----------|------|-------------|
| **FunctionBase** | `fkh-backend/FunctionBase.cs` | Base class for all HTTP functions. Extracts Bearer token, validates it against GitHub API, checks team membership, parses and validates parameters against the function catalog, and injects the GitHub username. |
| **GitHubAuthService** | `fkh-backend/Services/GitHubAuthService.cs` | Calls `GET /user` and `GET /orgs/{org}/teams/{team}/memberships/{username}` to authenticate and authorize requests. Allowed org/team pairs loaded from `ALLOWED_ORG_TEAMS` env var. |
| **GitHubAppTokenService** | `fkh-backend/Services/GitHubAppTokenService.cs` | Creates JWTs signed with the GitHub App private key, exchanges for installation access tokens, and dispatches the `createImages` workflow when an image is missing from ACR. |
| **FkhCreateContainer** | `fkh-backend/Services/FkhCreateContainer.cs` | Orchestrates container creation: ACR image check → database backup SAS URL → k8s exec to download and restore database → create K8s deployment, service, and secret. |
| **FkhRemoveContainer** | `fkh-backend/Services/FkhRemoveContainer.cs` | Removes Kubernetes resources (deployment, service, secret) and drops the database for a given container. |
| **FkhScaleContainer** | `fkh-backend/Services/FkhScaleContainer.cs` | Scales a container's deployment: StopContainer sets replicas to 0, StartContainer sets replicas to 1. Database is preserved across stop/start. |
| **FkhListVMs** | `fkh-backend/Services/FkhListVMs.cs` | Lists VMs filtered by user (or all). Shows status, image, web client URL, and CPU/memory usage via the metrics API. |
| **FkhAllowSqlAccess** | `fkh-backend/Services/FkhAllowSqlAccess.cs` | Manages temporary external SQL Server access. Creates a per-user LoadBalancer service (IP-restricted via `loadBalancerSourceRanges`) and a NetworkPolicy allowing the user's IP through to the MSSQL pod. Auto-revokes expired grants via the timer-triggered AutoStop function. |
| **FkhServiceBase** | `fkh-backend/Services/FkhServiceBase.cs` | Shared base class with AKS/ACR/Storage config, Kubernetes client creation via managed identity, and k8s exec helpers (`FindMssqlPodAsync`, `ExecInMssqlPodAsync`). |

### Infrastructure (Terraform)

| Resource | File | Description |
|----------|------|-------------|
| **AKS Cluster** | `main.tf` | Linux system pool (1× D2s_v3) + Windows autoscale pool (0–10 nodes). Azure CNI overlay networking. |
| **Function App** | `function.tf` | Windows Consumption (Y1) plan. Isolated .NET 8 worker. All config injected via app settings. |
| **SQL Server** | `kubernetes.tf` | `mssql/server:2022-latest` on Linux pod with 128 Gi Premium SSD PVC. ClusterIP service on port 1433. Network policy restricts ingress to `app-type: windows-servicetier` pods. External access can be temporarily granted per-user via `AllowSqlAccess`. |
| **ACR** | `acr.tf` | Basic SKU. AKS kubelet identity gets `AcrPull`; GitHub Actions federated identity gets `AcrPush`. |
| **Managed Identity** | `identity.tf` | User-assigned identity with AKS Contributor + Storage Blob Data Contributor roles. Federated credential for GitHub Actions OIDC. |
| **Storage (DBS)** | `function.tf` | `fkh{org}dbs` — holds database backup blobs in a `cronus` container, keyed by image tag. |
| **Storage (Func)** | `function.tf` | `fkh{org}func` — Azure Functions runtime state (queues, tables). |
| **GitHub Team** | `github.tf` | Manages the authorized team within the GitHub organization. |
| **Monitoring** | `monitoring.tf` | Log Analytics workspace (30-day retention) + Application Insights for function telemetry. |

### GitHub Actions

| Workflow | Trigger | Description |
|----------|---------|-------------|
| **CreateImages** | `workflow_dispatch` (artifactUrls) | Downloads BC artifacts via `BcContainerHelper`, extracts and uploads the `.bak` database backup to blob storage, builds the container image with `New-BcImage`, and pushes to ACR. Authenticates to Azure via OIDC federated identity. |

## Authentication Flow

There are three authentication paths:

1. **User requests** — VS Code / CLI sends a GitHub PAT to the Function App, which validates it against GitHub API.
2. **GitHub Actions → Function App (OIDC)** — A GitHub Actions workflow authenticates to the Function App using an OIDC ID token. The Function App validates the token and checks the repository against the `ALLOWED_OIDC_REPOS` allow-list.
3. **GitHub Actions → Azure (OIDC)** — GitHub Actions authenticates directly to Azure AD via a federated credential to push images to ACR and upload database backups to Blob Storage.

```mermaid
graph LR
    subgraph "Auth"
        U["User<br/>(VS Code / CLI)"] -->|"GitHub PAT"| FB["FunctionBase"]
        GA_OIDC["GitHub Actions<br/>(OIDC ID token)"] -->|"Bearer: id_token"| FB
        FB -->|"GET /user"| GH["GitHub API"]
        FB -->|"GET /teams/.../memberships"| GH
        GH -->|"username + team ✓"| FB
    end

    subgraph "Function → Azure"
        FB -->|"Managed Identity"| AKS["AKS<br/>(k8s API)"]
        FB -->|"Managed Identity"| ACR["Container<br/>Registry"]
        FB -->|"Managed Identity"| BLOB["Blob<br/>Storage"]
    end

    subgraph "CI/CD → Azure (direct)"
        GA["GitHub Actions"] -->|"OIDC token"| AAD["Azure AD"]
        AAD -->|"federated credential"| ACR2["ACR<br/>(AcrPush)"]
        AAD -->|"federated credential"| BLOB2["Blob Storage<br/>(upload .bak)"]
    end
```

### Detailed Sequence

```mermaid
sequenceDiagram
    participant User as User (VS Code / CLI)
    participant Func as Azure Function
    participant GH as GitHub API
    participant AKS as AKS (k8s API)
    participant Blob as Blob Storage
    participant ACR as ACR

    User->>Func: POST /api/CreateContainer (Bearer: gh_token)
    Func->>GH: GET /user (validate token)
    GH-->>Func: { login: "username" }
    Func->>GH: GET /orgs/{org}/teams/{team}/memberships/{username}
    GH-->>Func: { state: "active" }
    Note over Func: Authorized ✓

    Func->>ACR: Check image exists (ManagedIdentityCredential)
    ACR-->>Func: 200 OK / 404

    Func->>Blob: GetUserDelegationKey + build SAS URL (ManagedIdentity)
    Blob-->>Func: SAS URL

    Func->>AKS: Get kubeconfig (ManagedIdentity → ARM)
    Func->>AKS: k8s exec → curl .bak into mssql pod
    Func->>AKS: k8s exec → sqlcmd RESTORE DATABASE
    Func->>AKS: Create Deployment + Service + Secret

    Func-->>User: Container created (FQDN, deployment info)
```

## Container Creation Flow

1. **Image check** — Verify the requested BC image exists in ACR. If missing, trigger the `createImages` GitHub Actions workflow via the GitHub App and return an error asking the user to retry.
2. **Database backup** — Generate a 1-hour read-only SAS URL for the `.bak` blob in the DBS storage account.
3. **Database existence check** — K8s exec into the mssql pod and run `sqlcmd` to verify the database doesn't already exist.
4. **Database restore** — K8s exec to `curl` the backup into the pod, then `sqlcmd` to `RESTORE DATABASE` with `MOVE` clauses for data/log files.
5. **Kubernetes resources** — Create a Secret (admin password), a Deployment (Windows container with BC image and database env vars), and a LoadBalancer Service (public IP with Azure DNS label).
6. **Return** — FQDN (`{appName}.{region}.cloudapp.azure.com`), deployment name, and database name.

## SQL Access Flow

`AllowSqlAccess` grants temporary direct SQL Server access from a user's public IP:

1. **Create LoadBalancer service** — `mssql-ext-{username}` with `loadBalancerSourceRanges` set to the user's IP/32. Targets the mssql pod on port 1433.
2. **Create NetworkPolicy** — `mssql-allow-ip-{username}` with an ingress rule allowing the user's CIDR to reach the mssql pod on port 1433.
3. **Wait for external IP** — Polls the service status until Azure assigns a public IP (up to ~2.5 minutes).
4. **Return** — SQL endpoint (`{externalIp},1433`), allowed IP, and auto-revoke time.

Access is auto-revoked by the `AutoStop` timer function (runs every 30 minutes),
which checks for `fkh/sql-access-revoke-at` annotations on the services and
deletes expired resources. Users can also revoke access immediately via `RevokeSqlAccess`.

Each user can have only one active SQL access grant. Calling `AllowSqlAccess` again
replaces the existing grant (updating the allowed IP and extending the timer).

## Deployment

Infrastructure is provisioned via `terraform/deploy.ps1`, which:

1. Creates state storage (resource group + storage account + blob container)
2. Recovers secrets (SQL SA password, GitHub App key) from existing state or prompts
3. Runs a targeted bootstrap apply (AKS, storage, identity, function)
4. Runs a full `terraform apply` (Kubernetes resources, monitoring, GitHub)
5. Publishes the Azure Function code via `dotnet publish` + `az functionapp deployment`
6. Syncs GitHub Actions secrets (OIDC credentials, ACR login server, DBS storage account)
