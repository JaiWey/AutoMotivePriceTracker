# The free tier includes 500 MB of private ECR storage, so keep a single image per repository.
# Only the SSR Lambda uses ECR; the API host runs a self-contained build downloaded from S3.

resource "aws_ecr_repository" "ssr" {
  name         = "${var.name}-ssr"
  force_delete = true
}

resource "aws_ecr_lifecycle_policy" "keep_one" {
  for_each   = { ssr = aws_ecr_repository.ssr.name }
  repository = each.value

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep only the newest image"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 1
      }
      action = { type = "expire" }
    }]
  })
}
