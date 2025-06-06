# Adapted from https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile.chiseled

# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy project files and restore as distinct layers
COPY --link src/Caster.Api/*.csproj ./src/Caster.Api/
WORKDIR /source/src/Caster.Api
RUN dotnet restore -a $TARGETARCH

# Copy source code and publish app
WORKDIR /source
COPY --link . .
WORKDIR /source/src/Caster.Api
RUN dotnet publish -a $TARGETARCH --no-restore -o /app

# Production Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS prod
ARG commit
ENV COMMIT=$commit
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false

# This can be removed this after switching to IHost builder
ENV ASPNETCORE_URLS=http://*:8080

EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app .

# Install git and set credential store
RUN apt-get update                   && \
    apt-get install -y git jq curl unzip wget  && \
    git config --global credential.helper store

USER $APP_UID
ENTRYPOINT ["./Caster.Api"]


