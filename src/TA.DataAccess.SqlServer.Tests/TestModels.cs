namespace TA.DataAccess.SqlServer.Tests;

[Table("Products", Schema = "catalog")]
internal sealed class ProductRow
{
    [Identity]
    public int Id { get; set; }
    [Key]
    [Column("sku")]
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    [NoCrud]
    public string Note { get; set; } = string.Empty;
}

// No [Identity] and no [Key].
internal sealed class KeylessRow
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

// Composite key declared out of key order, to check [Key(Order)] wins over declaration order.
[Table("OrderLines", Schema = "sales")]
internal sealed class CompositeRow
{
    [Key(Order = 2)]
    public int LineNo { get; set; }
    [Key(Order = 1)]
    public int OrderId { get; set; }
    public int Qty { get; set; }
}

[Table("Docs", Schema = "planning")]
internal sealed class VersionedRow
{
    [Identity]
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    [Computed]
    public int Total { get; set; }
    [InsertOnly]
    public int CreatedBy { get; set; }
    [RowVersion]
    public byte[]? RowVer { get; set; }
}
