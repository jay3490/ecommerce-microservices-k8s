terraform {
  required_version = ">= 1.6"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # State is local by default. For shared use configure a remote backend, for example:
  # backend "azurerm" {
  #   resource_group_name  = "rg-tfstate"
  #   storage_account_name = "<storage account>"
  #   container_name       = "tfstate"
  #   key                  = "ecommerce-aks.tfstate"
  # }
}

provider "azurerm" {
  features {}

  # Falls back to the ARM_SUBSCRIPTION_ID environment variable when null.
  subscription_id = var.subscription_id
}
