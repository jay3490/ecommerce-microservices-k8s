terraform {
  required_version = ">= 1.6"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # State is local by default. For shared use configure a remote backend, for example:
  # backend "azurerm" {
  #   resource_group_name  = "rg-tfstate"
  #   storage_account_name = "<storage account>"
  #   container_name       = "tfstate"
  #   key                  = "ecommerce-dev.tfstate"
  # }
}

provider "azurerm" {
  features {
    key_vault {
      # Throwaway environment: really delete the vault on destroy.
      purge_soft_delete_on_destroy = true
    }
  }

  # Falls back to the ARM_SUBSCRIPTION_ID environment variable when null.
  subscription_id = var.subscription_id
}
