# Build from the repository root:
#   docker build -f src/Services/Payment/Payment.Api/Dockerfile -t ecommerce/payment-api:local .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Services/Payment/Payment.Api/Payment.Api.csproj src/Services/Payment/Payment.Api/
RUN dotnet restore src/Services/Payment/Payment.Api/Payment.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Payment/Payment.Api/ src/Services/Payment/Payment.Api/
RUN dotnet publish src/Services/Payment/Payment.Api/Payment.Api.csproj     --configuration Release     --no-restore     --output /app/publish     -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080     Observability__JsonLogs=true

EXPOSE 8080
COPY --from=build /app/publish .

# Non-root user provided by the .NET base images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Payment.Api.dll"]
