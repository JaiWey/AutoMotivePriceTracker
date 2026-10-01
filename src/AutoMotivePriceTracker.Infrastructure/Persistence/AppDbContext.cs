using AutoMotivePriceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoMotivePriceTracker.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceRecord> PriceRecords => Set<PriceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(p => new { p.Sku, p.Retailer }).IsUnique();
            e.HasIndex(p => p.Category);
            e.HasIndex(p => p.Retailer);
            e.HasMany(p => p.PriceHistory)
             .WithOne(r => r.Product)
             .HasForeignKey(r => r.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceRecord>(e =>
        {
            e.HasIndex(r => new { r.ProductId, r.Date });
        });
    }
}
