# Service accounts. Every workload has its own identity and can read only its own secrets.

resource "google_service_account" "api" {
  project      = var.project_id
  account_id   = "${local.prefix}-api"
  display_name = "MRP API runtime (${var.environment})"

  depends_on = [google_project_service.services]
}

resource "google_service_account" "web" {
  project      = var.project_id
  account_id   = "${local.prefix}-web"
  display_name = "MRP web runtime (${var.environment})"

  depends_on = [google_project_service.services]
}

resource "google_service_account" "migrate" {
  project      = var.project_id
  account_id   = "${local.prefix}-migrate"
  display_name = "MRP migrate job runtime (${var.environment})"

  depends_on = [google_project_service.services]
}

resource "google_service_account" "worker" {
  count = var.worker_enabled ? 1 : 0

  project      = var.project_id
  account_id   = "${local.prefix}-worker"
  display_name = "MRP worker runtime (${var.environment})"

  depends_on = [google_project_service.services]
}

resource "google_service_account" "deployer" {
  project      = var.project_id
  account_id   = "${local.prefix}-deployer"
  display_name = "MRP deployer for GitHub Actions (${var.environment})"

  depends_on = [google_project_service.services]
}

# ---------- secret access ----------

locals {
  # workload => secrets it may read. The web service reads no secret at all.
  secret_access = merge(
    {
      "api/connection-app"          = { member = google_service_account.api.member, secret = "connection-app" }
      "api/jwt-signing-key"         = { member = google_service_account.api.member, secret = "jwt-signing-key" }
      "migrate/connection-migrator" = { member = google_service_account.migrate.member, secret = "connection-migrator" }
      "migrate/jwt-signing-key"     = { member = google_service_account.migrate.member, secret = "jwt-signing-key" }
    },
    var.worker_enabled ? {
      "worker/connection-app"  = { member = google_service_account.worker[0].member, secret = "connection-app" }
      "worker/jwt-signing-key" = { member = google_service_account.worker[0].member, secret = "jwt-signing-key" }
    } : {},
  )
}

resource "google_secret_manager_secret_iam_member" "accessor" {
  for_each = local.secret_access

  project   = var.project_id
  secret_id = google_secret_manager_secret.this[each.value.secret].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = each.value.member
}

# ---------- deployer (GitHub Actions) ----------

# Push images to this repository only.
resource "google_artifact_registry_repository_iam_member" "deployer_writer" {
  project    = var.project_id
  location   = google_artifact_registry_repository.docker.location
  repository = google_artifact_registry_repository.docker.name
  role       = "roles/artifactregistry.writer"
  member     = google_service_account.deployer.member
}

# Deploy revisions, move traffic, update and execute jobs.
# roles/run.developer cannot change IAM policies (it cannot make a service public or private).
resource "google_project_iam_member" "deployer_run" {
  project = var.project_id
  role    = "roles/run.developer"
  member  = google_service_account.deployer.member
}

# Deploying a revision requires permission to act as the revision's runtime service account.
# Granted per service account, not on the project.
resource "google_service_account_iam_member" "deployer_act_as" {
  for_each = merge(
    {
      api     = google_service_account.api.name
      web     = google_service_account.web.name
      migrate = google_service_account.migrate.name
    },
    var.worker_enabled ? { worker = google_service_account.worker[0].name } : {},
  )

  service_account_id = each.value
  role               = "roles/iam.serviceAccountUser"
  member             = google_service_account.deployer.member
}

# ---------- Workload Identity Federation (no JSON keys) ----------

resource "google_iam_workload_identity_pool" "github" {
  project                   = var.project_id
  workload_identity_pool_id = "${local.prefix}-github"
  display_name              = "GitHub Actions"
  description               = "OIDC identities of GitHub Actions workflows"

  depends_on = [google_project_service.services]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  project                            = var.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github"
  display_name                       = "GitHub OIDC"

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }

  attribute_mapping = {
    "google.subject"        = "assertion.sub"
    "attribute.repository"  = "assertion.repository"
    "attribute.environment" = "assertion.environment"
    "attribute.ref"         = "assertion.ref"
  }

  # Tokens are accepted only from this repository AND only from jobs that run in the
  # matching GitHub environment. For production that environment has required reviewers,
  # so an unreviewed job can never obtain production credentials.
  attribute_condition = "assertion.repository == \"${var.github_repository}\" && assertion.environment == \"${local.github_environment}\""
}

resource "google_service_account_iam_member" "deployer_wif" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"
}
