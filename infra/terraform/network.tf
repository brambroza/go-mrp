# Private connectivity between Cloud Run and Cloud SQL.
#
# Cloud Run uses Direct VPC egress (no Serverless VPC Access connector): there is no
# always-on connector VM to pay for, and it is available for services and jobs in asia-southeast1.
# Cloud SQL gets a private IP through private services access (VPC peering), which is free.

resource "google_compute_network" "main" {
  project                 = var.project_id
  name                    = "${local.prefix}-vpc"
  auto_create_subnetworks = false
  routing_mode            = "REGIONAL"

  depends_on = [google_project_service.services]
}

resource "google_compute_subnetwork" "run" {
  project                  = var.project_id
  name                     = "${local.prefix}-run"
  region                   = var.region
  network                  = google_compute_network.main.id
  ip_cidr_range            = var.run_subnet_cidr
  private_ip_google_access = true
}

resource "google_compute_global_address" "private_services" {
  project       = var.project_id
  name          = "${local.prefix}-private-services"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = var.private_services_prefix_length
  network       = google_compute_network.main.id
}

resource "google_service_networking_connection" "private_services" {
  network                 = google_compute_network.main.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_services.name]

  depends_on = [google_project_service.services]
}
