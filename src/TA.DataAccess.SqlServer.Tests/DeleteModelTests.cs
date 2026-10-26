using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class DeleteModelTests
{
    [Fact]
    public void Versioned_MatchesKeyAndVersionAndCountsDeletedRows()
    {
        var row = new VersionedRow { Id = 7, RowVer = new byte[8] };
        var (sql, parameters, rowVersion) = SqlServerOperations.BuildDeleteModel(row, tableName: null);

        Assert.NotNull(rowVersion);
        Assert.Equal(
            "DECLARE @__deleted TABLE ([__rowversion] binary(8)); "
            + "DELETE FROM [planning].[Docs] OUTPUT DELETED.[RowVer] INTO @__deleted WHERE [Id] = @Id AND [RowVer] = @RowVer; "
            + "SELECT COUNT_BIG(*) FROM @__deleted;",
            sql);
        Assert.Equal(new[] { "@Id", "@RowVer" }, parameters.Select(p => p.ParameterName));
    }

    [Fact]
    public void Versioned_NullVersion_Throws()
        => Assert.Throws<InvalidOperationException>(() => SqlServerOperations.BuildDeleteModel(new VersionedRow { Id = 7 }, tableName: null));

    [Fact]
    public void CompositeKey_MatchesEveryKeyColumn()
    {
        var (sql, parameters, rowVersion) = SqlServerOperations.BuildDeleteModel(new CompositeRow { OrderId = 5, LineNo = 2 }, tableName: null);

        Assert.Null(rowVersion);
        Assert.Equal("DELETE FROM [sales].[OrderLines] WHERE [OrderId] = @OrderId AND [LineNo] = @LineNo", sql);
        Assert.Equal(new object[] { 5, 2 }, parameters.Select(p => p.Value));
    }

    [Fact]
    public void ExplicitTableName_IsUsed()
    {
        var (sql, _, _) = SqlServerOperations.BuildDeleteModel(new CompositeRow { OrderId = 5, LineNo = 2 }, tableName: "sales.OrderLinesArchive");
        Assert.StartsWith("DELETE FROM [sales].[OrderLinesArchive] WHERE", sql);
    }

    [Fact]
    public void NullKeyValue_Throws()
        => Assert.Throws<InvalidOperationException>(() => SqlServerOperations.BuildDeleteModel(new ProductRow { Sku = null! }, tableName: null));

    [Fact]
    public void NoKey_Throws()
        => Assert.Throws<InvalidOperationException>(() => SqlServerOperations.BuildDeleteModel(new KeylessRow(), tableName: null));
}
