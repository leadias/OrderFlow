# OrderFlow

Monorepo para una aplicación de gestión de pedidos. Cada servicio conserva su propio proyecto y Dockerfile.

## Estructura

- `backend/OrderFlow.Api`: API HTTP de pedidos en ASP.NET Core.
- `backend/OrderFlow.InventoryWorker`: worker independiente para procesos de inventario.
- `frontend`: aplicación Angular.
- `docker-compose.yml`: arranque local de los tres componentes.

## Requisitos

- .NET SDK 10
- Node.js 24 y npm
- Docker Desktop con el motor iniciado

## Ejecutar localmente

Desde la raíz del repositorio:

```powershell
dotnet run --project backend/OrderFlow.Api
dotnet run --project backend/OrderFlow.InventoryWorker
cd frontend; npm install; npm start
```

La API publica el endpoint de salud en `/health`. La URL local exacta de la API se muestra al iniciar `dotnet run`.

## Ejecutar con Docker Compose

```powershell
docker compose up --build
```

Angular queda disponible en `http://localhost:4200` y la API en `http://localhost:8080`. El worker se ejecuta en segundo plano.