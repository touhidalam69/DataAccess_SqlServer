using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TA.DataAccess.SqlServer
{
    public sealed class SqlServerHelper : ISqlServerHelper
    {
        private readonly string _connectionString;
        private readonly SqlServerHelperOptions _options;
        private readonly ILogger<SqlServerHelper> _logger;

        public SqlServerHelper(IConfiguration configuration, string connectionStringName)
            : this(configuration, connectionStringName, options: null, logger: null) { }

        public SqlServerHelper(
            IConfiguration configuration,
            string connectionStringName,
            SqlServerHelperOptions? options,
            ILogger<SqlServerHelper>? logger)
        {
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(connectionStringName)) throw new ArgumentException("Connection string name required.", nameof(connectionStringName));

            _connectionString = configuration.GetConnectionString(connectionStringName)
                ?? throw new ArgumentException($"Connection string '{connectionStringName}' not found.", nameof(connectionStringName));
            _options = options ?? new SqlServerHelperOptions();
            _logger = logger ?? NullLogger<SqlServerHelper>.Instance;
        }

        public SqlServerHelper(string connectionString, SqlServerHelperOptions? options = null, ILogger<SqlServerHelper>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string required.", nameof(connectionString));
            _connectionString = connectionString;
            _options = options ?? new SqlServerHelperOptions();
            _logger = logger ?? NullLogger<SqlServerHelper>.Instance;
        }

        private SqlConnection NewConnection() => new(_connectionString);

        private SqlCommand NewCommand(string commandText, SqlConnection connection, SqlTransaction? transaction = null)
        {
            var command = new SqlCommand(commandText, connection, transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds,
            };
            return command;
        }

        public int ExecuteNonQuery(string query, SqlParameter[]? parameters = null)
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            connection.Open();
            return Time(query, parameters, command.ExecuteNonQuery);
        }

        public async Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await TimeAsync(query, parameters, () => command.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
        }

        public int ExecuteNonQuery(IReadOnlyList<string> queries)
        {
            EnsureQueries(queries);
            using var connection = NewConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = NewCommand(string.Empty, connection, transaction);
            try
            {
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    command.CommandText = query;
                    command.Parameters.Clear();
                    rowsAffected += command.ExecuteNonQuery();
                }
                transaction.Commit();
                return rowsAffected;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<int> ExecuteNonQueryAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken = default)
        {
            EnsureQueries(queries);
            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            using var command = NewCommand(string.Empty, connection, transaction);
            try
            {
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    command.CommandText = query;
                    command.Parameters.Clear();
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public int ExecuteInterpolated(FormattableString query)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return ExecuteNonQuery(sql, parameters);
        }

        public Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        public DataTable Select(string query, SqlParameter[]? parameters = null)
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            using var adapter = new SqlDataAdapter(command);
            var dataTable = new DataTable();
            adapter.Fill(dataTable);
            return dataTable;
        }

        public async Task<DataTable> SelectAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> Select<T>(string query, SqlParameter[]? parameters = null) where T : new()
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            connection.Open();
            using var reader = command.ExecuteReader();
            return ReaderMapper.MapAll<T>(reader);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<List<T>> SelectAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new()
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReaderMapper.MapAllAsync<T>(reader, cancellationToken).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async IAsyncEnumerable<T> SelectStreamAsync<T>(
            string query,
            SqlParameter[]? parameters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : new()
        {
            EnsureQuery(query);
            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = NewCommand(query, connection);
            AddParameters(command, parameters);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var model in ReaderMapper.StreamAsync<T>(reader, cancellationToken).ConfigureAwait(false))
                yield return model;
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModel<T>(T model, string? tableName = null)
        {
            var (sql, parameters) = BuildInsert(model, tableName);
            return ExecuteNonQuery(sql, parameters);
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public Task<int> InsertModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildInsert(model, tableName);
            return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModels<T>(IReadOnlyList<T> models, string? tableName = null)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName);

            using var connection = NewConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = NewCommand(sql, connection, transaction);
            try
            {
                int rowsAffected = 0;
                foreach (var model in models)
                {
                    command.Parameters.Clear();
                    foreach (var column in metadata.InsertableColumns)
                        command.Parameters.AddWithValue($"@{column.PropertyName}", ValueCoercion.ToDbValue(column.Getter(model!)));
                    rowsAffected += command.ExecuteNonQuery();
                }
                transaction.Commit();
                return rowsAffected;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public async Task<int> InsertModelsAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName);

            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            using var command = NewCommand(sql, connection, transaction);
            try
            {
                int rowsAffected = 0;
                foreach (var model in models)
                {
                    command.Parameters.Clear();
                    foreach (var column in metadata.InsertableColumns)
                        command.Parameters.AddWithValue($"@{column.PropertyName}", ValueCoercion.ToDbValue(column.Getter(model!)));
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        public async Task<int> BulkInsertAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var table = ResolveTableName(tableName, metadata);

            using var dataTable = BuildBulkDataTable(metadata, models);

            using var connection = NewConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var bulk = new SqlBulkCopy(connection)
            {
                DestinationTableName = table,
                BatchSize = _options.BulkCopyBatchSize,
                BulkCopyTimeout = _options.BulkCopyTimeoutSeconds,
            };
            foreach (DataColumn column in dataTable.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            await bulk.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            return models.Count;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> GetAllModels<T>(string? tableName = null) where T : new()
        {
            var metadata = ModelMetadataCache.Get<T>();
            var sql = $"SELECT * FROM {ResolveTableName(tableName, metadata)}";
            return Select<T>(sql);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> GetAllModelsAsync<T>(string? tableName = null, CancellationToken cancellationToken = default) where T : new()
        {
            var metadata = ModelMetadataCache.Get<T>();
            var sql = $"SELECT * FROM {ResolveTableName(tableName, metadata)}";
            return SelectAsync<T>(sql, parameters: null, cancellationToken);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public T? GetModelById<T>(string tableName, string idColumn, object id) where T : new()
        {
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id);
            var results = Select<T>(sql, parameters);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id);
            var results = await SelectAsync<T>(sql, parameters, cancellationToken).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null)
        {
            var (sql, parameters) = BuildUpdate(model, tableName, idColumn);
            return ExecuteNonQuery(sql, parameters);
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildUpdate(model, tableName, idColumn);
            return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        public int DeleteModel(string tableName, string idColumn, object id)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id);
            return ExecuteNonQuery(sql, parameters);
        }

        public Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id);
            return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        private static (string Sql, SqlParameter[] Parameters) BuildInsert<T>(T model, string? tableName)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName);
            var parameters = metadata.InsertableColumns
                .Select(c => new SqlParameter($"@{c.PropertyName}", ValueCoercion.ToDbValue(c.Getter(model!))))
                .ToArray();
            return (sql, parameters);
        }

        private static string BuildInsertSql(ModelMetadata metadata, string? tableName)
        {
            if (metadata.InsertableColumns.Length == 0)
                throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no insertable columns.");

            var table = ResolveTableName(tableName, metadata);
            var columnNames = string.Join(", ", metadata.InsertableColumns.Select(c => Identifier.Quote(c.ColumnName)));
            var parameterNames = string.Join(", ", metadata.InsertableColumns.Select(c => $"@{c.PropertyName}"));
            return $"INSERT INTO {table} ({columnNames}) VALUES ({parameterNames})";
        }

        private static (string Sql, SqlParameter[] Parameters) BuildSelectById(string tableName, string idColumn, object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"SELECT * FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { new SqlParameter("@Id", ValueCoercion.ToDbValue(id)) });
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        private static (string Sql, SqlParameter[] Parameters) BuildUpdate<T>(T model, string? tableName, string? idColumn)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            var metadata = ModelMetadataCache.Get<T>();

            var keyBinding = idColumn is null
                ? metadata.KeyColumn ?? throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no [Key] or [Identity] property; pass idColumn explicitly.")
                : metadata.Columns.FirstOrDefault(c => c.PropertyName == idColumn)
                  ?? throw new ArgumentException($"Property '{idColumn}' not found on type '{metadata.ModelType.FullName}'.", nameof(idColumn));

            var updatable = metadata.Columns
                .Where(c => !c.IsIdentity && c.PropertyName != keyBinding.PropertyName)
                .ToArray();
            if (updatable.Length == 0)
                throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no updatable columns.");

            var table = ResolveTableName(tableName, metadata);
            var setClause = string.Join(", ", updatable.Select(c => $"{Identifier.Quote(c.ColumnName)} = @{c.PropertyName}"));
            var sql = $"UPDATE {table} SET {setClause} WHERE {Identifier.Quote(keyBinding.ColumnName)} = @{keyBinding.PropertyName}";

            var parameters = new SqlParameter[updatable.Length + 1];
            for (int i = 0; i < updatable.Length; i++)
                parameters[i] = new SqlParameter($"@{updatable[i].PropertyName}", ValueCoercion.ToDbValue(updatable[i].Getter(model!)));
            parameters[^1] = new SqlParameter($"@{keyBinding.PropertyName}", ValueCoercion.ToDbValue(keyBinding.Getter(model!)));
            return (sql, parameters);
        }

        private static (string Sql, SqlParameter[] Parameters) BuildDelete(string tableName, string idColumn, object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"DELETE FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { new SqlParameter("@Id", ValueCoercion.ToDbValue(id)) });
        }

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        private static DataTable BuildBulkDataTable<T>(ModelMetadata metadata, IReadOnlyList<T> models)
        {
            var table = new DataTable();
            foreach (var column in metadata.InsertableColumns)
                table.Columns.Add(column.ColumnName, column.UnderlyingType);

            foreach (var model in models)
            {
                var row = table.NewRow();
                foreach (var column in metadata.InsertableColumns)
                    row[column.ColumnName] = ValueCoercion.ToDbValue(column.Getter(model!));
                table.Rows.Add(row);
            }
            return table;
        }

        private static string ResolveTableName(string? explicitTableName, ModelMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(explicitTableName))
                return Identifier.Quote(explicitTableName);

            if (!string.IsNullOrWhiteSpace(metadata.TableName))
            {
                return string.IsNullOrWhiteSpace(metadata.Schema)
                    ? Identifier.Quote(metadata.TableName!)
                    : Identifier.Quote(metadata.Schema + "." + metadata.TableName);
            }

            return Identifier.Quote(metadata.ModelType.Name);
        }

        private static (string Sql, SqlParameter[] Parameters) ParameterizeInterpolation(FormattableString query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            var args = query.GetArguments();
            var names = new string[args.Length];
            var parameters = new SqlParameter[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var name = $"@p{i}";
                names[i] = name;
                parameters[i] = new SqlParameter(name, ValueCoercion.ToDbValue(args[i]));
            }
            var sql = string.Format(query.Format, names);
            return (sql, parameters);
        }

        private static void AddParameters(SqlCommand command, SqlParameter[]? parameters)
        {
            if (parameters is null || parameters.Length == 0) return;
            command.Parameters.AddRange(parameters);
        }

        private static void EnsureQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query must not be null or empty.", nameof(query));
        }

        private static void EnsureQueries(IReadOnlyList<string> queries)
        {
            if (queries is null || queries.Count == 0)
                throw new ArgumentException("Queries must not be null or empty.", nameof(queries));
        }

        private static void EnsureModels<T>(IReadOnlyList<T> models)
        {
            if (models is null || models.Count == 0)
                throw new ArgumentException("Models must not be null or empty.", nameof(models));
        }

        private T Time<T>(string sql, SqlParameter[]? parameters, Func<T> action)
        {
            if (!_logger.IsEnabled(LogLevel.Debug)) return action();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                stopwatch.Stop();
                LogExecution(sql, parameters, stopwatch.ElapsedMilliseconds);
            }
        }

        private async Task<T> TimeAsync<T>(string sql, SqlParameter[]? parameters, Func<Task<T>> action)
        {
            if (!_logger.IsEnabled(LogLevel.Debug)) return await action().ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                stopwatch.Stop();
                LogExecution(sql, parameters, stopwatch.ElapsedMilliseconds);
            }
        }

        private void LogExecution(string sql, SqlParameter[]? parameters, long elapsedMs)
        {
            if (_options.LogParameters && parameters is { Length: > 0 })
                _logger.LogDebug("SQL executed in {ElapsedMs}ms: {Sql} | params: {Params}", elapsedMs, sql, FormatParameters(parameters));
            else
                _logger.LogDebug("SQL executed in {ElapsedMs}ms: {Sql}", elapsedMs, sql);
        }

        private static string FormatParameters(SqlParameter[] parameters)
            => string.Join(", ", parameters.Select(p => $"{p.ParameterName}={p.Value}"));
    }
}
