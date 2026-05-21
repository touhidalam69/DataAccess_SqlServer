using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("Users", "[Users]")]
    [InlineData("dbo.Users", "[dbo].[Users]")]
    [InlineData("Order_Items", "[Order_Items]")]
    [InlineData("_Underscore1", "[_Underscore1]")]
    public void Quote_ValidIdentifier_BracketsIt(string input, string expected)
    {
        Assert.Equal(expected, Identifier.Quote(input));
    }

    [Theory]
    [InlineData("Users; DROP TABLE Users")]
    [InlineData("Users--")]
    [InlineData("1Users")]
    [InlineData("Users Names")]
    [InlineData("[Users]")]
    [InlineData("")]
    [InlineData("   ")]
    public void Quote_InvalidIdentifier_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => Identifier.Quote(input));
    }
}
