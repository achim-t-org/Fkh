# Fkh Architecture

Fkh is a **GitHub-authenticated AKS node provisioner** that allows authorized GitHub team members to create on-demand Business Central environments on Azure Kubernetes Service — directly from VS Code or a CLI — without requiring Azure credentials.

## High-Level Overview

```mermaid
graph TB
    subgraph "User Interfaces"
        VSIX["VS Code Extension<br/>(fkh-vsix)"]
        CLI["CLI Tool<br/>(fkh)"]
    end

    subgraph "GitHub"
        GH_AUTH["GitHub API<br/>(User Auth + Team Check)"]
        GH_APP["GitHub App<br/>(Workflow Trigger)"]
        GH_ACTIONS["GitHub Actions<br/>(CreateImages Workflow)"]
    end

    subgraph "Azure Functions (Consumption Plan)"
        FB["FunctionBase<br/>Auth & Parameter Validation"]
        CNF["CreateNodeFunction"]
        RNF["RemoveNodeFunction"]
        SNDF["StopNodeFunction"]
        SNTF["StartNodeFunction"]
        LNF["ListNodesFunction"]
        ASAF["AllowSqlAccessFunction"]
        RSAF["RevokeSqlAccessFunction"]
        CAT["GetFunctionCatalog"]
        CN["FKHCreateNode"]
        RN["FKHRemoveNode"]
        SN["FKHScaleNode"]
        LN["FKHListNodes"]
        SA["FKHAllowSqlAccess"]
        GAS["GitHubAppTokenService"]
        GHS["GitHubAuthService"]
    end

    subgraph "Azure Kubernetes Service"
        subgraph "Namespace: app"
            MSSQL["SQL Server 2022<br/>(Linux Pod)"]
            BC["Business Central<br/>(Windows Pod)"]
            LB["LoadBalancer Service<br/>(Public IP + DNS)"]
            SQL_LB["SQL LoadBalancer Service<br/>(Temporary, per-user IP)"]
            NP["NetworkPolicy<br/>(per-user IP allow)"]
            SEC["Kubernetes Secrets"]
        end
    end

    subgraph "Azure Storage"
        DBS["DBS Storage Account<br/>(cronus container — .bak files)"]
        FUNC_ST["Function Storage Account<br/>(runtime state)"]
    end

    ACR["Azure Container Registry"]
    MI["Managed Identity"]
    LOGS["Log Analytics<br/>+ App Insights"]

    %% User → Function
    VSIX -->|"POST /api/* (GitHub Bearer token)"| FB
    CLI -->|"POST /api/* (GitHub Bearer token)"| FB

    %% Function internal flow
    FB -->|validate token| GHS
    GHS -->|"GET /user + team membership"| GH_AUTH
    FB --> CNF & RNF & SNDF & SNTF & LNF & ASAF & RSAF & CAT
    CNF --> CN
    RNF --> RN
    SNDF --> SN
    SNTF --> SN
    LNF --> LN
    ASAF --> SA
    RSAF --> SA

    %% CN operations
    CN -->|"check image exists"| ACR
    CN -->|"generate SAS URL"| DBS
    CN -->|"k8s exec: download .bak + sqlcmd restore"| MSSQL
    CN -->|"create deployment, service, secret"| BC & LB & SEC
    CN -->|"trigger workflow (if image missing)"| GAS
    GAS -->|"JWT → installation token → dispatch"| GH_APP

    %% GitHub Actions
    GH_APP -->|"workflow_dispatch"| GH_ACTIONS
    GH_ACTIONS -->|"OIDC → push image"| ACR
    GH_ACTIONS -->|"upload .bak"| DBS

    %% Identity
    MI -.->|"auth"| CN & RN & SN & LN & SA
    MI -.->|"AKS Contributor"| BC
    MI -.->|"Blob Data Contributor"| DBS
    MI -.->|"AcrPull"| ACR

    %% BC → SQL
    BC -->|"TCP 1433"| MSSQL
    LB -->|"ports 80,443,7047-7049"| BC

    %% SQL external access
    SA -->|"create/delete service + policy"| SQL_LB & NP
    SQL_LB -->|"TCP 1433 (IP-restricted)"| MSSQL
    NP -.->|"allow CIDR"| MSSQL

    %% Monitoring
    CN & RN & SN & LN & SA -.-> LOGS
    FB -.-> LOGS
```

## Component Descriptions

### User Interfaces

| Component | Path | Description |
|-----------|------|-------------|
| **VS Code Extension** | `fkh-vsix/` | Registers commands to create/remove nodes. Uses VS Code's built-in GitHub auth to obtain a Bearer token and calls the Function App API. Fetches the function catalog for dynamic parameter prompts. Auto-detects public IP for SQL access. Parameter defaults can be set via `fkh.<Function>.<param>` settings. |
| **CLI Tool** | `fkh-cli/` | Standalone .NET executable (`fkh.exe`). Reads GitHub token from `GH_TOKEN`, `GITHUB_TOKEN`, or `gh auth token`. Interactively prompts for parameters with masked password input. Auto-detects public IP for SQL access. |

### Azure Functions Backend

| Component | Path | Description |
|-----------|------|-------------|
| **FunctionBase** | `fkh-backend/FunctionBase.cs` | Base class for all HTTP functions. Extracts Bearer token, validates it against GitHub API, checks team membership, parses and validates parameters against the function catalog, and injects the GitHub username. |
| **GitHubAuthService** | `fkh-backend/Services/GitHubAuthService.cs` | Calls `GET /user` and `GET /orgs/{org}/teams/{team}/memberships/{username}` to authenticate and authorize requests. Allowed org/team pairs loaded from `ALLOWED_ORG_TEAMS` env var. |
| **GitHubAppTokenService** | `fkh-backend/Services/GitHubAppTokenService.cs` | Creates JWTs signed with the GitHub App private key, exchanges for installation access tokens, and dispatches the `createImages` workflow when an image is missing from ACR. |
| **FkhCreateNode** | `fkh-backend/Services/FkhCreateNode.cs` | Orchestrates node creation: ACR image check → database backup SAS URL → k8s exec to download and restore database → create K8s deployment, service, and secret. |
| **FkhRemoveNode** | `fkh-backend/Services/FkhRemoveNode.cs` | Removes Kubernetes resources (deployment, service, secret) and drops the database for a given node. |
| **FkhScaleNode** | `fkh-backend/Services/FkhScaleNode.cs` | Scales a node's deployment: StopNode sets replicas to 0, StartNode sets replicas to 1. Database is preserved across stop/start. |
| **FkhListNodes** | `fkh-backend/Services/FkhListNodes.cs` | Lists nodes filtered by user (or all). Shows status, image, web client URL, and CPU/memory usage via the metrics API. |
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
| **Storage (DBS)** | `function.tf` | `fkh{customer}dbs` — holds database backup blobs in a `cronus` container, keyed by image tag. |
| **Storage (Func)** | `function.tf` | `fkh{customer}func` — Azure Functions runtime state (queues, tables). |
| **GitHub Team** | `github.tf` | Manages the authorized team within the GitHub organization. |
| **Monitoring** | `monitoring.tf` | Log Analytics workspace (30-day retention) + Application Insights for function telemetry. |

### GitHub Actions

| Workflow | Trigger | Description |
|----------|---------|-------------|
| **CreateImages** | `workflow_dispatch` (artifactUrls) | Downloads BC artifacts via `BcContainerHelper`, extracts and uploads the `.bak` database backup to blob storage, builds the container image with `New-BcImage`, and pushes to ACR. Authenticates to Azure via OIDC federated identity. |

## Authentication Flow

```mermaid
sequenceDiagram
    participant User as User (VS Code / CLI)
    participant Func as Azure Function
    participant GH as GitHub API
    participant AKS as AKS (k8s API)
    participant Blob as Blob Storage
    participant ACR as ACR

    User->>Func: POST /api/CreateNode (Bearer: gh_token)
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

    Func-->>User: Node created (FQDN, deployment info)
```

## Node Creation Flow

1. **Image check** — Verify the requested BC image exists in ACR. If missing, trigger the `createImages` GitHub Actions workflow via the GitHub App and return an error asking the user to retry.
2. **Database backup** — Generate a 1-hour read-only SAS URL for the `.bak` blob in the DBS storage account.
3. **Database existence check** — K8s exec into the mssql pod and run `sqlcmd` to verify the database doesn't already exist.
4. **Database restore** — K8s exec to `curl` the backup into the pod, then `sqlcmd` to `RESTORE DATABASE` with `MOVE` clauses for data/log files.
5. **Kubernetes resources** — Create a Secret (admin password), a Deployment (Windows pod with BC image and database env vars), and a LoadBalancer Service (public IP with Azure DNS label).
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
