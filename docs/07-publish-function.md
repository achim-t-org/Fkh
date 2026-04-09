# Step 7: Publish the FKH Backend

Terraform creates the Function App infrastructure but doesn't deploy the code. You need to publish the .NET function code separately.

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
$baseUrl = terraform output -raw function_app_url
Invoke-RestMethod "$baseUrl/api/functions"
```

You should see a JSON response listing all available functions (CreatePod, ListPods, etc.).

## When to Re-publish

Re-run `deploy-functionupdate.ps1` whenever you:
- Change code in `fkh-backend/`
- Pull updates from upstream
- Fix bugs in the backend

Terraform `apply` does **not** redeploy function code — only infrastructure changes.
