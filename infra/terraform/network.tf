locals {
  azs = slice(data.aws_availability_zones.available.names, 0, 2)
}

resource "aws_vpc" "main" {
  cidr_block           = "10.0.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = { Name = var.name }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = var.name }
}

# Public subnets only hold the NAT gateway; everything else lives in the private subnets.
resource "aws_subnet" "public" {
  count                   = 2
  vpc_id                  = aws_vpc.main.id
  cidr_block              = cidrsubnet(aws_vpc.main.cidr_block, 8, count.index)
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = false

  tags = { Name = "${var.name}-public-${count.index}" }
}

resource "aws_subnet" "private" {
  count             = 2
  vpc_id            = aws_vpc.main.id
  cidr_block        = cidrsubnet(aws_vpc.main.cidr_block, 8, count.index + 10)
  availability_zone = local.azs[count.index]

  tags = { Name = "${var.name}-private-${count.index}" }
}

# One NAT gateway (single AZ) keeps the cost to roughly one NAT; the API needs it to reach the retailers,
# ECR and SSM.
resource "aws_eip" "nat" {
  domain = "vpc"
  tags   = { Name = "${var.name}-nat" }
}

resource "aws_nat_gateway" "main" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id

  depends_on = [aws_internet_gateway.main]
  tags       = { Name = var.name }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = { Name = "${var.name}-public" }
}

resource "aws_route_table" "private" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.main.id
  }

  tags = { Name = "${var.name}-private" }
}

resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table_association" "private" {
  count          = 2
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}

# --- Security groups (nothing is open to the internet inbound) ---

resource "aws_security_group" "api" {
  name_prefix = "${var.name}-api-"
  description = "API host: reachable only from the CloudFront VPC origin"
  vpc_id      = aws_vpc.main.id

  egress {
    description = "Retailers, ECR, SSM, S3, database and search"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  lifecycle {
    create_before_destroy = true
  }
}

# CloudFront creates a managed security group for the VPC origin; allow it to reach the API on 8080.
data "aws_security_group" "cloudfront_vpc_origin" {
  name   = "CloudFront-VPCOrigins-Service-SG"
  vpc_id = aws_vpc.main.id

  depends_on = [aws_cloudfront_vpc_origin.api]
}

resource "aws_vpc_security_group_ingress_rule" "api_from_cloudfront" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = data.aws_security_group.cloudfront_vpc_origin.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
  description                  = "CloudFront VPC origin"
}

resource "aws_security_group" "db" {
  name_prefix = "${var.name}-db-"
  description = "PostgreSQL: only from the API host"
  vpc_id      = aws_vpc.main.id

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_vpc_security_group_ingress_rule" "db_from_api" {
  security_group_id            = aws_security_group.db.id
  referenced_security_group_id = aws_security_group.api.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}

resource "aws_security_group" "search" {
  name_prefix = "${var.name}-search-"
  description = "OpenSearch: only from the API host"
  vpc_id      = aws_vpc.main.id

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_vpc_security_group_ingress_rule" "search_from_api" {
  security_group_id            = aws_security_group.search.id
  referenced_security_group_id = aws_security_group.api.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
