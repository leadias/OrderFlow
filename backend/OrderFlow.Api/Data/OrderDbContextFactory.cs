using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace OrderFlow.Api.Data;

public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("OrdersDb")
            ?? throw new InvalidOperationException("ConnectionStrings:OrdersDb is not configured.");
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderDbContext(options);
    }
}
