# TA.DataAccess.SqlServer

`TA.DataAccess.SqlServer` is a lightweight, strongly-typed data access helper for SQL Server on modern .NET.

Targets: **net8.0**, **net9.0**, **net10.0**.

## Features

- Sync + async CRUD with `CancellationToken` on every async path
- Transactions / unit-of-work — run many operations atomically (`BeginTransaction`)
- Stored procedures with input + output parameters (`ExecuteProcedure`, `QueryProcedure`)
- Identifier hardening (`[bracketed]` + regex validation) against injection
- `FormattableString` parameterized API, single or batch (`ExecuteInterpolated`)
- Scalar queries (`ExecuteScalar<T>`), `Count<T>`, `Exists<T>`
- `InsertModel` writes the generated `[Identity]` value back onto the model
- Attribute-resolved by-key CRUD (`GetModelByKey<T>`, `DeleteByKey<T>`)
- Paging via `OFFSET/FETCH` (`SelectPaged<T>`)
- `IAsyncEnumerable<T>` streaming reads (`SelectStreamAsync`)
- `SqlBulkCopy` for big inserts, streamed with no `DataTable` (`BulkInsertAsync`)
- Attribute mapping: `[Table]`, `[Column]`, `[Key]`, `[Identity]`, `[NoCrud]`
- Expanded type coercion: `DateOnly`, `TimeOnly`, `DateTimeOffset`, `Guid`, enums
- Cached compiled getters/setters + per-column coercion delegates via expression trees
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
public class Product
{
    [Identity] public int Id { get; set; }
    [Key, Column("sku")] public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateOnly ReleasedOn { get; set; }
    [NoCrud] public string InternalNote { get; set; } = "";
}
```

Without `[Table]`, the type name is used. Without `[Column]`, the property name is used. `[NoCrud]` properties are skipped. `[Identity]` is excluded from inserts and its generated value is written back onto the model after `InsertModel`/`InsertModelAsync`. `[Key]` (or `[Identity]` as fallback) drives `UpdateModel`, `GetModelByKey`, `DeleteByKey`, and `Exists` when `idColumn` is omitted. When `idColumn` is supplied to `UpdateModel`, it matches either the property name or the mapped column name.

## Quick examples

```csharp
// parameterized interpolated query — safe
await helper.ExecuteInterpolatedAsync($"UPDATE Products SET Price = {newPrice} WHERE Sku = {sku}");

// parameterized batch — atomic, all-or-nothing in one transaction
await helper.ExecuteInterpolatedAsync(new FormattableString[]
{
    $"UPDATE Products SET Price = {newPrice} WHERE Sku = {sku}",
    $"INSERT INTO Audit (Message) VALUES ({msg})",
});

// streaming read — no DataTable, low memory
await foreach (var product in helper.SelectStreamAsync<Product>("SELECT * FROM catalog.Products"))
    Process(product);

// bulk insert
await helper.BulkInsertAsync(products, cancellationToken: token);

// CRUD using attribute-resolved table + key
await helper.InsertModelAsync(new Product { Sku = "X1", Name = "Widget", Price = 9.99m });
var p = await helper.GetModelByKeyAsync<Product>("X1");   // table + key resolved from attributes
p!.Price = 12.50m;
await helper.UpdateModelAsync(p);

// generated identity is written back onto the model
var order = new Order { CustomerId = 42 };
await helper.InsertModelAsync(order);
Console.WriteLine(order.Id);   // populated from OUTPUT INSERTED

// scalar, count, exists
long total  = await helper.CountAsync<Product>();
bool exists = await helper.ExistsAsync<Product>("X1");
decimal max = await helper.ExecuteScalarAsync<decimal>("SELECT MAX(Price) FROM catalog.Products");

// paging (query must include ORDER BY)
var page2 = await helper.SelectPagedAsync<Product>(
    "SELECT * FROM catalog.Products ORDER BY Sku", page: 2, pageSize: 50);

// stored procedure with an output parameter
var outParam = new SqlParameter("@Total", SqlDbType.Int) { Direction = ParameterDirection.Output };
var rows = await helper.QueryProcedureAsync<Product>("catalog.GetProducts",
    new[] { new SqlParameter("@MinPrice", 10m), outParam });
int total2 = (int)outParam.Value!;

// transaction / unit of work — all-or-nothing across multiple operations
await using var uow = await helper.BeginTransactionAsync();
try
{
    await uow.InsertModelAsync(new Product { Sku = "X2", Name = "Gadget", Price = 5m });
    await uow.ExecuteInterpolatedAsync($"UPDATE catalog.Products SET Price = {0m} WHERE Sku = {"X1"}");
    await uow.CommitAsync();
}
catch
{
    await uow.RollbackAsync();   // (disposal also rolls back if not committed)
    throw;
}
```

## API reference

| Method | Sync | Async | Notes |
| --- | --- | --- | --- |
| `ExecuteNonQuery(string, params)` | ✓ | ✓ | INSERT/UPDATE/DELETE/DDL. Returns rows affected. |
| `ExecuteNonQuery(IReadOnlyList<string>)` | ✓ | ✓ | Multiple statements in one transaction. |
| `ExecuteInterpolated(FormattableString)` | ✓ | ✓ | Auto-parameterizes interpolation arguments. |
| `ExecuteInterpolated(IReadOnlyList<FormattableString>)` | ✓ | ✓ | Parameterized batch in one transaction. |
| `ExecuteScalar<T>(string, params)` | ✓ | ✓ | Single value, coerced to `T`. Also `ExecuteScalarInterpolated<T>`. |
| `Select(string, params)` | ✓ | ✓ | Returns `DataTable`. |
| `Select<T>(string, params)` | ✓ | ✓ | Returns `List<T>`. |
| `SelectPaged<T>(query, page, pageSize, params)` | ✓ | ✓ | `OFFSET/FETCH`. Query must include `ORDER BY`. |
| `SelectStreamAsync<T>(string, params)` | — | ✓ | Returns `IAsyncEnumerable<T>`. |
| `InsertModel<T>(model, table?)` | ✓ | ✓ | Single insert. Skips `[Identity]`, writes the generated value back onto the model. |
| `InsertModels<T>(list, table?)` | ✓ | ✓ | Transactional batch (no identity write-back). |
| `BulkInsertAsync<T>(list \| IEnumerable, table?)` | — | ✓ | Streamed `SqlBulkCopy`. Best for >100 rows. |
| `GetAllModels<T>(table?)` | ✓ | ✓ | Full table read. |
| `GetModelById<T>(table, idColumn, id)` | ✓ | ✓ | Returns `T?`. |
| `GetModelByKey<T>(id)` | ✓ | ✓ | `T?`; table + key resolved from attributes. |
| `Count<T>(table?)` | ✓ | ✓ | `SELECT COUNT_BIG(*)` → `long`. |
| `Exists<T>(id)` | ✓ | ✓ | Existence check by `[Key]`/`[Identity]`. |
| `UpdateModel<T>(model, table?, idColumn?)` | ✓ | ✓ | `idColumn` defaults to `[Key]`/`[Identity]`. |
| `DeleteModel(table, idColumn, id)` | ✓ | ✓ | |
| `DeleteByKey<T>(id)` | ✓ | ✓ | Table + key resolved from attributes. |
| `ExecuteProcedure(name, params)` | ✓ | ✓ | Rows affected; output params readable after. |
| `QueryProcedure<T>(name, params)` | ✓ | ✓ | Maps result set to `List<T>`. Also `DataTable` overload. |
| `ExecuteProcedureScalar<T>(name, params)` | ✓ | ✓ | Single value from a proc. |
| `BeginTransaction(isolationLevel?)` | ✓ | ✓ | Returns `ISqlServerUnitOfWork` (run ops atomically). |

All async methods accept a trailing `CancellationToken`.

## Transactions

`BeginTransaction`/`BeginTransactionAsync` return an `ISqlServerUnitOfWork` that
exposes the same operations on one shared connection + transaction. Commit to
persist; disposing without committing rolls back.

```csharp
await using var uow = await helper.BeginTransactionAsync();
await uow.InsertModelAsync(order);
await uow.InsertModelsAsync(order.Lines);
await uow.CommitAsync();
```

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
