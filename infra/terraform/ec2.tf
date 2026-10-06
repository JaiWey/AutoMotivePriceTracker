data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

# --- Instance role: SSM Session Manager (no SSH), download the API build, read secrets ---

resource "aws_iam_role" "api" {
  name = "${var.name}-api"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "api_ssm" {
  role       = aws_iam_role.api.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy" "api" {
  name = "deploy-access"
  role = aws_iam_role.api.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "s3:GetObject"
        Resource = "${aws_s3_bucket.artifacts.arn}/*"
      },
      {
        Effect = "Allow"
        Action = "ssm:GetParameter"
        Resource = [
          aws_ssm_parameter.db_connection_string.arn,
          aws_ssm_parameter.origin_secret.arn,
        ]
      },
    ]
  })
}

resource "aws_iam_instance_profile" "api" {
  name = "${var.name}-api"
  role = aws_iam_role.api.name
}

# --- The API host: public subnet so it can reach SSM/S3 without a NAT gateway. The security group has
# no inbound rule except CloudFront's VPC origin, so the public IP is not reachable from the internet. ---

resource "aws_instance" "api" {
  ami                         = data.aws_ssm_parameter.al2023.value
  instance_type               = var.api_instance_type
  subnet_id                   = aws_subnet.public[0].id
  associate_public_ip_address = true
  vpc_security_group_ids = [aws_security_group.api.id]
  iam_instance_profile   = aws_iam_instance_profile.api.name

  metadata_options {
    http_tokens                 = "required" # IMDSv2 only
    http_put_response_hop_limit = 2          # containers can still reach the instance role
  }

  root_block_device {
    volume_size = 30
    volume_type = "gp3"
    encrypted   = true
  }

  user_data = templatefile("${path.module}/templates/user-data.sh.tftpl", {
    deploy_script = templatefile("${path.module}/templates/deploy.sh.tftpl", {
      region       = var.region
      bucket       = aws_s3_bucket.artifacts.id
      db_param     = aws_ssm_parameter.db_connection_string.name
      origin_param = aws_ssm_parameter.origin_secret.name
      search_url   = "https://${aws_opensearch_domain.search.endpoint}"
    })
  })

  # The bootstrap runs once; later script or AMI changes must not replace a running host with data on it.
  user_data_replace_on_change = false

  lifecycle {
    ignore_changes = [ami, user_data]
  }

  tags = { Name = "${var.name}-api" }

  depends_on = [aws_route_table_association.public]
}
