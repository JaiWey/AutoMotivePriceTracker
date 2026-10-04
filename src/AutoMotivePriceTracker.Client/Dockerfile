# Angular SSR server packaged as an AWS Lambda container image (AWS Lambda Web Adapter proxies Lambda
# invocations to the Express server). It also runs as a plain container: docker run -p 4000:8080 ...
#   docker build -t autoprice-ssr src/AutoMotivePriceTracker.Client
FROM node:24-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# environment.production.ts has an empty apiUrl: the API is reached on the same origin as the app.
RUN npx ng build

FROM node:24-slim
COPY --from=public.ecr.aws/awsguru/aws-lambda-adapter:0.9.1 /lambda-adapter /opt/extensions/lambda-adapter
ENV PORT=8080 \
    AWS_LWA_PORT=8080 \
    NODE_ENV=production
WORKDIR /app
COPY --from=build /app/dist/AutoMotivePriceTracker.Client ./dist/AutoMotivePriceTracker.Client
# NG_ALLOWED_HOSTS (comma separated host names, wildcards allowed) must list every Host / X-Forwarded-Host
# the server will see; angular.json only allows localhost.
CMD ["node", "dist/AutoMotivePriceTracker.Client/server/server.mjs"]
