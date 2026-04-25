# Step 3 — Create the GitHub App

> **Performed by:** GitHub Organization Administrator

> **Already have a GitHub App from another Fkh deployment?** You can reuse the same GitHub App and private key. Just install the app on your new deployment repository (step 3.3) and note the new **Installation ID**. You do not need to create a new app or generate a new private key. Skip ahead to [step 3.3](#33--install-the-app-on-your-deployment-repository).

The GitHub App allows the Fkh backend to automatically trigger image-build workflows when a requested Business Central image is not yet in the Azure Container Registry. The deployment workflow also uses it to sync secrets back to the deployment repository — replacing the need for a Personal Access Token (PAT).

## What the GitHub App does

When a user creates a container and the required BC image doesn't exist in ACR, the Fkh backend uses the GitHub App to dispatch the `CreateImages` workflow in your deployment repository. That workflow builds the image and pushes it to ACR.

During deployment, the GitHub App token is also used to write GitHub Actions secrets (such as Azure credentials) back to your deployment repository so that other workflows (CreateImages, etc.) can authenticate.

The app needs three repository permissions:

| Permission | Access | Why |
|-----------|--------|-----|
| **Actions** | Read & Write | Dispatch the `CreateImages` workflow |
| **Contents** | Read-only | Read workflow files from the repository |
| **Secrets** | Read & Write | Sync deployment outputs (Azure credentials, ACR server) to repo secrets |

No organization permissions, no user permissions, no webhooks.

## 3.1 — Create the GitHub App

1. Go to **GitHub** → your organization → **Settings** → **Developer settings** → **GitHub Apps** → **New GitHub App**.
2. Fill in:

| Field | Value |
|-------|-------|
| **App name** | `Fkh-<your-org-name>` (must be globally unique across GitHub) |
| **Homepage URL** | `https://github.com/<your-org>/Fkh` |
| **Webhook → Active** | **Uncheck** this checkbox |

3. Under **Repository permissions**, set:
   - **Actions**: Read & Write
   - **Contents**: Read-only
   - **Secrets**: Read & Write
4. Leave all other permissions at "No access".
5. Under **Where can this GitHub App be installed?** → select **Only on this account**.
6. Click **Create GitHub App**.
7. On the app settings page, note the **App ID** shown near the top.

## 3.2 — Generate a private key

The private key is how the Fkh backend authenticates as the GitHub App to obtain short-lived installation tokens.

1. On the App settings page, scroll down to **Private keys**.
2. Click **Generate a private key**.
3. A `.pem` file is downloaded automatically — **store it securely**. You will need it when configuring GitHub Secrets in a later step.

> **Security:** The private key is equivalent to the app's identity. Anyone who has it can act as the app. Do not commit it to source control. Store it in a password manager or secure vault.

## 3.3 — Install the app on your deployment repository

Install the GitHub App on your **private deployment repository** (created in Step 1). The `CreateImages` workflow runs here, alongside your other deployment workflows.

1. On the App page, click **Install App** in the left sidebar.
2. Select your organization.
3. Choose **Only select repositories** → select your **deployment repository** (e.g. `my-company/fkh-deploy-contoso`).
4. Click **Install**.
5. After installation, you are redirected to a URL like `https://github.com/organizations/<org>/settings/installations/<ID>`. Note the **Installation ID** from the URL.

> **Note:** Image builds typically take ~30 minutes on Windows runners. Since the deployment repo is private, these builds count against your organization's GitHub Actions minutes.

## 3.4 — Save your values

Record these values — you will need them when configuring the environment file and GitHub Secrets.

| Value | Where to find it |
|-------|-----------------|
| **App ID** | GitHub App settings page → near the top |
| **Installation ID** | From the URL after installing: `https://github.com/organizations/<org>/settings/installations/<ID>` |
| **Private Key** | The `.pem` file downloaded in step 3.2 |

---

*Previous: [Step 2 — Create the Azure Deployment Identity](Step2-AzureIdentity.md)*
*Next: [Step 4 — Set Up GitHub Teams](Step4-GitHubTeams.md)*
