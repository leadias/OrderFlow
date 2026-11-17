using OrderFlow.InventoryWorker;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("OrdersDb")
	?? throw new InvalidOperationException("ConnectionStrings:OrdersDb is not configured.");

builder.Services.AddSingleton(serviceProvider => new OrderProcessor(
	connectionString,
	serviceProvider.GetRequiredService<ILogger<OrderProcessor>>()));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
