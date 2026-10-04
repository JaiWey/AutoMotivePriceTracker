# Amazon OpenSearch Service domain, reachable only inside the VPC (from the API host's security group).
# The Elastic .NET client cannot talk to it; the app uses OpenSearch.Client (Infrastructure/Search).

resource "aws_iam_service_linked_role" "opensearch" {
  count            = var.create_opensearch_service_linked_role ? 1 : 0
  aws_service_name = "opensearchservice.amazonaws.com"
}

resource "aws_opensearch_domain" "search" {
  domain_name    = var.name
  engine_version = "OpenSearch_2.19"

  cluster_config {
    instance_type  = var.search_instance_type
    instance_count = 1
  }

  ebs_options {
    ebs_enabled = true
    volume_size = 10
    volume_type = "gp2"
  }

  vpc_options {
    subnet_ids         = [aws_subnet.private[0].id]
    security_group_ids = [aws_security_group.search.id]
  }

  encrypt_at_rest {
    enabled = true
  }

  node_to_node_encryption {
    enabled = true
  }

  domain_endpoint_options {
    enforce_https       = true
    tls_security_policy = "Policy-Min-TLS-1-2-2019-07"
  }

  # Network access is already restricted to the API host's security group inside a private subnet, so the
  # resource policy is open and the app needs no request signing.
  access_policies = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { AWS = "*" }
      Action    = "es:*"
      Resource  = "arn:aws:es:${var.region}:${data.aws_caller_identity.current.account_id}:domain/${var.name}/*"
    }]
  })

  depends_on = [aws_iam_service_linked_role.opensearch]
}
