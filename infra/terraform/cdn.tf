# One CloudFront distribution is the only public entry point:
#   /api/*                  -> the API host (private EC2) through a VPC origin
#   *.js, *.css, favicon... -> S3 (Angular static files)
#   everything else         -> the SSR Lambda (Function URL, origin access control)
# The browser and the SSR server both call /api on this same origin, so there is no CORS and no build-time URL.

locals {
  # Cache policy / origin request policy ids that AWS manages
  cache_disabled  = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" # Managed-CachingDisabled
  cache_optimized = "658327ea-f89d-4fab-a63d-7e88639e58f6" # Managed-CachingOptimized
  origin_all      = "216adef6-5c7f-47e4-b989-5492eafa07d3" # Managed-AllViewer

  static_patterns = ["*.js", "*.css", "favicon.ico", "*.png", "*.svg", "*.woff2"]
}

resource "aws_cloudfront_origin_access_control" "s3" {
  name                              = "${var.name}-s3"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_origin_access_control" "lambda" {
  name                              = "${var.name}-lambda"
  origin_access_control_origin_type = "lambda"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_vpc_origin" "api" {
  vpc_origin_endpoint_config {
    name                   = "${var.name}-api"
    arn                    = aws_instance.api.arn
    http_port              = 8080
    https_port             = 443
    origin_protocol_policy = "http-only"

    origin_ssl_protocols {
      quantity = 1
      items    = ["TLSv1.2"]
    }
  }
}

# Tells the SSR server which public host the viewer used, so the URLs it builds (and the API origin
# it derives for server-side calls) are the CloudFront ones rather than the Lambda Function URL's.
resource "aws_cloudfront_function" "forwarded_host" {
  name    = "${var.name}-forwarded-host"
  runtime = "cloudfront-js-2.0"
  publish = true
  code    = <<-JS
    function handler(event) {
      var request = event.request;
      // x-forwarded-* are restricted headers in CloudFront Functions, so use a custom name; server.ts maps it.
      request.headers['x-viewer-host'] = { value: request.headers.host.value };
      return request;
    }
  JS
}

resource "aws_cloudfront_origin_request_policy" "ssr" {
  name = "${var.name}-ssr"

  headers_config {
    header_behavior = "whitelist"
    headers {
      items = ["x-viewer-host"]
    }
  }
  cookies_config {
    cookie_behavior = "all"
  }
  query_strings_config {
    query_string_behavior = "all"
  }
}

resource "aws_cloudfront_distribution" "main" {
  enabled         = true
  is_ipv6_enabled = true
  comment         = var.name
  price_class     = "PriceClass_100" # US, Canada, Europe, Israel; cheapest. Use PriceClass_All for NZ-local edges.
  http_version    = "http2and3"

  origin {
    origin_id                = "ssr"
    domain_name              = split("/", aws_lambda_function_url.ssr.function_url)[2]
    origin_access_control_id = aws_cloudfront_origin_access_control.lambda.id

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
      origin_read_timeout    = 40
    }
  }

  origin {
    origin_id                = "assets"
    domain_name              = aws_s3_bucket.assets.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.s3.id
  }

  origin {
    origin_id   = "api"
    domain_name = aws_instance.api.private_dns

    vpc_origin_config {
      vpc_origin_id       = aws_cloudfront_vpc_origin.api.id
      origin_read_timeout = 60
    }

    # Checked by OriginVerificationMiddleware so the API only answers requests that came through CloudFront.
    custom_header {
      name  = "X-Origin-Verify"
      value = random_password.origin_secret.result
    }
  }

  # SSR pages: rendered per request, never cached
  default_cache_behavior {
    target_origin_id         = "ssr"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["GET", "HEAD", "OPTIONS"]
    cached_methods           = ["GET", "HEAD"]
    compress                 = true
    cache_policy_id          = local.cache_disabled
    origin_request_policy_id = aws_cloudfront_origin_request_policy.ssr.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.forwarded_host.arn
    }
  }

  # JSON API (reads and the admin POSTs): never cached, every method
  ordered_cache_behavior {
    path_pattern             = "/api/*"
    target_origin_id         = "api"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods           = ["GET", "HEAD"]
    compress                 = true
    cache_policy_id          = local.cache_disabled
    origin_request_policy_id = local.origin_all
  }

  # Angular static files (content-hashed names), cached for a long time
  dynamic "ordered_cache_behavior" {
    for_each = local.static_patterns

    content {
      path_pattern           = ordered_cache_behavior.value
      target_origin_id       = "assets"
      viewer_protocol_policy = "redirect-to-https"
      allowed_methods        = ["GET", "HEAD"]
      cached_methods         = ["GET", "HEAD"]
      compress               = true
      cache_policy_id        = local.cache_optimized
    }
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}
