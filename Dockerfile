FROM node:22-alpine AS web
WORKDIR /web
COPY src/EventPlatform.Web/package*.json ./
RUN npm ci
COPY src/EventPlatform.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/EventPlatform.Api/ src/EventPlatform.Api/
RUN dotnet restore src/EventPlatform.Api --locked-mode
RUN dotnet publish src/EventPlatform.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
RUN mkdir -p /app/keys && chown $APP_UID:$APP_UID /app/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EventPlatform.Api.dll"]
