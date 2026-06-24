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
