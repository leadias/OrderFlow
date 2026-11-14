FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OrderFlow.InventoryWorker/OrderFlow.InventoryWorker.csproj OrderFlow.InventoryWorker/
RUN dotnet restore OrderFlow.InventoryWorker/OrderFlow.InventoryWorker.csproj
COPY . .
RUN dotnet publish OrderFlow.InventoryWorker/OrderFlow.InventoryWorker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderFlow.InventoryWorker.dll"]