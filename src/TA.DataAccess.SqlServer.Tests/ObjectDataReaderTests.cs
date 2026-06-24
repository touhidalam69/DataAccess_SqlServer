using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ObjectDataReaderTests
{
    private static readonly List<ProductRow> Rows = new()
    {
        new ProductRow { Sku = "A", Name = "n1", Price = 1m },
        new ProductRow { Sku = "B", Name = "n2", Price = 2m },
    };

    [Fact]
    public void FieldCount_MatchesInsertableColumns()
    {
        var columns = ModelMetadataCache.Get<ProductRow>().InsertableColumns;
        using var reader = new ObjectDataReader<ProductRow>(Rows, columns);
        Assert.Equal(columns.Length, reader.FieldCount);
    }

    [Fact]
    public void GetOrdinal_IsCaseInsensitive_AndNamesMatchColumns()
    {
        var columns = ModelMetadataCache.Get<ProductRow>().InsertableColumns;
        using var reader = new ObjectDataReader<ProductRow>(Rows, columns);
        var ordinal = reader.GetOrdinal("SKU");
        Assert.Equal("sku", reader.GetName(ordinal));
    }

    [Fact]
    public void Read_IteratesAllRows_AndCounts()
    {
        var columns = ModelMetadataCache.Get<ProductRow>().InsertableColumns;
        using var reader = new ObjectDataReader<ProductRow>(Rows, columns);
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
        Assert.Equal(2, reader.RowsRead);
    }

    [Fact]
    public void GetValue_ReturnsCurrentRowValues()
    {
        var columns = ModelMetadataCache.Get<ProductRow>().InsertableColumns;
        using var reader = new ObjectDataReader<ProductRow>(Rows, columns);

        Assert.True(reader.Read());
        Assert.Equal("A", reader.GetValue(reader.GetOrdinal("sku")));
        Assert.Equal("n1", reader.GetValue(reader.GetOrdinal("Name")));
        Assert.Equal(1m, reader.GetValue(reader.GetOrdinal("Price")));
    }
}
