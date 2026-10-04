# Secrets live in SSM Parameter Store (SecureString, free tier). They are also in the Terraform state, so keep
# the state private (it is git-ignored) and move it to an encrypted S3 backend if more people use this.

resource "random_password" "origin_secret" {
  length  = 40
  special = false
}

resource "aws_ssm_parameter" "db_connection_string" {
  name = "/${var.name}/db-connection-string"
  type = "SecureString"

  # RDS for PostgreSQL 15+ requires SSL; the RDS certificate is not in the container trust store.
  value = "Host=${aws_db_instance.main.address};Port=5432;Database=${aws_db_instance.main.db_name};Username=${aws_db_instance.main.username};Password=${random_password.db.result};SSL Mode=Require;Trust Server Certificate=true"
}

resource "aws_ssm_parameter" "origin_secret" {
  name  = "/${var.name}/origin-secret"
  type  = "SecureString"
  value = random_password.origin_secret.result
}
