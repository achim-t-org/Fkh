# End User Setup

End users only need VS Code. No Azure CLI, no Terraform, no Kubernetes knowledge.

## Install the FKH Extension

Install from the VS Code Marketplace:
1. Open VS Code
2. Go to **Extensions** (Ctrl+Shift+X)
3. Search for **FKH**
4. Click **Install**

Alternatively, install from a `.vsix` file provided by your ops team:
1. **Ctrl+Shift+P** → **Extensions: Install from VSIX...** → select the file

## Configure the Backend URL

1. **Ctrl+Shift+P** → **Preferences: Open Settings (JSON)**
2. Add:

```json
{
  "fkh.backendUrl": "https://fkh-<org>-backend.azurewebsites.net/api"
}
```

Your ops team will provide the exact URL.

## Sign In

The first time you run a command, VS Code prompts you to sign in with GitHub. Grant the `read:user` and `read:org` scopes.

You must be a member of the authorized GitHub team (configured in the tfvars file).

## Create a Container

1. Open your AL-Go repository in VS Code
2. In the **FKH** sidebar, find your project under **AL-Go Projects**
3. Click the **Create Container** icon next to the project

Or: **Ctrl+Shift+P** → **FKH: Create Container**

The extension reads your AL-Go settings, resolves the artifact URL, and provisions a BC environment.

## Manage Containers

In the **FKH** sidebar:
- **Containers** — lists all your containers with status, WebClient link, and resource usage
- **Start/Stop** — click the icons to scale containers up/down (database is preserved)
- **Remove** — deletes the container and its database

## CLI Alternative

Install as a global .NET tool:

```powershell
dotnet tool install -g fkh
```

Configure the backend URL (one-time):

```powershell
# Option 1: Environment variable
$env:FKH_BACKEND_URL = "https://fkh-<org>-backend.azurewebsites.net/api"

# Option 2: Settings file (persistent)
New-Item -ItemType Directory -Path ~/.fkh -Force
@'{ "backendUrl": "https://fkh-<org>-backend.azurewebsites.net/api" }'@ | Set-Content ~/.fkh/settings.json
```

Usage:

```powershell
fkh createcontainer --name mybc --artifactUrl "https://..." --adminUsername admin --adminPassword "P@ssword1"
fkh listcontainers
fkh stopcontainer --name mybc
fkh startcontainer --name mybc
fkh removecontainer --name mybc
```

The CLI uses `gh auth token` for authentication. Sign in with `gh auth login` first.

## Access Permissions

| Role | What you can do |
|------|----------------|
| **Member** (Fkh-members team) | Create, start, stop, remove your own containers. View your containers and images. |
| **Admin** (Fkh-admins team) | All of the above + view all containers + view VMs + list all users' containers |
