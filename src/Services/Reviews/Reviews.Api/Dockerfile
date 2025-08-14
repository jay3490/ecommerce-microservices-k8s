# Build from the repository root:
#   docker build -f src/Services/Reviews/Reviews.Api/Dockerfile -t ecommerce/reviews-api:local .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Services/Reviews/Reviews.Api/Reviews.Api.csproj src/Services/Reviews/Reviews.Api/
RUN dotnet restore src/Services/Reviews/Reviews.Api/Reviews.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Reviews/Reviews.Api/ src/Services/Reviews/Reviews.Api/
RUN dotnet publish src/Services/Reviews/Reviews.Api/Reviews.Api.csproj     --configuration Release     --no-restore     --output /app/publish     -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080     Observability__JsonLogs=true

EXPOSE 8080
COPY --from=build /app/publish .

# Non-root user provided by the .NET base images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Reviews.Api.dll"]
