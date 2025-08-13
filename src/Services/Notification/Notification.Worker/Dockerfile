# Build from the repository root:
#   docker build -f src/Services/Notification/Notification.Worker/Dockerfile -t ecommerce/notification-worker:local .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Services/Notification/Notification.Worker/Notification.Worker.csproj src/Services/Notification/Notification.Worker/
RUN dotnet restore src/Services/Notification/Notification.Worker/Notification.Worker.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Notification/Notification.Worker/ src/Services/Notification/Notification.Worker/
RUN dotnet publish src/Services/Notification/Notification.Worker/Notification.Worker.csproj     --configuration Release     --no-restore     --output /app/publish     -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080     Observability__JsonLogs=true

EXPOSE 8080
COPY --from=build /app/publish .

# Non-root user provided by the .NET base images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Notification.Worker.dll"]
