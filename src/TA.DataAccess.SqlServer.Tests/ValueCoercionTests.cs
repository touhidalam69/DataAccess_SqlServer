using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ValueCoercionTests
{
    [Fact]
    public void Coerce_IntToLong_Works()
    {
        Assert.Equal(42L, ValueCoercion.Coerce(42, typeof(long)));
    }

    [Fact]
    public void Coerce_StringToGuid_Works()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, ValueCoercion.Coerce(guid.ToString(), typeof(Guid)));
    }

    [Fact]
    public void Coerce_DateTimeToDateOnly_Works()
    {
        var dt = new DateTime(2026, 5, 21, 10, 30, 0);
        Assert.Equal(new DateOnly(2026, 5, 21), ValueCoercion.Coerce(dt, typeof(DateOnly)));
    }

    [Fact]
    public void Coerce_TimeSpanToTimeOnly_Works()
    {
        var ts = new TimeSpan(13, 45, 0);
        Assert.Equal(new TimeOnly(13, 45), ValueCoercion.Coerce(ts, typeof(TimeOnly)));
    }

    private enum Color { Red = 1, Green = 2, Blue = 3 }

    [Fact]
    public void Coerce_IntToEnum_Works()
    {
        Assert.Equal(Color.Green, ValueCoercion.Coerce(2, typeof(Color)));
    }

    [Fact]
    public void Coerce_StringToEnum_Works()
    {
        Assert.Equal(Color.Blue, ValueCoercion.Coerce("blue", typeof(Color)));
    }

    [Fact]
    public void ToDbValue_Null_ReturnsDbNull()
    {
        Assert.Equal(DBNull.Value, ValueCoercion.ToDbValue(null));
    }

    [Fact]
    public void ToDbValue_DateOnly_ConvertsToDateTime()
    {
        var d = new DateOnly(2026, 1, 15);
        var result = ValueCoercion.ToDbValue(d);
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2026, 1, 15), result);
    }

    [Fact]
    public void ToDbValue_Enum_ReturnsUnderlying()
    {
        var result = ValueCoercion.ToDbValue(Color.Red);
        Assert.Equal(1, result);
    }
}
