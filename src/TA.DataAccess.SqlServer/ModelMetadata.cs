using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace TA.DataAccess.SqlServer
{
    internal sealed class ColumnBinding
    {
        public required string PropertyName { get; init; }
        public required string ColumnName { get; init; }
        public required Type PropertyType { get; init; }
        public required Type UnderlyingType { get; init; }
        public required Func<object, object?> Getter { get; init; }
        public required Action<object, object?> Setter { get; init; }
        public bool IsIdentity { get; init; }
        public bool IsKey { get; init; }
    }

    internal sealed class ModelMetadata
    {
        public required Type ModelType { get; init; }
        public required string? TableName { get; init; }
        public required string? Schema { get; init; }
        public required ColumnBinding[] Columns { get; init; }
        public required ColumnBinding[] InsertableColumns { get; init; }
        public required ColumnBinding? KeyColumn { get; init; }

        /// <summary>
        /// Resolves the column used as the key for an update/lookup. When <paramref name="idColumn"/>
        /// is null, falls back to the [Key]/[Identity] column. Otherwise matches a binding by either
        /// its property name or its mapped column name, so callers can pass whichever they have.
        /// </summary>
        public ColumnBinding GetKeyColumn(string? idColumn)
        {
            if (idColumn is null)
                return KeyColumn ?? throw new InvalidOperationException(
                    $"Type '{ModelType.FullName}' has no [Key] or [Identity] property; pass idColumn explicitly.");

            return Columns.FirstOrDefault(c => c.PropertyName == idColumn || c.ColumnName == idColumn)
                ?? throw new ArgumentException(
                    $"No property or column named '{idColumn}' on type '{ModelType.FullName}'.", nameof(idColumn));
        }
    }

    [RequiresUnreferencedCode("Reads public instance properties of T via reflection. Preserve T members when trimming.")]
    internal static class ModelMetadataCache
    {
        private static readonly ConcurrentDictionary<Type, ModelMetadata> Cache = new();

        public static ModelMetadata Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
            => Cache.GetOrAdd(typeof(T), Build);

        private static ModelMetadata Build(Type type)
        {
            var tableAttribute = type.GetCustomAttribute<TableAttribute>();

            var properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<NoCrudAttribute>() == null && p.CanRead && p.CanWrite)
                .ToArray();

            var columns = properties.Select(BuildBinding).ToArray();
            var insertable = columns.Where(c => !c.IsIdentity).ToArray();
            var key = columns.FirstOrDefault(c => c.IsKey)
                      ?? columns.FirstOrDefault(c => c.IsIdentity);

            return new ModelMetadata
            {
                ModelType = type,
                TableName = tableAttribute?.Name,
                Schema = tableAttribute?.Schema,
                Columns = columns,
                InsertableColumns = insertable,
                KeyColumn = key,
            };
        }

        private static ColumnBinding BuildBinding(PropertyInfo property)
        {
            var column = property.GetCustomAttribute<ColumnAttribute>();
            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            return new ColumnBinding
            {
                PropertyName = property.Name,
                ColumnName = column?.Name ?? property.Name,
                PropertyType = property.PropertyType,
                UnderlyingType = underlying,
                Getter = CompileGetter(property),
                Setter = CompileSetter(property),
                IsIdentity = property.GetCustomAttribute<IdentityAttribute>() != null,
                IsKey = property.GetCustomAttribute<KeyAttribute>() != null,
            };
        }

        private static Func<object, object?> CompileGetter(PropertyInfo property)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var cast = Expression.Convert(target, property.DeclaringType!);
            var access = Expression.Property(cast, property);
            var box = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object, object?>>(box, target).Compile();
        }

        private static Action<object, object?> CompileSetter(PropertyInfo property)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            var castTarget = Expression.Convert(target, property.DeclaringType!);
            var castValue = Expression.Convert(value, property.PropertyType);
            var assign = Expression.Call(castTarget, property.GetSetMethod(true)!, castValue);
            return Expression.Lambda<Action<object, object?>>(assign, target, value).Compile();
        }
    }
}
