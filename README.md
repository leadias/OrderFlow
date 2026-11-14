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

Inicia la API en una terminal:

```powershell
dotnet run --project backend/OrderFlow.Api --no-launch-profile --urls http://localhost:8080
```

Inicia Angular en otra terminal:

```powershell
cd frontend; npm install; npm start
```

Angular queda disponible en `http://localhost:4200` y reenvía `/api` a la API local. El endpoint de salud es `/health`. Los pedidos se almacenan en memoria y se pierden al reiniciar la API.

## Ejecutar con Docker Compose

```powershell
docker compose up --build
```

Angular queda disponible en `http://localhost:4200` y el endpoint de salud de la API en `http://localhost:8080/health`. El worker se ejecuta en segundo plano.