output "site_url" {
  description = "Public URL of the app (and /api)."
  value       = "https://${aws_cloudfront_distribution.main.domain_name}"
}

output "cloudfront_distribution_id" {
  value = aws_cloudfront_distribution.main.id
}

output "region" {
  value = var.region
}

output "ssr_ecr_repository_url" {
  value = aws_ecr_repository.ssr.repository_url
}

output "ssr_lambda_name" {
  value = aws_lambda_function.ssr.function_name
}

output "assets_bucket" {
  value = aws_s3_bucket.assets.id
}

output "artifacts_bucket" {
  value = aws_s3_bucket.artifacts.id
}

output "api_instance_id" {
  description = "Use with: aws ssm start-session --target <id>"
  value       = aws_instance.api.id
}

output "search_endpoint" {
  value = aws_opensearch_domain.search.endpoint
}
