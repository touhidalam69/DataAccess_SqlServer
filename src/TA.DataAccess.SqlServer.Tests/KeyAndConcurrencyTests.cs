using System.Data;
using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class KeyAndConcurrencyTests
{
    [Table("Bad", Schema = "dbo")]
    private sealed class RowVersionNotBytes
    {
        [Key] public int Id { get; set; }
        [RowVersion] public long Version { get; set; }
    }

    [Table("Bad", Schema = "dbo")]
    private sealed class TwoRowVersions
    {
        [Key] public int Id { get; set; }
        [RowVersion] public byte[]? A { get; set; }
        [RowVersion] public byte[]? B { get; set; }
    }

    // ---- metadata ----

    [Fact]
    public void CompositeKey_OrderedByKeyOrder()
    {
        var metadata = ModelMetadataCache.Get<CompositeRow>();
        Assert.Equal(new[] { "OrderId", "LineNo" }, metadata.KeyColumns.Select(k => k.PropertyName));
        Assert.True(metadata.HasCompositeKey);
    }

    [Fact]
    public void CompositeKey_SingleKeyColumnLookup_Throws()
        => Assert.Throws<InvalidOperationException>(() => ModelMetadataCache.Get<CompositeRow>().GetKeyColumn(null));

    [Fact]
    public void ComputedAndRowVersion_AreReadButNeverWritten()
    {
        var metadata = ModelMetadataCache.Get<VersionedRow>();
        Assert.Contains(metadata.Columns, c => c.PropertyName == "Total");
        Assert.Contains(metadata.Columns, c => c.PropertyName == "RowVer");
        Assert.DoesNotContain(metadata.InsertableColumns, c => c.PropertyName is "Total" or "RowVer" or "Id");
        Assert.DoesNotContain(metadata.UpdatableColumns, c => c.PropertyName is "Total" or "RowVer" or "Id");
        Assert.Equal("RowVer", metadata.RowVersionColumn?.PropertyName);
    }

    [Fact]
    public void InsertOnly_IsInsertedButNotUpdated()
    {
        var metadata = ModelMetadataCache.Get<VersionedRow>();
        Assert.Contains(metadata.InsertableColumns, c => c.PropertyName == "CreatedBy");
        Assert.DoesNotContain(metadata.UpdatableColumns, c => c.PropertyName == "CreatedBy");
    }

    [Fact]
    public void RowVersion_MustBeByteArray()
        => Assert.Throws<InvalidOperationException>(() => ModelMetadataCache.Get<RowVersionNotBytes>());

    [Fact]
    public void RowVersion_AtMostOne()
        => Assert.Throws<InvalidOperationException>(() => ModelMetadataCache.Get<TwoRowVersions>());

    // ---- insert ----

    [Fact]
    public void Insert_Versioned_SkipsGeneratedColumnsAndReturnsIdentityAndVersion()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<VersionedRow>(), tableName: null, writeBack: true);

        Assert.Contains("INSERT INTO [planning].[Docs] ([Code], [CreatedBy])", sql);
        Assert.Contains("OUTPUT INSERTED.[Id], INSERTED.[RowVer] INTO @__generated", sql);
        Assert.Contains("SELECT [__identity], [__rowversion] FROM @__generated;", sql);
    }

    // ---- update ----

    [Fact]
    public void Update_Versioned_ChecksVersionAndSkipsGeneratedAndInsertOnly()
    {
        var row = new VersionedRow { Id = 7, Code = "A", Total = 99, CreatedBy = 3, RowVer = new byte[8] };
        var (sql, parameters, rowVersion) = SqlServerOperations.BuildUpdate(row, tableName: null, idColumn: null);

        Assert.NotNull(rowVersion);
        Assert.Contains("UPDATE [planning].[Docs] SET [Code] = @Code OUTPUT INSERTED.[RowVer] INTO @__updated", sql);
        Assert.Contains("WHERE [Id] = @Id AND [RowVer] = @RowVer;", sql);
        Assert.DoesNotContain("[Total] =", sql);
        Assert.DoesNotContain("[CreatedBy] =", sql);
        Assert.Equal(new[] { "@Code", "@Id", "@RowVer" }, parameters.Select(p => p.ParameterName));
    }

    [Fact]
    public void Update_Versioned_NullVersion_Throws()
    {
        var row = new VersionedRow { Id = 7, Code = "A" };
        Assert.Throws<InvalidOperationException>(() => SqlServerOperations.BuildUpdate(row, null, null));
    }

    [Fact]
    public void Update_CompositeKey_MatchesEveryKeyColumn()
    {
        var row = new CompositeRow { OrderId = 5, LineNo = 2, Qty = 10 };
        var (sql, parameters, rowVersion) = SqlServerOperations.BuildUpdate(row, null, null);

        Assert.Null(rowVersion);
        Assert.Equal("UPDATE [sales].[OrderLines] SET [Qty] = @Qty WHERE [OrderId] = @OrderId AND [LineNo] = @LineNo", sql);
        Assert.Equal(new[] { "@Qty", "@OrderId", "@LineNo" }, parameters.Select(p => p.ParameterName));
    }

    [Fact]
    public void Update_ExplicitIdColumn_StillUsesThatColumnOnly()
    {
        var row = new ProductRow { Id = 1, Sku = "X1", Name = "N", Price = 1m };
        var (sql, _, _) = SqlServerOperations.BuildUpdate(row, null, "Name");
        Assert.EndsWith("WHERE [Name] = @Name", sql);
    }

    // ---- by-key ----

    [Fact]
    public void SelectByKey_CompositeKey_UsesAllValuesInKeyOrder()
    {
        var (sql, parameters) = SqlServerOperations.BuildSelectByKey<CompositeRow>(new object[] { 5, 2 });
        Assert.Equal("SELECT * FROM [sales].[OrderLines] WHERE [OrderId] = @Id0 AND [LineNo] = @Id1", sql);
        Assert.Equal(5, parameters[0].Value);
        Assert.Equal(2, parameters[1].Value);
    }

    [Fact]
    public void DeleteByKey_CompositeKey_SingleValue_Throws()
        => Assert.Throws<ArgumentException>(() => SqlServerOperations.BuildDeleteByKey<CompositeRow>(5));

    [Fact]
    public void DeleteByKey_CompositeKey_WrongValueCount_Throws()
        => Assert.Throws<ArgumentException>(() => SqlServerOperations.BuildDeleteByKey<CompositeRow>(new object[] { 5 }));

    [Fact]
    public void Exists_CompositeKey()
    {
        var (sql, _) = SqlServerOperations.BuildExists<CompositeRow>(new object[] { 5, 2 });
        Assert.Equal("SELECT CASE WHEN EXISTS (SELECT 1 FROM [sales].[OrderLines] WHERE [OrderId] = @Id0 AND [LineNo] = @Id1) THEN 1 ELSE 0 END", sql);
    }

    [Fact]
    public void SingleKey_AcceptsOneElementArray()
    {
        var (sql, parameters) = SqlServerOperations.BuildDeleteByKey<ProductRow>(new object[] { "X1" });
        Assert.Equal("DELETE FROM [catalog].[Products] WHERE [sku] = @Id", sql);
        Assert.Equal("X1", parameters[0].Value);
    }

    [Fact]
    public void SingleKey_RejectsSeveralValues()
        => Assert.Throws<ArgumentException>(() => SqlServerOperations.BuildDeleteByKey<ProductRow>(new object[] { "X1", "X2" }));

    // ---- datetime2 option ----

    [Fact]
    public void DateTime_WithDateTime2Option_UsesDateTime2()
        => Assert.Equal(SqlDbType.DateTime2, ParameterFactory.Create("@dt", new DateTime(2024, 1, 2, 3, 4, 5, 1), dateTime2: true).SqlDbType);

    [Fact]
    public void Interpolation_WithDateTime2Option_UsesDateTime2()
    {
        var at = new DateTime(2024, 1, 2, 3, 4, 5, 1);
        var (_, parameters) = SqlServerOperations.ParameterizeInterpolation($"SELECT 1 WHERE {at} > 0", dateTime2: true);
        Assert.Equal(SqlDbType.DateTime2, parameters[0].SqlDbType);
    }
}
