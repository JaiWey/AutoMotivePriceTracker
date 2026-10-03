// One-off tool: copies products and price history from the old SQLite database into PostgreSQL.
//
//   dotnet run --project tools/AutoMotivePriceTracker.SqliteImport -- <sqlite-file> <postgres-connection-string>
//
// Ids are preserved. The target database is migrated first and must be empty (the tool refuses
// to merge into existing data).
using System.Globalization;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using AutoMotivePriceTracker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SqliteImport <sqlite-file> <postgres-connection-string>");
    return 2;
}

var sqlitePath = Path.GetFullPath(args[0]);
if (!File.Exists(sqlitePath))
{
    Console.Error.WriteLine($"SQLite file not found: {sqlitePath}");
    return 2;
}

var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(args[1]).Options;
await using var target = new AppDbContext(options);
await target.Database.MigrateAsync();

if (await target.Products.AnyAsync() || await target.PriceRecords.AnyAsync())
{
    Console.Error.WriteLine("Target database already contains data; refusing to import into it.");
    return 1;
}

await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = sqlitePath,
    Mode = SqliteOpenMode.ReadOnly,
}.ToString());
await source.OpenAsync();

// SQLite stores DateTimes as text without a zone; everything the app wrote is UTC.
static DateTime Utc(string text) =>
    DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture), DateTimeKind.Utc);

var products = new Dictionary<int, Product>();
await using (var cmd = source.CreateCommand())
{
    cmd.CommandText = "SELECT Id, Name, Brand, Category, Sku, ImageUrl, SourceUrl, Retailer, LastChecked FROM Products ORDER BY Id";
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var p = new Product
        {
            Id = r.GetInt32(0),
            Name = r.GetString(1),
            Brand = r.IsDBNull(2) ? null : r.GetString(2),
            Category = r.IsDBNull(3) ? null : r.GetString(3),
            Sku = r.GetString(4),
            ImageUrl = r.IsDBNull(5) ? null : r.GetString(5),
            SourceUrl = r.GetString(6),
            Retailer = (Retailer)r.GetInt32(7),
            LastChecked = Utc(r.GetString(8)),
        };
        products[p.Id] = p;
    }
}

var recordCount = 0;
await using (var cmd = source.CreateCommand())
{
    cmd.CommandText = "SELECT Id, ProductId, Price, WasPrice, Date FROM PriceRecords ORDER BY Id";
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var productId = r.GetInt32(1);
        if (!products.TryGetValue(productId, out var product))
        {
            Console.Error.WriteLine($"Skipping price record {r.GetInt32(0)}: unknown product {productId}");
            continue;
        }
        product.PriceHistory.Add(new PriceRecord
        {
            Id = r.GetInt32(0),
            Price = decimal.Parse(r.GetString(2), CultureInfo.InvariantCulture),
            WasPrice = r.IsDBNull(3) ? null : decimal.Parse(r.GetString(3), CultureInfo.InvariantCulture),
            Date = Utc(r.GetString(4)),
        });
        recordCount++;
    }
}

await using var tx = await target.Database.BeginTransactionAsync();
target.Products.AddRange(products.Values);
await target.SaveChangesAsync();

// Ids were inserted explicitly, so move the identity sequences past them.
await target.Database.ExecuteSqlRawAsync(
    """SELECT setval(pg_get_serial_sequence('"Products"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Products"), 1), (SELECT COUNT(*) > 0 FROM "Products"))""");
await target.Database.ExecuteSqlRawAsync(
    """SELECT setval(pg_get_serial_sequence('"PriceRecords"', 'Id'), COALESCE((SELECT MAX("Id") FROM "PriceRecords"), 1), (SELECT COUNT(*) > 0 FROM "PriceRecords"))""");
await tx.CommitAsync();

Console.WriteLine($"Imported {products.Count} products and {recordCount} price records.");
return 0;
