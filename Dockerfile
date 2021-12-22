#
#multi-stage target: dev
#
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS dev

ENV ASPNETCORE_URLS=http://*:5000 \
    ASPNETCORE_ENVIRONMENT=DEVELOPMENT

COPY . /app
WORKDIR /app/src/Caster.Api
RUN dotnet publish -c Release -o /app/dist
CMD [ "dotnet", "run" ]

#
#multi-stage target: prod
#
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS prod
ARG commit
ENV COMMIT=$commit
COPY --from=dev /app/dist /app
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_URLS=http://*:80
CMD [ "dotnet", "Caster.Api.dll" ]

#Install git and set credential store
RUN apt-get update              && \
    apt-get install -y git jq   && \
    git config --global credential.helper store
