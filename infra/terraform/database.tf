# Cloud SQL for PostgreSQL 17.
#
# Role model (identical to infra/docker/postgres/init/01-roles.sh):
#   mrp_owner - owns the database and every schema, used ONLY by the migrate job
#   mrp_app   - used by the API, subject to Row-Level Security
#
# Terraform creates mrp_owner, because a login is needed to bootstrap the rest.
# Terraform does NOT create mrp_app: every user created through the Cloud SQL API
# (google_sql_user) becomes a member of "cloudsqlsuperuser" with CREATEROLE and CREATEDB,
# and that cannot be switched off from Terraform. mrp_app is created as a plain role by
# sql/01-roles.sql (run once per environment, see README.md). Terraform only generates its
# password and stores it in Secret Manager.

# Instance names cannot be reused for about a week after deletion, hence the random suffix.
resource "random_id" "db_suffix" {
  byte_length = 2
}

resource "google_sql_database_instance" "main" {
  project          = var.project_id
  name             = "${local.prefix}-pg-${random_id.db_suffix.hex}"
  region           = var.region
  database_version = "POSTGRES_17"

  # Terraform-level guard; the API-level guard is settings.deletion_protection_enabled.
  deletion_protection = var.deletion_protection

  settings {
    # Shared-core tiers only exist in the Enterprise edition. It has to be set explicitly,
    # because newer PostgreSQL versions default to Enterprise Plus.
    edition           = "ENTERPRISE"
    tier              = var.db_tier
    availability_type = var.db_availability_type
    activation_policy = var.db_activation_policy

    disk_type             = "PD_SSD"
    disk_size             = var.db_disk_size_gb
    disk_autoresize       = true
    disk_autoresize_limit = var.db_disk_autoresize_limit_gb

    deletion_protection_enabled = var.deletion_protection
    user_labels                 = local.labels

    ip_configuration {
      ipv4_enabled    = var.db_public_ip_enabled
      private_network = google_compute_network.main.id
      ssl_mode        = "ENCRYPTED_ONLY"
    }

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
      transaction_log_retention_days = var.db_pitr_retention_days
      # 19:00 UTC = 02:00 Asia/Bangkok
      start_time = "19:00"

      backup_retention_settings {
        retained_backups = var.db_backup_retained_count
        retention_unit   = "COUNT"
      }
    }

    maintenance_window {
      # Sunday 20:00 UTC = Monday 03:00 Asia/Bangkok
      day          = 7
      hour         = 20
      update_track = "stable"
    }

    insights_config {
      query_insights_enabled  = true
      record_application_tags = false
      record_client_address   = false
    }
  }

  depends_on = [google_service_networking_connection.private_services]

  lifecycle {
    precondition {
      condition     = var.environment != "production" || var.deletion_protection
      error_message = "deletion_protection must be true for the production environment."
    }
  }
}

resource "google_sql_database" "mrp" {
  project  = var.project_id
  instance = google_sql_database_instance.main.name
  name     = "mrp"

  # Never drop the database from Terraform; removing it from state is the only effect of a destroy.
  deletion_policy = "ABANDON"
}

# Passwords avoid special characters so that they need no escaping in a connection string.
resource "random_password" "db_owner" {
  length  = 40
  special = false
}

resource "random_password" "db_app" {
  length  = 40
  special = false
}

resource "google_sql_user" "owner" {
  project  = var.project_id
  instance = google_sql_database_instance.main.name
  name     = "mrp_owner"
  password = random_password.db_owner.result

  # The role owns the schema; dropping it would fail and must never happen implicitly.
  deletion_policy = "ABANDON"
}
