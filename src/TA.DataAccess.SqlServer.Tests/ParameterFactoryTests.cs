using System.Data;
using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ParameterFactoryTests
{
    [Fact]
    public void ShortString_UsesNVarCharBucket4000()
    {
        var p = ParameterFactory.Create("@s", "abc");
        Assert.Equal(SqlDbType.NVarChar, p.SqlDbType);
        Assert.Equal(4000, p.Size);
        Assert.Equal("abc", p.Value);
    }

    [Fact]
    public void LongString_UsesMaxSize()
    {
        var p = ParameterFactory.Create("@s", new string('x', 5000));
        Assert.Equal(SqlDbType.NVarChar, p.SqlDbType);
        Assert.Equal(-1, p.Size);
    }

    [Fact]
    public void Bool_UsesBit()
        => Assert.Equal(SqlDbType.Bit, ParameterFactory.Create("@b", true).SqlDbType);

    [Fact]
    public void Guid_UsesUniqueIdentifier()
        => Assert.Equal(SqlDbType.UniqueIdentifier, ParameterFactory.Create("@g", Guid.NewGuid()).SqlDbType);

    [Fact]
    public void ByteArray_UsesVarBinaryBucket()
    {
        var p = ParameterFactory.Create("@bin", new byte[10]);
        Assert.Equal(SqlDbType.VarBinary, p.SqlDbType);
        Assert.Equal(8000, p.Size);
    }

    [Fact]
    public void Null_BecomesDbNull()
    {
        var p = ParameterFactory.Create("@n", null);
        Assert.Equal(DBNull.Value, p.Value);
    }

    [Fact]
    public void Decimal_LeftToInference()
        => Assert.Equal(SqlDbType.Decimal, ParameterFactory.Create("@d", 1.5m).SqlDbType);

    [Fact]
    public void DateTime_LeftToInference_NotSwitchedToDateTime2()
        => Assert.Equal(SqlDbType.DateTime, ParameterFactory.Create("@dt", new DateTime(2024, 1, 2, 3, 4, 5)).SqlDbType);
}
