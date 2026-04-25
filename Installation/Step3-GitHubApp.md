# Step 3 — Create the GitHub App

> **Performed by:** GitHub Organization Administrator

In this step you create and install a GitHub App for Fkh.

The GitHub App is used for two things:

- The Fkh backend uses it to trigger image-build workflows when a requested Business Central image is missing from Azure Container Registry.
- The deployment workflow uses it to sync deployment outputs back to the deployment repository as GitHub Actions secrets.

> **Already have a GitHub App from another Fkh deployment?** You can reuse it. Install the existing app on the new deployment repository, note the new **Installation ID**, and continue from [3.4 — Save your values](#34--save-your-values).

## Required permissions

The app needs repository permissions only. It does not need organization permissions, user permissions, or webhooks.

| Permission | Access | Why it is needed |
|---|---|---|
| Actions | Read & Write | Dispatch the `CreateImages` workflow from the Fkh backend, and used by the deployment workflow when syncing outputs |
| Secrets | Read & Write | Sync deployment outputs to repository secrets at the end of `Deploy Full Stack` |

---

## 3.1 — Create the GitHub App

1. In GitHub, open your organization.
2. Go to **Settings** → **Developer settings** → **GitHub Apps**.
3. Select **New GitHub App**.
4. Fill in the basic settings:

| Field | Value |
|---|---|
| App name | `Fkh-<your-org-name>`; this must be globally unique on GitHub |
| Homepage URL | `https://github.com/<your-org>/Fkh` |
| Webhook → Active | Uncheck this box |

5. Under **Repository permissions**, set:

| Permission | Access |
|---|---|
| Actions | Read & Write |
| Secrets | Read & Write |

6. Leave all other permissions set to **No access**.
7. Under **Where can this GitHub App be installed?**, select **Only on this account**.
8. Select **Create GitHub App**.
9. On the app settings page, copy the **App ID** shown near the top.

## 3.2 — Generate a private key

The private key allows Fkh to authenticate as the GitHub App and request short-lived installation tokens.

1. On the GitHub App settings page, scroll to **Private keys**.
2. Select **Generate a private key**.
3. Save the downloaded `.pem` file securely.

> **Security note:** the private key represents the app's identity. Do not commit it to source control. Store it in a password manager or secure vault. You will add it as a GitHub Secret in Step 5.

## 3.3 — Install the app on the deployment repository

Install the app on the private deployment repository you created in Step 1.

1. On the GitHub App page, select **Install App** in the left sidebar.
2. Select your organization.
3. Choose **Only select repositories**.
4. Select your deployment repository, for example `my-company/fkh-deploy-contoso`.
5. Select **Install**.
6. After installation, GitHub redirects you to a URL like this:

   ```text
   https://github.com/organizations/<org>/settings/installations/<ID>
   ```

7. Copy the final number in the URL. This is the **Installation ID**.

> **Note:** image builds can take around 30 minutes on Windows runners. Because the deployment repository is private, these builds count against your organization's GitHub Actions minutes.

## 3.4 — Save your values

Record these values for Step 5:

| Value | Where to find it |
|---|---|
| App ID | GitHub App settings page, near the top |
| Installation ID | The number at the end of the installation URL |
| Private Key | The `.pem` file downloaded in 3.2 |

## Step complete

The GitHub App is now ready to be referenced in `deployment.tfvars` and GitHub Secrets.

---

*Previous: [Step 2 — Create the Azure Deployment Identity](Step2-AzureIdentity.md)*  
*Next: [Step 4 — Set Up GitHub Teams](Step4-GitHubTeams.md)*
