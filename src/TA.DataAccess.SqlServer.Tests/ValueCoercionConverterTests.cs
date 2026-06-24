using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ValueCoercionConverterTests
{
    private static void AssertMatchesCoerce(Type target, object value)
        => Assert.Equal(ValueCoercion.Coerce(value, target), ValueCoercion.BuildConverter(target)(value));

    [Fact] public void Int_FromLong() => AssertMatchesCoerce(typeof(int), 5L);
    [Fact] public void Decimal_FromInt() => AssertMatchesCoerce(typeof(decimal), 5);
    [Fact] public void Enum_FromInt() => AssertMatchesCoerce(typeof(DayOfWeek), 3);
    [Fact] public void Enum_FromString() => AssertMatchesCoerce(typeof(DayOfWeek), "Wednesday");
    [Fact] public void Guid_FromString() => AssertMatchesCoerce(typeof(Guid), "11111111-1111-1111-1111-111111111111");
    [Fact] public void DateOnly_FromDateTime() => AssertMatchesCoerce(typeof(DateOnly), new DateTime(2024, 1, 2, 3, 4, 5));
    [Fact] public void TimeOnly_FromTimeSpan() => AssertMatchesCoerce(typeof(TimeOnly), new TimeSpan(1, 2, 3));
    [Fact] public void DateTimeOffset_FromDateTime() => AssertMatchesCoerce(typeof(DateTimeOffset), new DateTime(2024, 1, 2, 0, 0, 0));
    [Fact] public void TimeSpan_FromString() => AssertMatchesCoerce(typeof(TimeSpan), "01:02:03");
    [Fact] public void String_Passthrough() => AssertMatchesCoerce(typeof(string), "hello");

    [Fact]
    public void ConverterIsReusableAcrossValues()
    {
        var convert = ValueCoercion.BuildConverter(typeof(int));
        Assert.Equal(1, convert(1L));
        Assert.Equal(2, convert("2"));
    }
}
