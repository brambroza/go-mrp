# ---------- project ----------

variable "project_id" {
  description = "GCP project ID that hosts this environment. One project per environment is recommended."
  type        = string
}

variable "region" {
  description = "GCP region for every regional resource."
  type        = string
  default     = "asia-southeast1"
}

variable "environment" {
  description = "Environment name. Also the name of the GitHub environment that is allowed to deploy."
  type        = string

  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "environment must be \"staging\" or \"production\"."
  }
}

variable "name_prefix" {
  description = "Prefix of resource names. Keep \"mrp\" when each environment has its own project; use e.g. \"mrp-stg\" when two environments share a project."
  type        = string
  default     = "mrp"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,14}$", var.name_prefix))
    error_message = "name_prefix must be 2-15 characters: lowercase letters, digits and hyphens, starting with a letter."
  }
}

variable "labels" {
  description = "Extra labels added to every resource that supports labels."
  type        = map(string)
  default     = {}
}

# ---------- network ----------

variable "run_subnet_cidr" {
  description = "CIDR of the subnet used by Cloud Run Direct VPC egress. /24 leaves room for scaling; /26 is the minimum."
  type        = string
  default     = "10.10.0.0/24"
}

variable "private_services_prefix_length" {
  description = "Prefix length of the range reserved for private services access (Cloud SQL private IP)."
  type        = number
  default     = 20
}

# ---------- database ----------

variable "db_tier" {
  description = "Cloud SQL machine tier. Shared-core tiers (db-f1-micro, db-g1-small) are cheap but are not covered by the Cloud SQL SLA."
  type        = string
  default     = "db-g1-small"
}

variable "db_availability_type" {
  description = "ZONAL (single zone, cheapest) or REGIONAL (high availability, roughly doubles the instance cost)."
  type        = string
  default     = "ZONAL"

  validation {
    condition     = contains(["ZONAL", "REGIONAL"], var.db_availability_type)
    error_message = "db_availability_type must be ZONAL or REGIONAL."
  }
}

variable "db_activation_policy" {
  description = "ALWAYS = instance is running. NEVER = instance is stopped (only storage is billed); useful for a staging environment that is idle for weeks."
  type        = string
  default     = "ALWAYS"

  validation {
    condition     = contains(["ALWAYS", "NEVER"], var.db_activation_policy)
    error_message = "db_activation_policy must be ALWAYS or NEVER."
  }
}

variable "db_disk_size_gb" {
  description = "Initial SSD size in GB. The disk grows automatically up to db_disk_autoresize_limit_gb."
  type        = number
  default     = 10
}

variable "db_disk_autoresize_limit_gb" {
  description = "Upper bound for automatic disk growth (cost guard). 0 means no limit."
  type        = number
  default     = 50
}

variable "db_backup_retained_count" {
  description = "Number of automated daily backups to keep."
  type        = number
  default     = 7
}

variable "db_pitr_retention_days" {
  description = "Days of transaction logs kept for point-in-time recovery (1-7 on Enterprise edition)."
  type        = number
  default     = 7

  validation {
    condition     = var.db_pitr_retention_days >= 1 && var.db_pitr_retention_days <= 7
    error_message = "db_pitr_retention_days must be between 1 and 7."
  }
}

variable "db_public_ip_enabled" {
  description = "Give the instance a public IP. Keep false. Set true only temporarily to reach the instance through the Cloud SQL Auth Proxy from a laptop (no authorized networks are ever configured)."
  type        = bool
  default     = false
}

variable "db_max_pool_size" {
  description = "Npgsql \"Maximum Pool Size\" per container instance. api_max_instances * db_max_pool_size must stay below the instance's max_connections."
  type        = number
  default     = 10
}

variable "deletion_protection" {
  description = "Protect the Cloud SQL instance and the Cloud Run services from deletion. Must be true for production."
  type        = bool
  default     = true
}

# ---------- Cloud Run ----------

variable "api_image" {
  description = "Image used when Terraform CREATES the API service and the migrate job. Later image changes are made by the deploy workflow and ignored by Terraform."
  type        = string
  default     = "us-docker.pkg.dev/cloudrun/container/hello:latest"
}

variable "web_image" {
  description = "Image used when Terraform CREATES the web service. Later image changes are made by the deploy workflow and ignored by Terraform."
  type        = string
  default     = "us-docker.pkg.dev/cloudrun/container/hello:latest"
}

variable "api_min_instances" {
  description = "Minimum API instances. 0 = scale to zero (cold start of a few seconds)."
  type        = number
  default     = 0
}

variable "api_max_instances" {
  description = "Maximum API instances."
  type        = number
  default     = 3
}

variable "api_cpu" {
  description = "CPU limit of the API container."
  type        = string
  default     = "1"
}

variable "api_memory" {
  description = "Memory limit of the API container."
  type        = string
  default     = "512Mi"
}

variable "web_min_instances" {
  description = "Minimum web instances. 0 = scale to zero."
  type        = number
  default     = 0
}

variable "web_max_instances" {
  description = "Maximum web instances."
  type        = number
  default     = 3
}

variable "web_cpu" {
  description = "CPU limit of the web container."
  type        = string
  default     = "1"
}

variable "web_memory" {
  description = "Memory limit of the web container."
  type        = string
  default     = "512Mi"
}

variable "ingress" {
  description = "Cloud Run ingress setting for api and web. INGRESS_TRAFFIC_ALL until traffic is restricted to Cloudflare (see README)."
  type        = string
  default     = "INGRESS_TRAFFIC_ALL"

  validation {
    condition     = contains(["INGRESS_TRAFFIC_ALL", "INGRESS_TRAFFIC_INTERNAL_ONLY", "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"], var.ingress)
    error_message = "ingress must be a valid Cloud Run v2 ingress value."
  }
}

variable "allow_unauthenticated" {
  description = "Grant roles/run.invoker to allUsers on api and web (public endpoints; the API authenticates with JWT itself)."
  type        = bool
  default     = true
}

variable "cors_origins" {
  description = "Origins allowed by the API (Cors__Origins__N). Empty = the run.app URL of the web service, plus https://<web_domain> when set."
  type        = list(string)
  default     = []
}

variable "worker_enabled" {
  description = "Create the background worker service. Keep false: the API has no worker mode yet."
  type        = bool
  default     = false
}

# ---------- custom domains (optional) ----------

variable "api_domain" {
  description = "Custom domain of the API, e.g. api.example.com. Empty = no domain mapping."
  type        = string
  default     = ""
}

variable "web_domain" {
  description = "Custom domain of the web app, e.g. app.example.com. Empty = no domain mapping."
  type        = string
  default     = ""
}

# ---------- CI/CD ----------

variable "github_repository" {
  description = "GitHub repository allowed to deploy, as \"owner/name\"."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", var.github_repository))
    error_message = "github_repository must look like \"owner/name\"."
  }
}

variable "github_environment" {
  description = "GitHub environment whose jobs may impersonate the deployer. Empty = same as var.environment."
  type        = string
  default     = ""
}
