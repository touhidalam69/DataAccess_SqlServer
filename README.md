# TA.DataAccess.SqlServer

`TA.DataAccess.SqlServer` is a lightweight, strongly-typed data access helper for SQL Server on modern .NET.

Targets: **net8.0**, **net9.0**, **net10.0**.

## Features

- Sync + async CRUD with `CancellationToken` on every async path
- Transactions / unit-of-work — run many operations atomically (`BeginTransaction`)
- Stored procedures with input + output parameters (`ExecuteProcedure`, `QueryProcedure`)
- Identifier hardening (`[bracketed]` + regex validation) against injection
- `FormattableString` parameterized API for writes and reads (`ExecuteInterpolated`, `SelectInterpolated`)
- Scalar queries (`ExecuteScalar<T>`), `Count<T>`, `Exists<T>`
- `InsertModel` writes the generated `[Identity]` and `[RowVersion]` values back onto the model, including on tables with triggers
- Attribute-resolved by-key CRUD (`GetModelByKey<T>`, `DeleteByKey<T>`), with composite keys
- Optimistic concurrency with `[RowVersion]` (`UpdateModel` throws `DBConcurrencyException` on a stale version)
- `[Computed]` (database-filled) and `[InsertOnly]` columns
- Multiple result sets in one round trip (`SelectMultiple<T1, T2>` / `<T1, T2, T3>`)
- Paging via `OFFSET/FETCH` (`SelectPaged<T>`)
- `IAsyncEnumerable<T>` streaming reads (`SelectStreamAsync`)
- `SqlBulkCopy` for big inserts, streamed with no `DataTable` (`BulkInsertAsync`)
- Attribute mapping: `[Table]`, `[Column]`, `[Key]`, `[Identity]`, `[Computed]`, `[RowVersion]`, `[InsertOnly]`, `[NoCrud]`
- Optional `datetime2` parameters for `DateTime` values (`UseDateTime2`)
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

| Attribute | Read | Inserted | Updated | Use for |
| --- | --- | --- | --- | --- |
| *(none)* | ✓ | ✓ | ✓ | Ordinary columns |
| `[Identity]` | ✓ | — | — | `IDENTITY` key; written back after insert |
| `[Key]` | ✓ | ✓ | — (used in `WHERE`) | Key column; several make a composite key, ordered by `[Key(Order = n)]` |
| `[Computed]` | ✓ | — | — | Computed columns, and columns left to a database default |
| `[RowVersion]` | ✓ | — | — (checked in `WHERE`) | `rowversion` column (`byte[]`); written back after insert and update |
| `[InsertOnly]` | ✓ | ✓ | — | `CreatedAt`, `CreatedBy` |
| `[NoCrud]` | — | — | — | Properties that are not columns |

```csharp
[Table("ProductionPlan", Schema = "planning")]
public class Plan
{
    [Identity] public int PlanId { get; set; }
    public string Title { get; set; } = "";
    public int Styles { get; set; }
    public int PiecesPerStyle { get; set; }
    [Computed] public int PlannedQty { get; set; }      // computed column in SQL Server
    [Computed] public DateTime CreatedAt { get; set; }  // DEFAULT SYSUTCDATETIME()
    [InsertOnly] public int CreatedBy { get; set; }
    [RowVersion] public byte[]? RowVer { get; set; }
}
```

### Composite keys

Mark every key column with `[Key]`. `UpdateModel` matches all of them. The by-key methods take an
`object[]` with one value per key column, in key order; passing a single value for a composite key
throws `ArgumentException` instead of matching on part of the key.

```csharp
[Table("PlanLineSizeColor", Schema = "planning")]
public class SizeColorQty
{
    [Key(Order = 1)] public int PlanLineId { get; set; }
    [Key(Order = 2)] public int ProductSizeId { get; set; }
    [Key(Order = 3)] public int ProductColorId { get; set; }
    public int Quantity { get; set; }
}

var row = helper.GetModelByKey<SizeColorQty>(new object[] { lineId, sizeId, colorId });
helper.DeleteByKey<SizeColorQty>(new object[] { lineId, sizeId, colorId });
```

### Optimistic concurrency

With a `[RowVersion]` property, `UpdateModel` updates only if the stored version still equals the
model's. Otherwise it throws `System.Data.DBConcurrencyException` and changes nothing. On success the
new version is written back onto the model, so the same instance can be updated again. The version
must be loaded (or carried from the client) before updating; a null version throws.

```csharp
var plan = helper.GetModelByKey<Plan>(id)!;
plan.Title = "Summer 2027";
try
{
    helper.UpdateModel(plan);
}
catch (DBConcurrencyException)
{
    // someone else changed or deleted the row: reload and retry, or tell the user
}
```

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

// parameterized read — safe, no manual SqlParameter[]
var pricey = await helper.SelectInterpolatedAsync<Product>(
    $"SELECT * FROM catalog.Products WHERE Price > {minPrice}");

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
Console.WriteLine(order.Id);   // populated from the inserted row (works on tables with triggers)

// header + lines in one round trip
var (headers, lines) = await helper.SelectMultipleInterpolatedAsync<Order, OrderLine>(
    $"SELECT * FROM sales.Orders WHERE Id = {order.Id}; SELECT * FROM sales.OrderLines WHERE OrderId = {order.Id};");

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
| `SelectInterpolated<T>(FormattableString)` | ✓ | ✓ | Parameterized read → `List<T>`. Also non-generic `DataTable` overload. |
| `SelectPaged<T>(query, page, pageSize, params)` | ✓ | ✓ | `OFFSET/FETCH`. Query must include `ORDER BY`. |
| `SelectStreamAsync<T>(string, params)` | — | ✓ | Returns `IAsyncEnumerable<T>`. |
| `SelectStreamInterpolatedAsync<T>(FormattableString)` | — | ✓ | Parameterized streaming read. |
| `SelectMultiple<T1, T2[, T3]>(string, params)` | ✓ | ✓ | 2 or 3 result sets in one round trip. Also `SelectMultipleInterpolated`. Throws if fewer sets come back. |
| `InsertModel<T>(model, table?)` | ✓ | ✓ | Single insert. Skips `[Identity]`/`[Computed]`/`[RowVersion]`, writes generated identity and rowversion back onto the model. |
| `InsertModels<T>(list, table?)` | ✓ | ✓ | Transactional batch (no identity write-back). |
| `BulkInsertAsync<T>(list \| IEnumerable, table?)` | — | ✓ | Streamed `SqlBulkCopy`. Best for >100 rows. |
| `GetAllModels<T>(table?)` | ✓ | ✓ | Full table read. |
| `GetModelById<T>(table, idColumn, id)` | ✓ | ✓ | Returns `T?`. |
| `GetModelByKey<T>(id)` | ✓ | ✓ | `T?`; table + key resolved from attributes. Composite key: `object[]` of key values. |
| `Count<T>(table?)` | ✓ | ✓ | `SELECT COUNT_BIG(*)` → `long`. |
| `Exists<T>(id)` | ✓ | ✓ | Existence check by `[Key]`/`[Identity]`. Composite key: `object[]`. |
| `UpdateModel<T>(model, table?, idColumn?)` | ✓ | ✓ | `idColumn` defaults to all `[Key]` columns (or `[Identity]`). Checks `[RowVersion]`; throws `DBConcurrencyException` on conflict. |
| `DeleteModel(table, idColumn, id)` | ✓ | ✓ | |
| `DeleteByKey<T>(id)` | ✓ | ✓ | Table + key resolved from attributes. Composite key: `object[]`. |
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

Stored procedures can run inside the unit of work, including procedures that open their own
transaction with `SET XACT_ABORT ON`. If one fails, SQL Server rolls back the whole transaction, the
original `SqlException` reaches the caller, and disposing the unit of work is still safe.

## Options

`SqlServerHelperOptions`:

| Property | Default | Purpose |
| --- | --- | --- |
| `CommandTimeoutSeconds` | 30 | Per-command timeout. |
| `BulkCopyTimeoutSeconds` | 60 | Used by `BulkInsertAsync`. |
| `BulkCopyBatchSize` | 1000 | Used by `BulkInsertAsync`. |
| `LogParameters` | false | Whether to include parameter values in debug logs. |
| `UseDateTime2` | false | Send `DateTime` values that the library parameterizes as `datetime2` (full precision and range). Turn on for `datetime2` columns; the default `datetime` rounds to 1/300 s and rejects dates before 1753. |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT — see [LICENSE](LICENSE).
