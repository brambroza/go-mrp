# Cloud Run services and the migrate job.
#
# Ownership split:
#   Terraform owns the shape of a service (scaling, env vars, secrets, probes, networking, identity).
#   The deploy workflow owns WHICH image runs and HOW traffic is split.
# That is why image and traffic are in ignore_changes: a `terraform apply` never rolls the
# application back to the bootstrap image and never undoes a traffic rollback.

locals {
  # Settings shared by every container that runs the API image.
  api_env = merge(
    {
      ASPNETCORE_ENVIRONMENT = local.aspnetcore_environment
      Database__AppRole      = "mrp_app"
    },
    { for index, origin in local.cors_origins : "Cors__Origins__${index}" => origin },
  )
}

# ---------- API ----------

resource "google_cloud_run_v2_service" "api" {
  project             = var.project_id
  name                = "${local.prefix}-api"
  location            = var.region
  ingress             = var.ingress
  deletion_protection = var.deletion_protection
  labels              = local.labels

  template {
    service_account                  = google_service_account.api.email
    max_instance_request_concurrency = 80
    timeout                          = "60s"

    scaling {
      min_instance_count = var.api_min_instances
      max_instance_count = var.api_max_instances
    }

    # Direct VPC egress; only traffic to private ranges (Cloud SQL) goes through the VPC,
    # so no Cloud NAT is needed for calls to LINE, Expo Push, Cloudflare R2, ...
    vpc_access {
      egress = "PRIVATE_RANGES_ONLY"

      network_interfaces {
        network    = google_compute_network.main.id
        subnetwork = google_compute_subnetwork.run.id
      }
    }

    containers {
      name  = "api"
      image = var.api_image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = var.api_cpu
          memory = var.api_memory
        }
        # CPU is billed only while a request is being handled.
        cpu_idle          = true
        startup_cpu_boost = true
      }

      dynamic "env" {
        for_each = local.api_env

        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name = "ConnectionStrings__App"

        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.this["connection-app"].secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "Jwt__SigningKey"

        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.this["jwt-signing-key"].secret_id
            version = "latest"
          }
        }
      }

      startup_probe {
        initial_delay_seconds = 0
        period_seconds        = 5
        timeout_seconds       = 3
        failure_threshold     = 12

        http_get {
          path = "/healthz"
          port = 8080
        }
      }

      liveness_probe {
        period_seconds    = 30
        timeout_seconds   = 3
        failure_threshold = 3

        http_get {
          path = "/healthz"
          port = 8080
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.this,
    google_secret_manager_secret_iam_member.accessor,
  ]

  lifecycle {
    ignore_changes = [
      client,
      client_version,
      traffic,
      template[0].containers[0].image,
      template[0].labels,
      template[0].revision,
    ]
  }
}

# ---------- web ----------

resource "google_cloud_run_v2_service" "web" {
  project             = var.project_id
  name                = "${local.prefix}-web"
  location            = var.region
  ingress             = var.ingress
  deletion_protection = var.deletion_protection
  labels              = local.labels

  template {
    service_account                  = google_service_account.web.email
    max_instance_request_concurrency = 80
    timeout                          = "60s"

    scaling {
      min_instance_count = var.web_min_instances
      max_instance_count = var.web_max_instances
    }

    containers {
      name  = "web"
      image = var.web_image

      ports {
        container_port = 3000
      }

      resources {
        limits = {
          cpu    = var.web_cpu
          memory = var.web_memory
        }
        cpu_idle          = true
        startup_cpu_boost = true
      }

      # NEXT_PUBLIC_API_BASE_URL is inlined at image build time, so there is no runtime setting here.

      startup_probe {
        initial_delay_seconds = 0
        period_seconds        = 5
        timeout_seconds       = 3
        failure_threshold     = 12

        tcp_socket {
          port = 3000
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [
      client,
      client_version,
      traffic,
      template[0].containers[0].image,
      template[0].labels,
      template[0].revision,
    ]
  }
}

# ---------- public access ----------

resource "google_cloud_run_v2_service_iam_member" "public" {
  for_each = var.allow_unauthenticated ? {
    api = google_cloud_run_v2_service.api.name
    web = google_cloud_run_v2_service.web.name
  } : {}

  project  = var.project_id
  location = var.region
  name     = each.value
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# ---------- migrate job ----------

# Same image as the API, started with the "migrate" argument. The deploy workflow points the job
# at the new image, executes it and waits for success before any new API revision is deployed.
resource "google_cloud_run_v2_job" "migrate" {
  project             = var.project_id
  name                = "${local.prefix}-migrate"
  location            = var.region
  deletion_protection = false
  labels              = local.labels

  template {
    task_count  = 1
    parallelism = 1

    template {
      service_account = google_service_account.migrate.email
      # A failed migration must be looked at by a human, never retried blindly.
      max_retries = 0
      timeout     = "900s"

      vpc_access {
        egress = "PRIVATE_RANGES_ONLY"

        network_interfaces {
          network    = google_compute_network.main.id
          subnetwork = google_compute_subnetwork.run.id
        }
      }

      containers {
        name  = "migrate"
        image = var.api_image
        args  = ["migrate"]

        resources {
          limits = {
            cpu    = "1"
            memory = "512Mi"
          }
        }

        env {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = local.aspnetcore_environment
        }

        env {
          name  = "Database__AppRole"
          value = "mrp_app"
        }

        env {
          name = "ConnectionStrings__Migrator"

          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.this["connection-migrator"].secret_id
              version = "latest"
            }
          }
        }

        env {
          name = "Jwt__SigningKey"

          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.this["jwt-signing-key"].secret_id
              version = "latest"
            }
          }
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.this,
    google_secret_manager_secret_iam_member.accessor,
  ]

  lifecycle {
    ignore_changes = [
      client,
      client_version,
      template[0].labels,
      template[0].template[0].containers[0].image,
    ]
  }
}

# ---------- worker (planned) ----------

# NOT ACTIVE: the API has no worker mode yet. Enabling this today would only start a second copy
# of the HTTP API. Once `dotnet Mrp.Api.dll worker` (Hangfire server) exists:
#   1. confirm the argument name below,
#   2. set worker_enabled = true,
#   3. add the worker to the deploy workflow.
# Hangfire polls the database, so the worker needs one always-on instance with CPU always
# allocated. That is a fixed monthly cost; check the budget before enabling.
resource "google_cloud_run_v2_service" "worker" {
  count = var.worker_enabled ? 1 : 0

  project             = var.project_id
  name                = "${local.prefix}-worker"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_INTERNAL_ONLY"
  deletion_protection = var.deletion_protection
  labels              = local.labels

  template {
    service_account = google_service_account.worker[0].email

    scaling {
      min_instance_count = 1
      max_instance_count = 1
    }

    vpc_access {
      egress = "PRIVATE_RANGES_ONLY"

      network_interfaces {
        network    = google_compute_network.main.id
        subnetwork = google_compute_subnetwork.run.id
      }
    }

    containers {
      name  = "worker"
      image = var.api_image
      args  = ["worker"]

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        # Background jobs run outside of requests, so CPU must stay allocated.
        cpu_idle = false
      }

      dynamic "env" {
        for_each = local.api_env

        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name = "ConnectionStrings__App"

        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.this["connection-app"].secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "Jwt__SigningKey"

        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.this["jwt-signing-key"].secret_id
            version = "latest"
          }
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.this,
    google_secret_manager_secret_iam_member.accessor,
  ]

  lifecycle {
    ignore_changes = [
      client,
      client_version,
      traffic,
      template[0].containers[0].image,
      template[0].labels,
      template[0].revision,
    ]
  }
}
