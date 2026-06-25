using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ParameterizeInterpolationTests
{
    [Fact]
    public void SingleStatement_SubstitutesPositionalParameterNames()
    {
        decimal price = 9.99m;
        string sku = "X1";
        FormattableString query = $"UPDATE Products SET Price = {price} WHERE Sku = {sku}";

        var (sql, parameters) = SqlServerHelper.ParameterizeInterpolation(query);

        Assert.Equal("UPDATE Products SET Price = @p0 WHERE Sku = @p1", sql);
        Assert.Equal(2, parameters.Length);
        Assert.Equal("@p0", parameters[0].ParameterName);
        Assert.Equal(price, parameters[0].Value);
        Assert.Equal("@p1", parameters[1].ParameterName);
        Assert.Equal(sku, parameters[1].Value);
    }

    [Fact]
    public void LiteralOnly_ProducesNoParametersAndUnchangedSql()
    {
        FormattableString query = $"DELETE FROM Products";

        var (sql, parameters) = SqlServerHelper.ParameterizeInterpolation(query);

        Assert.Equal("DELETE FROM Products", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void SelectQuery_ParameterizesFilterWithTypedParameter()
    {
        string name = "Widget";
        FormattableString query = $"SELECT * FROM catalog.Products WHERE Name = {name}";

        var (sql, parameters) = SqlServerHelper.ParameterizeInterpolation(query);

        Assert.Equal("SELECT * FROM catalog.Products WHERE Name = @p0", sql);
        Assert.Single(parameters);
        Assert.Equal("@p0", parameters[0].ParameterName);
        Assert.Equal(name, parameters[0].Value);
        // string parameters are given a plan-cache-friendly NVarChar type by ParameterFactory.
        Assert.Equal(System.Data.SqlDbType.NVarChar, parameters[0].SqlDbType);
    }

    [Fact]
    public void IndependentCalls_EachRenumberFromP0()
    {
        FormattableString first = $"UPDATE A SET V = {1}";
        FormattableString second = $"UPDATE B SET V = {2}";

        var (firstSql, firstParams) = SqlServerHelper.ParameterizeInterpolation(first);
        var (secondSql, secondParams) = SqlServerHelper.ParameterizeInterpolation(second);

        // Each statement re-numbers from @p0, so a shared command that clears
        // parameters between statements never collides.
        Assert.Equal("UPDATE A SET V = @p0", firstSql);
        Assert.Equal("UPDATE B SET V = @p0", secondSql);
        Assert.Equal("@p0", firstParams[0].ParameterName);
        Assert.Equal("@p0", secondParams[0].ParameterName);
        Assert.Equal(1, firstParams[0].Value);
        Assert.Equal(2, secondParams[0].Value);
    }
}
