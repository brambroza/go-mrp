output "api_url" {
  description = "run.app URL of the API service."
  value       = google_cloud_run_v2_service.api.uri
}

output "web_url" {
  description = "run.app URL of the web service."
  value       = google_cloud_run_v2_service.web.uri
}

output "api_default_url" {
  description = "Deterministic URL of the API service (known before the service exists)."
  value       = local.api_default_url
}

output "cors_origins" {
  description = "Origins the API accepts."
  value       = local.cors_origins
}

output "migrate_job_name" {
  description = "Name of the Cloud Run job that applies database migrations."
  value       = google_cloud_run_v2_job.migrate.name
}

output "registry_path" {
  description = "Artifact Registry path; images are <registry_path>/<prefix>-api:<sha> and <registry_path>/<prefix>-web:<sha>."
  value       = local.registry_path
}

output "wif_provider_name" {
  description = "Workload Identity Federation provider; value of the GitHub variable GCP_WORKLOAD_IDENTITY_PROVIDER."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "deployer_service_account_email" {
  description = "Deployer service account; value of the GitHub variable GCP_DEPLOYER_SERVICE_ACCOUNT."
  value       = google_service_account.deployer.email
}

output "runtime_service_accounts" {
  description = "Runtime service account of every workload."
  value = {
    api     = google_service_account.api.email
    web     = google_service_account.web.email
    migrate = google_service_account.migrate.email
  }
}

output "sql_instance_name" {
  description = "Cloud SQL instance name."
  value       = google_sql_database_instance.main.name
}

output "sql_instance_connection_name" {
  description = "Cloud SQL connection name for the Cloud SQL Auth Proxy (project:region:instance)."
  value       = google_sql_database_instance.main.connection_name
}

output "sql_private_ip" {
  description = "Private IP address of the Cloud SQL instance."
  value       = google_sql_database_instance.main.private_ip_address
}

output "secret_ids" {
  description = "Secret Manager secret IDs (names only, never values)."
  value       = { for key, secret in google_secret_manager_secret.this : key => secret.secret_id }
}

output "domain_dns_records" {
  description = "DNS records to create in Cloudflare for the custom domains."
  value = {
    api = try(google_cloud_run_domain_mapping.api[0].status[0].resource_records, [])
    web = try(google_cloud_run_domain_mapping.web[0].status[0].resource_records, [])
  }
}
