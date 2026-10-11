# syntax=docker/dockerfile:1
# Build context: repository root (see render.yaml / docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the dependency layer is cached between code changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/OrderFlow.Contracts/OrderFlow.Contracts.csproj src/BuildingBlocks/OrderFlow.Contracts/
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Services/Identity/OrderFlow.Identity.API/OrderFlow.Identity.API.csproj src/Services/Identity/OrderFlow.Identity.API/
COPY src/Services/Assistant/OrderFlow.Assistant/OrderFlow.Assistant.csproj src/Services/Assistant/OrderFlow.Assistant/
COPY src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj src/Gateway/OrderFlow.Gateway/
RUN dotnet restore src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Identity/OrderFlow.Identity.API/ src/Services/Identity/OrderFlow.Identity.API/
COPY src/Services/Assistant/OrderFlow.Assistant/ src/Services/Assistant/OrderFlow.Assistant/
COPY src/Gateway/OrderFlow.Gateway/ src/Gateway/OrderFlow.Gateway/
RUN dotnet publish src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled Ubuntu image: no shell or package manager, runs as non-root — small and hardened.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production     ASPNETCORE_HTTP_PORTS=8080     DOTNET_GCHeapHardLimitPercent=0x4B
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFlow.Gateway.dll"]
