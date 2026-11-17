using Microsoft.EntityFrameworkCore;
using OrderFlow.Api.Models;

namespace OrderFlow.Api.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<InventoryItemEntity> InventoryItems => Set<InventoryItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.CustomerId).HasMaxLength(100).IsRequired();
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(order => order.CreatedAtUtc).IsRequired();
            entity.HasMany(order => order.Items)
                .WithOne(item => item.Order)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItemEntity>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(item => new { item.OrderId, item.LineNumber });
            entity.Property(item => item.ProductId).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Quantity).IsRequired();
        });

        modelBuilder.Entity<InventoryItemEntity>(entity =>
        {
            entity.ToTable("inventory_items", table =>
                table.HasCheckConstraint("CK_inventory_items_quantity_available", "\"QuantityAvailable\" >= 0"));
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).HasMaxLength(100);
            entity.Property(item => item.QuantityAvailable).IsRequired();
        });
    }
}