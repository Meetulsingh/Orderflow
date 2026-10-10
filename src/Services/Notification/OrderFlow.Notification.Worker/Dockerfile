# syntax=docker/dockerfile:1
# Build context: repository root (see render.yaml / docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the dependency layer is cached between code changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/OrderFlow.Contracts/OrderFlow.Contracts.csproj src/BuildingBlocks/OrderFlow.Contracts/
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Services/Notification/OrderFlow.Notification.Worker/OrderFlow.Notification.Worker.csproj src/Services/Notification/OrderFlow.Notification.Worker/
RUN dotnet restore src/Services/Notification/OrderFlow.Notification.Worker/OrderFlow.Notification.Worker.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Notification/OrderFlow.Notification.Worker/ src/Services/Notification/OrderFlow.Notification.Worker/
RUN dotnet publish src/Services/Notification/OrderFlow.Notification.Worker/OrderFlow.Notification.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled Ubuntu image: no shell or package manager, runs as non-root — small and hardened.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production     ASPNETCORE_HTTP_PORTS=8080     DOTNET_GCHeapHardLimitPercent=0x4B
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFlow.Notification.Worker.dll"]
