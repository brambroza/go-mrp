# Root module of the MRP platform on GCP.
# One state per environment (selected with -backend-config), one tfvars file per environment.

data "google_project" "this" {
  project_id = var.project_id
}

locals {
  prefix = var.name_prefix

  labels = merge(
    {
      app         = "mrp"
      environment = var.environment
      managed-by  = "terraform"
    },
    var.labels,
  )

  github_environment = var.github_environment != "" ? var.github_environment : var.environment

  # ASP.NET Core environment name. Anything but "Development" makes the API refuse to start
  # when its database role could bypass Row-Level Security.
  aspnetcore_environment = var.environment == "production" ? "Production" : "Staging"

  # Cloud Run v2 has a deterministic URL, which lets the API allow the web origin
  # without a dependency cycle between the two services.
  web_default_url = "https://${local.prefix}-web-${data.google_project.this.number}.${var.region}.run.app"
  api_default_url = "https://${local.prefix}-api-${data.google_project.this.number}.${var.region}.run.app"

  cors_origins = length(var.cors_origins) > 0 ? var.cors_origins : compact([
    local.web_default_url,
    var.web_domain != "" ? "https://${var.web_domain}" : "",
  ])

  registry_path = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.docker.repository_id}"
}

# ---------- APIs ----------

resource "google_project_service" "services" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "cloudresourcemanager.googleapis.com",
    "compute.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "servicenetworking.googleapis.com",
    "sqladmin.googleapis.com",
    "sts.googleapis.com",
  ])

  project = var.project_id
  service = each.value

  # Destroying an environment must not switch APIs off for anything else in the project.
  disable_on_destroy = false
}

# ---------- Artifact Registry ----------

resource "google_artifact_registry_repository" "docker" {
  project       = var.project_id
  location      = var.region
  repository_id = local.prefix
  description   = "Container images of the MRP platform (${var.environment})"
  format        = "DOCKER"
  labels        = local.labels

  # Storage cost guard: always keep the newest 10 versions of every image,
  # delete everything else once it is older than 30 days.
  cleanup_policies {
    id     = "keep-recent"
    action = "KEEP"

    most_recent_versions {
      keep_count = 10
    }
  }

  cleanup_policies {
    id     = "delete-old"
    action = "DELETE"

    condition {
      older_than = "2592000s"
    }
  }

  depends_on = [google_project_service.services]
}
