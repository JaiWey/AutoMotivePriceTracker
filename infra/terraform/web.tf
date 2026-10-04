# --- Static assets (Angular dist/browser) served by CloudFront from S3 ---

resource "random_id" "suffix" {
  byte_length = 4
}

resource "aws_s3_bucket" "assets" {
  bucket        = "${var.name}-assets-${random_id.suffix.hex}"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "assets" {
  bucket                  = aws_s3_bucket.assets.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_policy" "assets" {
  bucket = aws_s3_bucket.assets.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "cloudfront.amazonaws.com" }
      Action    = "s3:GetObject"
      Resource  = "${aws_s3_bucket.assets.arn}/*"
      Condition = { StringEquals = { "AWS:SourceArn" = aws_cloudfront_distribution.main.arn } }
    }]
  })
}

# --- Deploy bundle for the API host (docker-compose.api.yml) ---

resource "aws_s3_bucket" "artifacts" {
  bucket        = "${var.name}-artifacts-${random_id.suffix.hex}"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "artifacts" {
  bucket                  = aws_s3_bucket.artifacts.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# --- Angular SSR as a Lambda container image behind a Function URL ---
# The image must exist in ECR before this is created: deploy/deploy.ps1 applies the ECR repositories first
# (-target), pushes the images, then applies everything.

resource "aws_iam_role" "ssr" {
  name = "${var.name}-ssr"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ssr_logs" {
  role       = aws_iam_role.ssr.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_lambda_function" "ssr" {
  function_name = "${var.name}-ssr"
  role          = aws_iam_role.ssr.arn
  package_type  = "Image"
  image_uri     = "${aws_ecr_repository.ssr.repository_url}:latest"
  architectures = ["x86_64"]
  memory_size   = 1024
  timeout       = 30

  environment {
    variables = {
      # Host / X-Forwarded-Host values the SSR server accepts (angular.json only allows localhost):
      # the CloudFront domain (set as X-Forwarded-Host by the viewer-request function) and the Function URL host.
      NG_ALLOWED_HOSTS = "*.cloudfront.net,*.lambda-url.${var.region}.on.aws"
      AWS_LWA_PORT     = "8080"
      PORT             = "8080"
      # Only reachable through CloudFront (AWS_IAM + origin access control), whose function sets X-Forwarded-*
      TRUST_PROXY_HEADERS = "true"
    }
  }

  depends_on = [aws_iam_role_policy_attachment.ssr_logs]
}

# AWS_IAM auth + CloudFront origin access control: the Function URL cannot be called except through CloudFront.
resource "aws_lambda_function_url" "ssr" {
  function_name      = aws_lambda_function.ssr.function_name
  authorization_type = "AWS_IAM"
}

resource "aws_lambda_permission" "cloudfront_invoke_url" {
  statement_id           = "AllowCloudFrontInvokeFunctionUrl"
  action                 = "lambda:InvokeFunctionUrl"
  function_name          = aws_lambda_function.ssr.function_name
  principal              = "cloudfront.amazonaws.com"
  source_arn             = aws_cloudfront_distribution.main.arn
  function_url_auth_type = "AWS_IAM"
}

resource "aws_lambda_permission" "cloudfront_invoke" {
  statement_id  = "AllowCloudFrontInvokeFunction"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.ssr.function_name
  principal     = "cloudfront.amazonaws.com"
  source_arn    = aws_cloudfront_distribution.main.arn
}
