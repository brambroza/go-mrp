# Optional custom domains. Created only when api_domain / web_domain are set.
# The domain must be verified for the project first, and the DNS records printed in the
# `domain_dns_records` output must be added in Cloudflare (see README.md).

resource "google_cloud_run_domain_mapping" "api" {
  count = var.api_domain != "" ? 1 : 0

  project  = var.project_id
  location = var.region
  name     = var.api_domain

  metadata {
    namespace = var.project_id
    labels    = local.labels
  }

  spec {
    route_name = google_cloud_run_v2_service.api.name
  }
}

resource "google_cloud_run_domain_mapping" "web" {
  count = var.web_domain != "" ? 1 : 0

  project  = var.project_id
  location = var.region
  name     = var.web_domain

  metadata {
    namespace = var.project_id
    labels    = local.labels
  }

  spec {
    route_name = google_cloud_run_v2_service.web.name
  }
}
