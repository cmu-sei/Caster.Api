#
#multi-stage target: dev
#
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev

ENV ASPNETCORE_HTTP_PORTS=5000
ENV ASPNETCORE_ENVIRONMENT=DEVELOPMENT

COPY . /app
WORKDIR /app/src/Caster.Api
RUN dotnet publish -c Release -o /app/dist
CMD [ "dotnet", "run" ]

#
#multi-stage target: prod
#
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS prod
ARG commit
ENV COMMIT=$commit
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false
COPY --from=dev /app/dist /app

WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80

CMD [ "dotnet", "Caster.Api.dll" ]

#Install git and set credential store
RUN apt-get update                   && \
    apt-get install -y git jq curl   && \
    git config --global credential.helper store
