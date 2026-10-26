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
        public required Func<object, object?> Convert { get; init; }
        public bool IsIdentity { get; init; }
        public bool IsKey { get; init; }
        public int KeyOrder { get; init; }

        /// <summary>Filled by the database: read, never inserted or updated ([Computed] or [RowVersion]).</summary>
        public bool IsComputed { get; init; }
        public bool IsRowVersion { get; init; }
        public bool IsInsertOnly { get; init; }
    }

    internal sealed class ModelMetadata
    {
        public required Type ModelType { get; init; }
        public required string? TableName { get; init; }
        public required string? Schema { get; init; }
        public required ColumnBinding[] Columns { get; init; }
        public required ColumnBinding[] InsertableColumns { get; init; }

        /// <summary>Columns an update may set (before excluding the key columns used in its WHERE).</summary>
        public required ColumnBinding[] UpdatableColumns { get; init; }

        /// <summary>The [Key] columns in key order; the [Identity] column when there is no [Key]; empty when neither.</summary>
        public required ColumnBinding[] KeyColumns { get; init; }
        public required ColumnBinding? IdentityColumn { get; init; }
        public required ColumnBinding? RowVersionColumn { get; init; }

        /// <summary>The first key column (the only one unless the key is composite).</summary>
        public ColumnBinding? KeyColumn => KeyColumns.Length > 0 ? KeyColumns[0] : null;

        public bool HasCompositeKey => KeyColumns.Length > 1;

        /// <summary>
        /// Resolves the single column used as the key for an update/lookup. When <paramref name="idColumn"/>
        /// is null, falls back to the [Key]/[Identity] column. Otherwise matches a binding by either
        /// its property name or its mapped column name, so callers can pass whichever they have.
        /// </summary>
        public ColumnBinding GetKeyColumn(string? idColumn)
        {
            var columns = GetKeyColumns(idColumn);
            if (columns.Length > 1)
                throw new InvalidOperationException(
                    $"Type '{ModelType.FullName}' has a composite key ({KeyColumnList}); a single key column cannot be used.");
            return columns[0];
        }

        /// <summary>
        /// Resolves the key columns for an update/lookup: all [Key] columns (or the [Identity] column)
        /// when <paramref name="idColumn"/> is null, otherwise the one column it names.
        /// </summary>
        public ColumnBinding[] GetKeyColumns(string? idColumn)
        {
            if (idColumn is null)
                return KeyColumns.Length > 0
                    ? KeyColumns
                    : throw new InvalidOperationException(
                        $"Type '{ModelType.FullName}' has no [Key] or [Identity] property; pass idColumn explicitly.");

            var column = Columns.FirstOrDefault(c => c.PropertyName == idColumn || c.ColumnName == idColumn)
                ?? throw new ArgumentException(
                    $"No property or column named '{idColumn}' on type '{ModelType.FullName}'.", nameof(idColumn));
            return new[] { column };
        }

        public string KeyColumnList => string.Join(", ", KeyColumns.Select(c => c.PropertyName));
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
            var insertable = columns.Where(c => !c.IsIdentity && !c.IsComputed).ToArray();
            var updatable = columns.Where(c => !c.IsIdentity && !c.IsComputed && !c.IsInsertOnly).ToArray();
            var identity = columns.FirstOrDefault(c => c.IsIdentity);
            var keys = columns.Where(c => c.IsKey).OrderBy(c => c.KeyOrder).ToArray();   // OrderBy is stable: ties keep declaration order
            if (keys.Length == 0 && identity is not null)
                keys = new[] { identity };

            var rowVersions = columns.Where(c => c.IsRowVersion).ToArray();
            if (rowVersions.Length > 1)
                throw new InvalidOperationException($"Type '{type.FullName}' has more than one [RowVersion] property.");
            if (rowVersions.Length == 1 && rowVersions[0].PropertyType != typeof(byte[]))
                throw new InvalidOperationException(
                    $"[RowVersion] property '{type.FullName}.{rowVersions[0].PropertyName}' must be of type byte[].");

            return new ModelMetadata
            {
                ModelType = type,
                TableName = tableAttribute?.Name,
                Schema = tableAttribute?.Schema,
                Columns = columns,
                InsertableColumns = insertable,
                UpdatableColumns = updatable,
                KeyColumns = keys,
                IdentityColumn = identity,
                RowVersionColumn = rowVersions.FirstOrDefault(),
            };
        }

        private static ColumnBinding BuildBinding(PropertyInfo property)
        {
            var column = property.GetCustomAttribute<ColumnAttribute>();
            var key = property.GetCustomAttribute<KeyAttribute>();
            var isRowVersion = property.GetCustomAttribute<RowVersionAttribute>() != null;
            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            return new ColumnBinding
            {
                PropertyName = property.Name,
                ColumnName = column?.Name ?? property.Name,
                PropertyType = property.PropertyType,
                UnderlyingType = underlying,
                Getter = CompileGetter(property),
                Setter = CompileSetter(property),
                Convert = ValueCoercion.BuildConverter(underlying),
                IsIdentity = property.GetCustomAttribute<IdentityAttribute>() != null,
                IsKey = key != null,
                KeyOrder = key?.Order ?? 0,
                IsComputed = isRowVersion || property.GetCustomAttribute<ComputedAttribute>() != null,
                IsRowVersion = isRowVersion,
                IsInsertOnly = property.GetCustomAttribute<InsertOnlyAttribute>() != null,
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
