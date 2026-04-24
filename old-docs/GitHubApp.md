# Create the GitHub App

The GitHub App lets the Fkh backend automatically trigger image-build workflows when a BC image is missing from ACR.

## Required GitHub Permissions

The person performing this step needs **Owner** access on the GitHub organization (or admin on the target repository).

## 1. Create the App

1. Go to **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App**
2. Fill in:

| Field | Value |
|-------|-------|
| **App name** | `Fkh-<your-org-name>` (must be globally unique) |
| **Homepage URL** | `https://github.com/<your-org>/Fkh` |
| **Webhook → Active** | **Uncheck** this |

3. Under **Repository permissions**:

| Permission | Access |
|-----------|--------|
| **Actions** | Read & Write |
| **Contents** | Read-only |

No other permissions needed. No org permissions, no user permissions.

4. Under **Where can this be installed?** → **Only on this account**
5. Click **Create GitHub App**
6. **Save the App ID** shown on the page

## 2. Generate a Private Key

1. On the App settings page → scroll to **Private keys**
2. Click **Generate a private key**
3. A `.pem` file downloads — store it securely

## 3. Install the App

1. On the App page → **Install App** (left sidebar)
2. Select your organization
3. Choose **Only select repositories** → pick your Fkh fork
4. Click **Install**
5. **Save the Installation ID** from the URL: `https://github.com/settings/installations/<ID>`

## Values to Save

You'll need these three values for [Configure Your Environment](ConfigureEnvironment.md):

| Value | Where |
|-------|-------|
| **App ID** | App settings page, top section |
| **Installation ID** | URL after installing the app |
| **Private Key** | The `.pem` file you downloaded |
