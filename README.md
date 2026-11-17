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

## Persistencia

Orders API usa PostgreSQL mediante EF Core. Al iniciar, aplica las migraciones pendientes. Compose conserva los datos en el volumen `postgres_data`; bajar y volver a levantar los servicios no elimina los pedidos. Para borrar también la base de datos de desarrollo, ejecuta `docker compose down -v`.

Pedidos e inventario requieren una sesión autenticada. La cuenta administradora inicial se configura mediante `ORDERFLOW_ADMIN_EMAIL` y `ORDERFLOW_ADMIN_PASSWORD` en `.env`, que está ignorado por Git. En un checkout nuevo, copia `.env.example` a `.env` y reemplaza los valores de ejemplo por valores propios. Identity guarda un hash de la contraseña y la sesión usa una cookie `HttpOnly`, `SameSite=Strict` y `Secure`.

Inventory Worker procesa los pedidos `Pending`. Si todos sus productos tienen existencias suficientes, las descuenta en una transacción y cambia el pedido a `Confirmed`; si falta un producto o cantidad, lo cambia a `Rejected` sin descontar ninguna línea. Los pedidos se procesan de forma segura incluso si se ejecutan varias instancias del worker.

La pestaña Pedidos consulta cambios de estado cada 3 segundos mientras está abierta; al volver desde Inventario también actualiza la lista inmediatamente.

En Angular, inicia sesión y abre la pestaña **Inventario** para consultar productos, registrar un SKU o ajustar sus existencias. La API también ofrece estos endpoints autenticados:

```powershell
Invoke-RestMethod http://localhost:8080/api/inventory
Invoke-RestMethod -Method Put -Uri http://localhost:8080/api/inventory/SKU-001 -ContentType 'application/json' -Body '{"quantityAvailable":10}'
```

Sin sesión, los endpoints de pedidos e inventario responden `401 Unauthorized`. `/health` permanece público para los healthchecks.

## Ejecutar localmente

Desde la raíz del repositorio:

Inicia PostgreSQL en una terminal:

```powershell
docker compose up -d postgres
```

Luego inicia la API y Angular en terminales separadas:

```powershell
$env:ConnectionStrings__OrdersDb = "Host=localhost;Port=5432;Database=orderflow;Username=orderflow;Password=<POSTGRES_PASSWORD de .env>"
$env:AdminAccount__Email = "<correo configurado en .env>"
$env:AdminAccount__Password = "<contraseña configurada en .env>"
dotnet run --project backend/OrderFlow.Api --no-launch-profile --urls http://localhost:8080
```

```powershell
cd frontend; npm install; npm start
```

Angular queda disponible en `http://localhost:4200` y reenvía `/api` a la API local. El endpoint de salud es `/health`. La cadena de conexión local está en `backend/OrderFlow.Api/appsettings.json` y puede sobrescribirse mediante `ConnectionStrings__OrdersDb`.

## Pruebas y migraciones

Ejecuta las pruebas del backend y Angular desde la raíz:

```powershell
dotnet test backend/OrderFlow.sln
cd frontend; npm test -- --watch=false
```

Para agregar una migración después de cambiar el modelo:

```powershell
dotnet ef migrations add NombreDeLaMigracion --project backend/OrderFlow.Api --startup-project backend/OrderFlow.Api
```

Las migraciones se aplican al arrancar Orders API. Las pruebas HTTP del API usan una base EF Core en memoria; las pruebas del Worker arrancan PostgreSQL temporal con Testcontainers y requieren Docker.

## Ejecutar con Docker Compose

Configura `.env` usando `.env.example` como guía y arranca los servicios:

```powershell
docker compose up --build
```

Angular queda disponible en `http://127.0.0.1:4200`; la API y PostgreSQL solo publican puertos en loopback. El worker se ejecuta en segundo plano y PostgreSQL conserva los datos en `postgres_data`.