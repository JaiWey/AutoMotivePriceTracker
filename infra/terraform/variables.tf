variable "region" {
  description = "AWS region. The retailers are NZ, so Sydney is the closest."
  type        = string
  default     = "ap-southeast-2"
}

variable "name" {
  description = "Prefix for resource names."
  type        = string
  default     = "autoprice"
}

variable "api_instance_type" {
  description = "EC2 type for the API + Chromium scrapers. t3.micro (1 GB) is free-tier eligible but tight; use t3.small if scrapes run out of memory."
  type        = string
  default     = "t3.micro"
}

variable "db_instance_class" {
  type    = string
  default = "db.t3.micro"
}

variable "db_skip_final_snapshot" {
  description = "Skip the final RDS snapshot on destroy."
  type        = bool
  default     = false
}

variable "search_instance_type" {
  description = "OpenSearch data node type (t3.small.search is free-tier eligible for 12 months)."
  type        = string
  default     = "t3.small.search"
}

variable "create_opensearch_service_linked_role" {
  description = "Create the OpenSearch service-linked role. Set false if the account already has it (creating it twice fails)."
  type        = bool
  default     = true
}

variable "budget_email" {
  description = "Email for the monthly cost alert. Empty disables the budget."
  type        = string
  default     = ""
}

variable "monthly_budget_usd" {
  type    = number
  default = 20
}
