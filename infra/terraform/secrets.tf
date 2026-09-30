# Secret Manager. Values are generated here, so they also live in the Terraform state:
# the state bucket must be private and versioned (see README.md).

resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

locals {
  db_host = google_sql_database_instance.main.private_ip_address

  connection_string_common = "Host=${local.db_host};Port=5432;Database=${google_sql_database.mrp.name};SSL Mode=Require;Maximum Pool Size=${var.db_max_pool_size}"

  secret_names = [
    "connection-app",
    "connection-migrator",
    "jwt-signing-key",
    "db-owner-password",
    "db-app-password",
  ]

  secrets = {
    # Read by the API service.
    "connection-app" = "${local.connection_string_common};Username=mrp_app;Password=${random_password.db_app.result}"
    # Read by the migrate job only.
    "connection-migrator" = "${local.connection_string_common};Username=${google_sql_user.owner.name};Password=${random_password.db_owner.result}"
    # Read by the API service and the migrate job.
    "jwt-signing-key" = random_password.jwt_signing_key.result
    # Read by humans running sql/01-roles.sql; no workload has access.
    "db-owner-password" = random_password.db_owner.result
    "db-app-password"   = random_password.db_app.result
  }
}

resource "google_secret_manager_secret" "this" {
  # Static names, so that the sensitive values never end up in a resource address.
  for_each = toset(local.secret_names)

  project   = var.project_id
  secret_id = "${local.prefix}-${each.key}"
  labels    = local.labels

  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }

  depends_on = [google_project_service.services]
}

resource "google_secret_manager_secret_version" "this" {
  for_each = google_secret_manager_secret.this

  secret      = each.value.id
  secret_data = local.secrets[each.key]
}
