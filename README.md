# TA.DataAccess.SqlServer

`TA.DataAccess.SqlServer` is a lightweight, strongly-typed data access helper for SQL Server on modern .NET.

Targets: **net8.0**, **net9.0**, **net10.0**.

## Features

- Sync + async CRUD with `CancellationToken` on every async path
- Identifier hardening (`[bracketed]` + regex validation) against injection
- `FormattableString` parameterized API (`ExecuteInterpolated`)
- `IAsyncEnumerable<T>` streaming reads (`SelectStreamAsync`)
- `SqlBulkCopy` for big inserts (`BulkInsertAsync`)
- Attribute mapping: `[Table]`, `[Column]`, `[Key]`, `[Identity]`, `[NoCrud]`
- Expanded type coercion: `DateOnly`, `TimeOnly`, `DateTimeOffset`, `Guid`, enums
- Cached compiled getters/setters via expression trees
- `ILogger<SqlServerHelper>` integration with timing
- Source Link + symbol package for stepping into source from PDB

## Install

```sh
dotnet add package TA.DataAccess.SqlServer
```

```powershell
PM> NuGet\Install-Package TA.DataAccess.SqlServer
```

## Configure

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=App;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

DI registration:

```csharp
services.AddSingleton<ISqlServerHelper>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SqlServerHelper>>();
    var options = new SqlServerHelperOptions { CommandTimeoutSeconds = 60 };
    return new SqlServerHelper(config, "DefaultConnection", options, logger);
});
```

## Model mapping

```csharp
[Table("Products", Schema = "catalog")]
public record class Product
{
    [Identity] public int Id { get; init; }
    [Key, Column("sku")] public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public DateOnly ReleasedOn { get; init; }
    [NoCrud] public string InternalNote { get; init; } = "";
}
```

Without `[Table]`, the type name is used. Without `[Column]`, the property name is used. `[NoCrud]` properties are skipped. `[Identity]` is excluded from inserts. `[Key]` (or `[Identity]` as fallback) drives `UpdateModel` when `idColumn` is omitted.

## Quick examples

```csharp
// parameterized interpolated query — safe
await helper.ExecuteInterpolatedAsync($"UPDATE Products SET Price = {newPrice} WHERE Sku = {sku}");

// streaming read — no DataTable, low memory
await foreach (var product in helper.SelectStreamAsync<Product>("SELECT * FROM catalog.Products"))
    Process(product);

// bulk insert
await helper.BulkInsertAsync(products, cancellationToken: token);

// CRUD using attribute-resolved table + key
await helper.InsertModelAsync(new Product { Sku = "X1", Name = "Widget", Price = 9.99m });
var p = await helper.GetModelByIdAsync<Product>("catalog.Products", "sku", "X1");
p!.Price = 12.50m;
await helper.UpdateModelAsync(p);
```

## API reference

| Method | Sync | Async | Notes |
| --- | --- | --- | --- |
| `ExecuteNonQuery(string, params)` | ✓ | ✓ | INSERT/UPDATE/DELETE/DDL. Returns rows affected. |
| `ExecuteNonQuery(IReadOnlyList<string>)` | ✓ | ✓ | Multiple statements in one transaction. |
| `ExecuteInterpolated(FormattableString)` | ✓ | ✓ | Auto-parameterizes interpolation arguments. |
| `Select(string, params)` | ✓ | ✓ | Returns `DataTable`. |
| `Select<T>(string, params)` | ✓ | ✓ | Returns `List<T>`. |
| `SelectStreamAsync<T>(string, params)` | — | ✓ | Returns `IAsyncEnumerable<T>`. |
| `InsertModel<T>(model, table?)` | ✓ | ✓ | Single insert. Skips `[Identity]`. |
| `InsertModels<T>(list, table?)` | ✓ | ✓ | Transactional batch. |
| `BulkInsertAsync<T>(list, table?)` | — | ✓ | `SqlBulkCopy`. Best for >100 rows. |
| `GetAllModels<T>(table?)` | ✓ | ✓ | Full table read. |
| `GetModelById<T>(table, idColumn, id)` | ✓ | ✓ | Returns `T?`. |
| `UpdateModel<T>(model, table?, idColumn?)` | ✓ | ✓ | `idColumn` defaults to `[Key]`/`[Identity]`. |
| `DeleteModel(table, idColumn, id)` | ✓ | ✓ | |

All async methods accept a trailing `CancellationToken`.

## Options

`SqlServerHelperOptions`:

| Property | Default | Purpose |
| --- | --- | --- |
| `CommandTimeoutSeconds` | 30 | Per-command timeout. |
| `BulkCopyTimeoutSeconds` | 60 | Used by `BulkInsertAsync`. |
| `BulkCopyBatchSize` | 1000 | Used by `BulkInsertAsync`. |
| `LogParameters` | false | Whether to include parameter values in debug logs. |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT — see [LICENSE](LICENSE).
