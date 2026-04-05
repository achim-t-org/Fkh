# Setting up a GitHub App for FK8s

FK8s uses a **per-customer GitHub App** to trigger the `createImages` workflow
automatically when a requested container image does not yet exist in ACR.

Each customer gets their own GitHub App so that private keys are isolated —
one customer's credentials can never trigger builds in another customer's
repository.

---

## 1. Create the GitHub App

1. Go to **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App**.
2. Fill in:
   | Field | Value |
   |---|---|
   | **GitHub App name** | `FK8s-<customer-name>` (must be globally unique) |
   | **Homepage URL** | `https://github.com/<org>/<repo>` |
   | **Webhook → Active** | **Unchecked** (no webhook needed) |
3. Under **Permissions → Repository permissions** set:
   | Permission | Access |
   |---|---|
   | **Actions** | Read & Write |
   | **Contents** | Read-only |
4. Under **Where can this GitHub App be installed?** select **Only on this account**.
5. Click **Create GitHub App**.
6. Note the **App ID** shown on the next page.

## 2. Generate a private key

1. On the App settings page, scroll to **Private keys**.
2. Click **Generate a private key** — a `.pem` file will download.
3. Store the PEM file securely (e.g. Azure Key Vault or a secrets manager).

## 3. Install the App on the repository

1. On the App settings page click **Install App** in the left sidebar.
2. Select your organisation / account.
3. Choose **Only select repositories** and pick the FK8s repository.
4. Click **Install**.
5. Note the **Installation ID** from the URL:
   `https://github.com/settings/installations/<INSTALLATION_ID>`.

## 4. Configure Terraform variables

Add these to your customer's `.tfvars` file:

```hcl
github_app_id              = "<app-id>"
github_app_installation_id = "<installation-id>"
```

Set the private key as an environment variable (never commit it):

```powershell
$env:TF_VAR_github_app_private_key = Get-Content "<path-to>.pem" -Raw
```

## 5. Deploy

Run the normal deploy:

```powershell
cd terraform
.\deploy.ps1 -VarFile customers/<customer-name>.tfvars
```

The Function App will receive the three new settings
(`GITHUB_APP_ID`, `GITHUB_APP_PRIVATE_KEY`, `GITHUB_APP_INSTALLATION_ID`)
and will automatically trigger the workflow when an image is not found in ACR.

---

## Security notes

- Each customer must have a **separate** GitHub App with its own private key.
- Private keys are stored in the Function App's configuration (encrypted at rest
  by Azure) and are never exposed to end users.
- The App only needs **Actions: Read & Write** and **Contents: Read-only** —
  no admin, no webhook, no org-level access.
