terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Remote state with locking. Create the backend storage once (see infra/README.md),
  # then `terraform init`. Use `-backend=false` for offline `validate`.
  backend "azurerm" {
    resource_group_name  = "rg-tfstate"
    storage_account_name = "sttfstatecontoso"
    container_name       = "tfstate"
    key                  = "contoso-univ-prod.tfstate"
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}
