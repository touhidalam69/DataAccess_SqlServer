using System.Globalization;

namespace TA.DataAccess.SqlServer
{
    internal static class ValueCoercion
    {
        public static object Coerce(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType.IsEnum)
            {
                if (value is string s) return Enum.Parse(targetType, s, ignoreCase: true);
                return Enum.ToObject(targetType, value);
            }

            if (targetType == typeof(Guid))
            {
                return value switch
                {
                    Guid g => g,
                    string str => Guid.Parse(str),
                    byte[] bytes => new Guid(bytes),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to Guid."),
                };
            }

            if (targetType == typeof(DateOnly))
            {
                return value switch
                {
                    DateOnly d => d,
                    DateTime dt => DateOnly.FromDateTime(dt),
                    string str => DateOnly.Parse(str, CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to DateOnly."),
                };
            }

            if (targetType == typeof(TimeOnly))
            {
                return value switch
                {
                    TimeOnly t => t,
                    TimeSpan ts => TimeOnly.FromTimeSpan(ts),
                    DateTime dt => TimeOnly.FromDateTime(dt),
                    string str => TimeOnly.Parse(str, CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to TimeOnly."),
                };
            }

            if (targetType == typeof(DateTimeOffset))
            {
                return value switch
                {
                    DateTimeOffset dto => dto,
                    DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                    string str => DateTimeOffset.Parse(str, CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to DateTimeOffset."),
                };
            }

            if (targetType == typeof(TimeSpan))
            {
                return value switch
                {
                    TimeSpan ts => ts,
                    string str => TimeSpan.Parse(str, CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to TimeSpan."),
                };
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        public static object ToDbValue(object? value)
        {
            if (value is null) return DBNull.Value;
            var type = value.GetType();
            if (type.IsEnum) return Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            if (value is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);
            if (value is TimeOnly t) return t.ToTimeSpan();
            return value;
        }
    }
}
