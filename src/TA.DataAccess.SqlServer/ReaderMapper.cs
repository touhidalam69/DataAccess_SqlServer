using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
    internal static class ReaderMapper
    {
        public static List<T> MapAll<T>(SqlDataReader reader) where T : new()
        {
            var bindings = BuildBindings<T>(reader);
            var models = new List<T>();
            while (reader.Read())
                models.Add(MapRow<T>(reader, bindings));
            return models;
        }

        public static async Task<List<T>> MapAllAsync<T>(SqlDataReader reader, CancellationToken cancellationToken) where T : new()
        {
            var bindings = BuildBindings<T>(reader);
            var models = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                models.Add(MapRow<T>(reader, bindings));
            return models;
        }

        public static async IAsyncEnumerable<T> StreamAsync<T>(SqlDataReader reader, [EnumeratorCancellation] CancellationToken cancellationToken) where T : new()
        {
            var bindings = BuildBindings<T>(reader);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return MapRow<T>(reader, bindings);
        }

        private static (ColumnBinding Binding, int Ordinal)[] BuildBindings<T>(SqlDataReader reader)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var bindings = new List<(ColumnBinding, int)>(metadata.Columns.Length);
            for (int i = 0; i < metadata.Columns.Length; i++)
            {
                var column = metadata.Columns[i];
                int ordinal;
                try { ordinal = reader.GetOrdinal(column.ColumnName); }
                catch (IndexOutOfRangeException) { continue; }
                bindings.Add((column, ordinal));
            }
            return bindings.ToArray();
        }

        private static T MapRow<T>(SqlDataReader reader, (ColumnBinding Binding, int Ordinal)[] bindings) where T : new()
        {
            var model = new T();
            foreach (var (column, ordinal) in bindings)
            {
                if (reader.IsDBNull(ordinal)) continue;
                var value = reader.GetValue(ordinal);
                try
                {
                    var converted = column.Convert(value);
                    column.Setter(model!, converted);
                }
                catch (Exception ex)
                {
                    throw new InvalidCastException(
                        $"Failed to convert column '{column.ColumnName}' value '{value}' to {column.PropertyType.Name}.", ex);
                }
            }
            return model;
        }
    }
}
