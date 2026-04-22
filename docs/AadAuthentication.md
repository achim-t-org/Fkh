# AAD Authentication for Containers

By default, containers use **NavUserPassword** authentication (username + password). You can optionally enable **Azure AD (AAD) authentication** so users sign in with their Microsoft 365 / Entra ID accounts.

## Overview

AAD authentication works out of the box — no manual App Registration setup required. When a container is created with `authenticationEmail`, the Function automatically:

1. Creates a dedicated AAD App Registration for that container
2. Configures the correct redirect URI and token settings
3. Passes the AAD app details to the container as environment variables
4. Deletes the App Registration when the container is removed

The managed identity authenticates to the Microsoft Graph API using `Application.ReadWrite.OwnedBy`, which limits it to managing only apps it created.

---

## Using AAD Authentication

Pass `authenticationEmail` when creating a container:

- **VS Code extension**: Set the `authenticationEmail` parameter to your email address
- **CLI**: `fkh create --authenticationEmail user@contoso.com ...`
- **API**: Include `"authenticationEmail": "user@contoso.com"` in the request body

The container's web client URL will use the `/BC/SignIn` path for AAD login instead of the default username/password login.

---

## How It Works

1. When `authenticationEmail` is provided, the Function creates a new AAD App Registration named `fkh-<container>-auth` with a redirect URI of `https://<container-fqdn>/BC/SignIn`.
2. The app's client ID is stored as a Kubernetes annotation (`fkh/aad-app-object-id`) on the deployment so it can be cleaned up later.
3. The container receives `AadAppId`, `AadAppRedirectUri`, `AadTenantId`, and `authenticationEMail` as environment variables.
4. Users sign in via the standard Microsoft login flow in the browser.
5. When the container is removed, the Function deletes the AAD App Registration automatically.

---

## Prerequisites

The Function's managed identity needs the `Application.ReadWrite.OwnedBy` Microsoft Graph permission. This is granted automatically by Terraform during deployment — no manual steps required.

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| "Failed to create AAD App Registration" | Managed identity lacks Graph permissions | Re-run `terraform apply` to ensure the `Application.ReadWrite.OwnedBy` role is assigned |
| "Insufficient privileges to complete the operation" | Same as above | Check the managed identity's Graph API permissions in Azure Portal → Enterprise Applications |
