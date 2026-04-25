# ── Deployment configuration ─────────────────────────────────────────────────────────────────
# Edit this file with your organization's values.
# Deploy by running the "Deploy Full Stack" workflow in GitHub Actions.
#
# Never commit secrets (sql_sa_password, github_app_private_key) to source control.
# Set them as GitHub Secrets in this repository instead.

#  _____             _                                  _     _   _                      
# |  __ \           | |                                | |   | \ | |                     
# | |  | | ___ _ __ | | ___  _   _ _ __ ___   ___ _ __ | |_  |  \| | __ _ _ __ ___   ___ 
# | |  | |/ _ \ '_ \| |/ _ \| | | | '_ ` _ \ / _ \ '_ \| __| | . ` |/ _` | '_ ` _ \ / _ \
# | |__| |  __/ |_) | | (_) | |_| | | | | | |  __/ | | | |_  | |\  | (_| | | | | | |  __/
# |_____/ \___| .__/|_|\___/ \__, |_| |_| |_|\___|_| |_|\__| |_| \_|\__,_|_| |_| |_|\___|
#             | |             __/ |                                                      
#             |_|            |___/                                                       
#
# Deployment name — a short identifier for this Fkh deployment.
# Used as a prefix for all Azure resource names (e.g. fkh-contoso-aks, fkh-contoso-backend).
# Needs to be lower case, letters and numbers only
fkhDeploymentName = "myorg"


#                                   _____      _   _   _                 
#     /\                           / ____|    | | | | (_)                
#    /  \    _____   _ _ __ ___   | (___   ___| |_| |_ _ _ __   __ _ ___ 
#   / /\ \  |_  / | | | '__/ _ \   \___ \ / _ \ __| __| | '_ \ / _` / __|
#  / ____ \  / /| |_| | | |  __/   ____) |  __/ |_| |_| | | | | (_| \__ \
# /_/    \_\/___|\__,_|_|  \___|  |_____/ \___|\__|\__|_|_| |_|\__, |___/
#                                                               __/ |    
#                                                              |___/     
# Azure — the subscription, tenant, and region where all Fkh infrastructure
# (AKS cluster, Function App, Container Registry, storage, etc.) will be created.
# Get these from the Azure Portal or by running:
#   az account show --query '{subscription_id:id, tenant_id:tenantId}' -o table
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"   # Azure region (e.g. westeurope, eastus, swedencentral)
state_location  = ""             # Azure region for the Terraform state resource group and storage account. Leave empty to use 'location'.


#  _  __     _                          _                _____      _   _   _                 
# | |/ /    | |                        | |              / ____|    | | | | (_)                
# | ' /_   _| |__   ___ _ __ _ __   ___| |_ ___  ___   | (___   ___| |_| |_ _ _ __   __ _ ___ 
# |  <| | | | '_ \ / _ \ '__| '_ \ / _ \ __/ _ \/ __|   \___ \ / _ \ __| __| | '_ \ / _` / __|
# | . \ |_| | |_) |  __/ |  | | | |  __/ ||  __/\__ \   ____) |  __/ |_| |_| | | | | (_| \__ \
# |_|\_\__,_|_.__/ \___|_|  |_| |_|\___|\__\___||___/  |_____/ \___|\__|\__|_|_| |_|\__, |___/
#                                                                                    __/ |    
#                                                                                   |___/     
aks_sku_tier                 = "Free"             # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)
linux_vm_size                = "Standard_D4s_v5"  # v6 not supported for sqlserver
windows_vm_size              = "Standard_D4s_v5"  # v6 not supported for hyhervisor gen1
windows_min_node_count       = 0                  # Set to 1 to keep a warm Windows node (~$70-100/mo)
windows_max_node_count       = 10                 # Maximum Windows nodes the autoscaler can scale to
windows_overprovision        = false              # Set to true to keep spare capacity for instant container scheduling
windows_overprovision_cpu    = "250m"             # CPU reserved by the overprovision placeholder
windows_overprovision_memory = "3Gi"              # Memory reserved by the overprovision placeholder
container_default_cpu        = "250m"             # Default CPU request for BC containers
container_default_memory     = "3Gi"              # Default memory request for BC containers
windows_spot_enabled         = false              # Set to true to add a Spot pricing Windows pool for lower cost
windows_spot_vm_size         = "Standard_D2ds_v5" # VM size for spot nodes
windows_spot_min_node_count  = 0                  # Minimum spot nodes (0 = scale to zero when idle)
windows_spot_max_node_count  = 10                 # Maximum spot nodes the autoscaler can scale to

# Images to pre-pull on Windows nodes (speeds up container creation)
windows_prepull_images = [
  # "<acr-name>.azurecr.io/businesscentral:<tag>"
]

# Kubecost — free per-pod cost analysis dashboard
# Needs minimum D4s Linux VM Size to enable
kubecost_enabled = false

# SQL Server
# sql_sa_password = ""  # set as GitHub Secret: SQL_SA_PASSWORD
namespace        = "app"
sql_storage_size = "128Gi"


#   _____            _        _                     _____      _   _   _                 
#  / ____|          | |      (_)                   / ____|    | | | | (_)                
# | |     ___  _ __ | |_ __ _ _ _ __   ___ _ __   | (___   ___| |_| |_ _ _ __   __ _ ___ 
# | |    / _ \| '_ \| __/ _` | | '_ \ / _ \ '__|   \___ \ / _ \ __| __| | '_ \ / _` / __|
# | |___| (_) | | | | || (_| | | | | |  __/ |      ____) |  __/ |_| |_| | | | | (_| \__ \
#  \_____\___/|_| |_|\__\__,_|_|_| |_|\___|_|     |_____/ \___|\__|\__|_|_| |_|\__, |___/
#                                                                               __/ |    
#                                                                              |___/     
# Contact email for Let's Encrypt
contact_email_for_letsencrypt = "admin@example.com"

# AAD container authentication — allows users to sign in with Microsoft 365 accounts
# Requires the deployment identity to have the Privileged Role Administrator directory role in Entra ID.
# See Installation/Step1-AzureIdentity.md for details.
enable_aad_container_auth = false


#   _____ _ _   _    _       _         _____      _   _   _                 
#  / ____(_) | | |  | |     | |       / ____|    | | | | (_)                
# | |  __ _| |_| |__| |_   _| |__    | (___   ___| |_| |_ _ _ __   __ _ ___ 
# | | |_ | | __|  __  | | | | '_ \    \___ \ / _ \ __| __| | '_ \ / _` / __|
# | |__| | | |_| |  | | |_| | |_) |   ____) |  __/ |_| |_| | | | | (_| \__ \
#  \_____|_|\__|_|  |_|\__,_|_.__/   |_____/ \___|\__|\__|_|_| |_|\__, |___/
#                                                                  __/ |    
#                                                                 |___/     
# Authentication — GitHub is used to determine who the user is
# Authorization — GitHub teams that control who can use this Fkh deployment.
# These teams must already exist in your GitHub organization (see Step 4 of the installation guide).
# Values are case-sensitive.

# Member teams — users in these teams can provision containers
allowed_org_teams = [
  { org = "my-company",    team = "Fkh-members" },
  { org = "partner-org",   team = "Fkh-members" }
]

# Admin teams — members get admin access (and also have normal access)
admin_org_teams = [
  { org = "my-company",    team = "Fkh-admins" }
]

# Repositories — GitHub repos allowed to call Fkh via OIDC from GitHub Actions
# Please note that the AUTH token provided must be the ID token
allowed_oidc_repos = [
  # "my-company/my-bc-app"
]

# GitHub App — triggers image-build workflows in this deployment repo
github_app_id              = "1234567"  # paste your App ID here
# github_app_private_key   = ""  # set as GitHub Secret: GH_APP_PRIVATE_KEY
github_app_installation_id = "123456789"  # paste your Installation ID here


#  _    _                   _____      _   _   _                 
# | |  | |                 / ____|    | | | | (_)                
# | |  | |___  ___ _ __   | (___   ___| |_| |_ _ _ __   __ _ ___ 
# | |  | / __|/ _ \ '__|   \___ \ / _ \ __| __| | '_ \ / _` / __|
# | |__| \__ \  __/ |      ____) |  __/ |_| |_| | | | | (_| \__ \
#  \____/|___/\___|_|     |_____/ \___|\__|\__|_|_| |_|\__, |___/
#                                                       __/ |    
#                                                      |___/     
# Default user settings (deployed to settings/usersettings.json in storage)
# _members = defaults for all users, _admins = defaults for admin users
default_user_settings = <<-EOT
  {
    "_members": {
      "MaxContainers": 3
    },
    "_admins": {
      "MaxContainers": 10
    }
  }
EOT
