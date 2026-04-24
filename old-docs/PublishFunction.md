# Publish the FKH Backend

> This page covers **Path B** (local deployment). If you're using **Path A** (GitHub Actions), the Deploy workflow publishes the function automatically. For code-only updates, run the **Deploy Function Update** workflow instead.

> **Note:** If you used `deploy.ps1`, it already published the function code. This page is only needed for subsequent code-only updates.

To publish updated backend code without re-running the full infrastructure deploy:

## Publish

From the `terraform/` directory:

```powershell
.\deploy-functionupdate.ps1
```

This script:
1. Reads `function_app_name` and `resource_group_name` from Terraform output
2. Builds the `fkh-backend` project
3. Publishes it to the Function App via `func azure functionapp publish`

You can also specify explicitly:

```powershell
.\deploy-functionupdate.ps1 -FunctionAppName fkh-mycompany-functions
```

## Verify

Check that the function is running:

```powershell
# Get the function catalog (no auth required)
$baseUrl = terraform output -raw function_url
Invoke-RestMethod "$baseUrl"
```

You should see a JSON response listing all available functions (CreateContainer, ListContainers, etc.).

## When to Re-publish

Re-run `deploy-functionupdate.ps1` whenever you:
- Change code in `fkh-backend/`
- Pull updates from upstream
- Fix bugs in the backend

Terraform `apply` does **not** redeploy function code — only infrastructure changes.
