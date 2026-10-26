using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class InsertReadBackTests
{
    [Table("Big", Schema = "dbo")]
    private sealed class BigIdentityRow
    {
        [Identity] public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Computed] public int Total { get; set; }
    }

    [Fact]
    public void Identity_ReadsComputedAndVersionBackByTheNewId()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<VersionedRow>(), tableName: null, writeBack: true);

        Assert.EndsWith(
            "SELECT [__identity], [__rowversion] FROM @__generated; "
            + "SELECT [Total], [RowVer] FROM [planning].[Docs] WHERE [Id] = (SELECT CAST([__identity] AS int) FROM @__generated);",
            sql);
    }

    [Fact]
    public void Identity_CastMatchesThePropertyType()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<BigIdentityRow>(), tableName: null, writeBack: true);
        Assert.EndsWith("WHERE [Id] = (SELECT CAST([__identity] AS bigint) FROM @__generated);", sql);
    }

    [Fact]
    public void SuppliedKey_ReadsBackByTheKeyParameters()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<DefaultedRow>(), tableName: null, writeBack: true);
        Assert.Equal(
            "INSERT INTO [app].[Settings] ([Code], [Value]) VALUES (@Code, @Value); "
            + "SELECT [CreatedAt] FROM [app].[Settings] WHERE [Code] = @Code;",
            sql);
    }

    [Fact]
    public void ExplicitTableName_IsUsedForTheReadBack()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<DefaultedRow>(), tableName: "app.SettingsArchive", writeBack: true);
        Assert.EndsWith("SELECT [CreatedAt] FROM [app].[SettingsArchive] WHERE [Code] = @Code;", sql);
    }

    [Fact]
    public void NoWayToFindTheRow_NoReadBack()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<KeylessComputedRow>(), tableName: null, writeBack: true);
        Assert.Equal("INSERT INTO [dbo].[Log] ([Message]) VALUES (@Message)", sql);
    }

    [Fact]
    public void WithoutWriteBack_PlainInsert()
    {
        var sql = SqlServerOperations.BuildInsertSql(ModelMetadataCache.Get<VersionedRow>(), tableName: null, writeBack: false);
        Assert.Equal("INSERT INTO [planning].[Docs] ([Code], [CreatedBy]) VALUES (@Code, @CreatedBy)", sql);
    }
}
