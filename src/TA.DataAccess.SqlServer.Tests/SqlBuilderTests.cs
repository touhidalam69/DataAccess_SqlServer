using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class SqlBuilderTests
{
    [Fact]
    public void Insert_WithIdentity_IncludesOutputClause()
    {
        var metadata = ModelMetadataCache.Get<ProductRow>();
        var sql = SqlServerOperations.BuildInsertSql(metadata, tableName: null, includeIdentityOutput: true);

        Assert.StartsWith("INSERT INTO [catalog].[Products]", sql);
        Assert.Contains("OUTPUT INSERTED.[Id]", sql);
        // Identity is excluded from the column/value lists; NoCrud is excluded entirely.
        Assert.DoesNotContain("[Id],", sql);
        Assert.DoesNotContain("Note", sql);
    }

    [Fact]
    public void Insert_WithoutOutputFlag_OmitsOutput()
    {
        var metadata = ModelMetadataCache.Get<ProductRow>();
        var sql = SqlServerOperations.BuildInsertSql(metadata, tableName: null, includeIdentityOutput: false);
        Assert.DoesNotContain("OUTPUT", sql);
    }

    [Fact]
    public void Insert_NoIdentity_OmitsOutputEvenWhenRequested()
    {
        var metadata = ModelMetadataCache.Get<KeylessRow>();
        var sql = SqlServerOperations.BuildInsertSql(metadata, tableName: null, includeIdentityOutput: true);
        Assert.DoesNotContain("OUTPUT", sql);
    }

    [Fact]
    public void SelectByKey_ResolvesTableAndKey_NoDoubleQuoting()
    {
        var (sql, parameters) = SqlServerOperations.BuildSelectByKey<ProductRow>("X1");
        Assert.Equal("SELECT * FROM [catalog].[Products] WHERE [sku] = @Id", sql);
        Assert.Single(parameters);
        Assert.Equal("@Id", parameters[0].ParameterName);
        Assert.DoesNotContain("[[", sql);
    }

    [Fact]
    public void DeleteByKey_ResolvesTableAndKey()
    {
        var (sql, _) = SqlServerOperations.BuildDeleteByKey<ProductRow>("X1");
        Assert.Equal("DELETE FROM [catalog].[Products] WHERE [sku] = @Id", sql);
    }

    [Fact]
    public void Exists_BuildsCaseExistsAgainstKey()
    {
        var (sql, _) = SqlServerOperations.BuildExists<ProductRow>("X1");
        Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM [catalog].[Products] WHERE [sku] = @Id) THEN 1 ELSE 0 END",
            sql);
    }

    [Fact]
    public void Paged_AppendsOffsetFetchAndComputesOffset()
    {
        var sql = SqlServerOperations.BuildPagedSql("SELECT * FROM T ORDER BY Id", page: 3, pageSize: 25, parameters: null, out var merged);

        Assert.Equal("SELECT * FROM T ORDER BY Id OFFSET @__offset ROWS FETCH NEXT @__fetch ROWS ONLY", sql);
        Assert.Equal(2, merged.Length);
        Assert.Equal(50, merged[0].Value);   // (3 - 1) * 25
        Assert.Equal(25, merged[1].Value);
    }

    [Fact]
    public void Paged_MergesExistingParameters()
    {
        var existing = new[] { new Microsoft.Data.SqlClient.SqlParameter("@name", "abc") };
        SqlServerOperations.BuildPagedSql("SELECT * FROM T ORDER BY Id", 1, 10, existing, out var merged);
        Assert.Equal(3, merged.Length);
        Assert.Equal("@name", merged[0].ParameterName);
        Assert.Equal("@__offset", merged[1].ParameterName);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 10)]
    public void Paged_InvalidArguments_Throw(int page, int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SqlServerOperations.BuildPagedSql("SELECT 1 ORDER BY 1", page, pageSize, null, out _));
    }
}
