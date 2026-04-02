# ============================================================================
# Namespace
# ============================================================================

resource "kubernetes_namespace" "workload" {
  metadata {
    name = var.namespace
  }

  depends_on = [azurerm_kubernetes_cluster.this]
}

# ============================================================================
# Kubernetes Secret for SQL SA password
# ============================================================================

resource "kubernetes_secret" "mssql" {
  metadata {
    name      = "mssql-secret"
    namespace = kubernetes_namespace.workload.metadata[0].name
  }

  data = {
    "sa-password" = var.sql_sa_password
  }

  type = "Opaque"
}

# ============================================================================
# PersistentVolumeClaim for SQL Server data (Premium SSD, 128Gi)
# ============================================================================

resource "kubernetes_persistent_volume_claim" "mssql_data" {
  metadata {
    name      = "mssql-data-pvc"
    namespace = kubernetes_namespace.workload.metadata[0].name
  }

  spec {
    access_modes       = ["ReadWriteOnce"]
    storage_class_name = "managed-csi-premium"

    resources {
      requests = {
        storage = var.sql_storage_size
      }
    }
  }
}

# ============================================================================
# SQL Server Deployment on Linux
# ============================================================================

resource "kubernetes_deployment" "mssql" {
  metadata {
    name      = "mssql-deployment"
    namespace = kubernetes_namespace.workload.metadata[0].name
  }

  spec {
    replicas = 1

    selector {
      match_labels = {
        app = "mssql"
      }
    }

    template {
      metadata {
        labels = {
          app = "mssql"
        }
      }

      spec {
        node_selector = {
          "kubernetes.io/os" = "linux"
        }

        security_context {
          fs_group = 10001
        }

        # Init container to fix permissions on mounted volumes
        init_container {
          name  = "fix-permissions"
          image = "mcr.microsoft.com/mssql/server:2022-latest"

          command = ["/bin/bash", "-c", "chown -R 10001:0 /var/opt/mssql/data /var/opt/mssql/log"]

          security_context {
            run_as_user = 0
          }

          volume_mount {
            name       = "mssql-data"
            mount_path = "/var/opt/mssql/data"
            sub_path   = "data"
          }

          volume_mount {
            name       = "mssql-data"
            mount_path = "/var/opt/mssql/log"
            sub_path   = "log"
          }
        }

        container {
          name  = "mssql"
          image = "mcr.microsoft.com/mssql/server:2022-latest"

          port {
            container_port = 1433
            protocol       = "TCP"
          }

          env {
            name  = "ACCEPT_EULA"
            value = "Y"
          }

          env {
            name = "MSSQL_SA_PASSWORD"
            value_from {
              secret_key_ref {
                name = kubernetes_secret.mssql.metadata[0].name
                key  = "sa-password"
              }
            }
          }

          env {
            name  = "MSSQL_DATA_DIR"
            value = "/var/opt/mssql/data"
          }

          env {
            name  = "MSSQL_LOG_DIR"
            value = "/var/opt/mssql/log"
          }

          volume_mount {
            name       = "mssql-data"
            mount_path = "/var/opt/mssql/data"
            sub_path   = "data"
          }

          volume_mount {
            name       = "mssql-data"
            mount_path = "/var/opt/mssql/log"
            sub_path   = "log"
          }

          resources {
            requests = {
              memory = "2Gi"
              cpu    = "1"
            }
            limits = {
              memory = "4Gi"
              cpu    = "2"
            }
          }

          readiness_probe {
            tcp_socket {
              port = 1433
            }
            initial_delay_seconds = 15
            period_seconds        = 10
          }

          liveness_probe {
            tcp_socket {
              port = 1433
            }
            initial_delay_seconds = 30
            period_seconds        = 20
          }
        }

        volume {
          name = "mssql-data"
          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim.mssql_data.metadata[0].name
          }
        }
      }
    }
  }
}

# ============================================================================
# ClusterIP Service for SQL Server (internal only)
# ============================================================================

resource "kubernetes_service" "mssql" {
  metadata {
    name      = "mssql-service"
    namespace = kubernetes_namespace.workload.metadata[0].name
  }

  spec {
    type = "ClusterIP"

    selector = {
      app = "mssql"
    }

    port {
      protocol    = "TCP"
      port        = 1433
      target_port = 1433
    }
  }
}

# ============================================================================
# Network Policy: restrict SQL Server access to Windows app pods only
# ============================================================================

resource "kubernetes_network_policy" "mssql_allow_windows_app_only" {
  metadata {
    name      = "mssql-allow-windows-app-only"
    namespace = kubernetes_namespace.workload.metadata[0].name
  }

  spec {
    pod_selector {
      match_labels = {
        app = "mssql"
      }
    }

    policy_types = ["Ingress"]

    ingress {
      from {
        pod_selector {
          match_labels = {
            app = "windows-app"
          }
        }
      }

      ports {
        protocol = "TCP"
        port     = "1433"
      }
    }
  }
}
