terraform {
  required_version = ">= 1.9.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Remote state in a GCS bucket. Bucket and prefix are passed at init time:
  #   terraform init -backend-config=envs/staging.backend.hcl
  # so that no bucket name is committed. See README.md.
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}
