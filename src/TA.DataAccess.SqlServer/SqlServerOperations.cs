using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// Shared implementation of every data operation. Connection acquisition and the ambient
    /// transaction are provided by derived types via the abstract hooks, so the same logic serves
    /// both the connection-per-call <see cref="SqlServerHelper"/> and the shared-connection
    /// <see cref="SqlServerUnitOfWork"/>.
    /// </summary>
    public abstract class SqlServerOperations
    {
        private protected readonly SqlServerHelperOptions _options;
        private protected readonly ILogger _logger;

        private protected SqlServerOperations(SqlServerHelperOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;
        }

        // ---- hooks supplied by derived types ----

        /// <summary>Returns an open connection. <paramref name="owns"/> is true when the caller must dispose it.</summary>
        private protected abstract SqlConnection GetOpenConnection(out bool owns);

        private protected abstract ValueTask<(SqlConnection Connection, bool Owns)> GetOpenConnectionAsync(CancellationToken cancellationToken);

        /// <summary>The ambient transaction commands enlist in, or null for autocommit.</summary>
        private protected abstract SqlTransaction? CurrentTransaction { get; }

        private SqlCommand NewCommand(string commandText, SqlConnection connection, CommandType commandType)
        {
            var command = new SqlCommand(commandText, connection, CurrentTransaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds,
                CommandType = commandType,
            };
            return command;
        }

        // ---- execution primitives ----

        private int RunNonQuery(string sql, SqlParameter[]? parameters, CommandType commandType)
        {
            EnsureQuery(sql);
            var connection = GetOpenConnection(out var owns);
            try
            {
                using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                return Time(sql, parameters, command.ExecuteNonQuery);
            }
            finally
            {
                if (owns) connection.Dispose();
            }
        }

        private async Task<int> RunNonQueryAsync(string sql, SqlParameter[]? parameters, CommandType commandType, CancellationToken cancellationToken)
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                return await TimeAsync(sql, parameters, () => command.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        private object? RunScalarRaw(string sql, SqlParameter[]? parameters, CommandType commandType)
        {
            EnsureQuery(sql);
            var connection = GetOpenConnection(out var owns);
            try
            {
                using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                return Time(sql, parameters, command.ExecuteScalar);
            }
            finally
            {
                if (owns) connection.Dispose();
            }
        }

        private async Task<object?> RunScalarRawAsync(string sql, SqlParameter[]? parameters, CommandType commandType, CancellationToken cancellationToken)
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                return await TimeAsync(sql, parameters, () => command.ExecuteScalarAsync(cancellationToken)).ConfigureAwait(false);
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        private List<TModel> RunList<TModel>(string sql, SqlParameter[]? parameters, CommandType commandType) where TModel : new()
        {
            EnsureQuery(sql);
            var connection = GetOpenConnection(out var owns);
            try
            {
                using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                using var reader = command.ExecuteReader();
                return ReaderMapper.MapAll<TModel>(reader);
            }
            finally
            {
                if (owns) connection.Dispose();
            }
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        private async Task<List<TModel>> RunListAsync<TModel>(string sql, SqlParameter[]? parameters, CommandType commandType, CancellationToken cancellationToken) where TModel : new()
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return await ReaderMapper.MapAllAsync<TModel>(reader, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        private DataTable RunDataTable(string sql, SqlParameter[]? parameters, CommandType commandType)
        {
            EnsureQuery(sql);
            var connection = GetOpenConnection(out var owns);
            try
            {
                using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                using var adapter = new SqlDataAdapter(command);
                var dataTable = new DataTable();
                adapter.Fill(dataTable);
                return dataTable;
            }
            finally
            {
                if (owns) connection.Dispose();
            }
        }

        private async Task<DataTable> RunDataTableAsync(string sql, SqlParameter[]? parameters, CommandType commandType, CancellationToken cancellationToken)
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, commandType);
                AddParameters(command, parameters);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        private async IAsyncEnumerable<TModel> RunStream<TModel>(string sql, SqlParameter[]? parameters, [EnumeratorCancellation] CancellationToken cancellationToken) where TModel : new()
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, CommandType.Text);
                AddParameters(command, parameters);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await foreach (var model in ReaderMapper.StreamAsync<TModel>(reader, cancellationToken).ConfigureAwait(false))
                    yield return model;
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ---- raw SQL ----

        public int ExecuteNonQuery(string query, SqlParameter[]? parameters = null)
            => RunNonQuery(query, parameters, CommandType.Text);

        public Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => RunNonQueryAsync(query, parameters, CommandType.Text, cancellationToken);

        public int ExecuteNonQuery(IReadOnlyList<string> queries)
        {
            EnsureQueries(queries);
            var connection = GetOpenConnection(out var owns);
            var localTransaction = owns ? connection.BeginTransaction() : null;
            try
            {
                using var command = new SqlCommand { Connection = connection, Transaction = localTransaction ?? CurrentTransaction, CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    command.CommandText = query;
                    command.Parameters.Clear();
                    rowsAffected += command.ExecuteNonQuery();
                }
                localTransaction?.Commit();
                return rowsAffected;
            }
            catch
            {
                localTransaction?.Rollback();
                throw;
            }
            finally
            {
                localTransaction?.Dispose();
                if (owns) connection.Dispose();
            }
        }

        public async Task<int> ExecuteNonQueryAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken = default)
        {
            EnsureQueries(queries);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var localTransaction = owns ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
            try
            {
                await using var command = new SqlCommand { Connection = connection, Transaction = localTransaction ?? CurrentTransaction, CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    command.CommandText = query;
                    command.Parameters.Clear();
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                if (localTransaction is not null) await localTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch
            {
                if (localTransaction is not null) await localTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (localTransaction is not null) await localTransaction.DisposeAsync().ConfigureAwait(false);
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        public int ExecuteInterpolated(FormattableString query)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        public Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        public int ExecuteInterpolated(IReadOnlyList<FormattableString> queries)
        {
            EnsureInterpolatedQueries(queries);
            var connection = GetOpenConnection(out var owns);
            var localTransaction = owns ? connection.BeginTransaction() : null;
            try
            {
                using var command = new SqlCommand { Connection = connection, Transaction = localTransaction ?? CurrentTransaction, CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    var (sql, parameters) = ParameterizeInterpolation(query);
                    command.CommandText = sql;
                    command.Parameters.Clear();
                    AddParameters(command, parameters);
                    rowsAffected += command.ExecuteNonQuery();
                }
                localTransaction?.Commit();
                return rowsAffected;
            }
            catch
            {
                localTransaction?.Rollback();
                throw;
            }
            finally
            {
                localTransaction?.Dispose();
                if (owns) connection.Dispose();
            }
        }

        public async Task<int> ExecuteInterpolatedAsync(IReadOnlyList<FormattableString> queries, CancellationToken cancellationToken = default)
        {
            EnsureInterpolatedQueries(queries);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var localTransaction = owns ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
            try
            {
                await using var command = new SqlCommand { Connection = connection, Transaction = localTransaction ?? CurrentTransaction, CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var query in queries)
                {
                    var (sql, parameters) = ParameterizeInterpolation(query);
                    command.CommandText = sql;
                    command.Parameters.Clear();
                    AddParameters(command, parameters);
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                if (localTransaction is not null) await localTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch
            {
                if (localTransaction is not null) await localTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (localTransaction is not null) await localTransaction.DisposeAsync().ConfigureAwait(false);
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ---- scalar ----

        public T? ExecuteScalar<T>(string query, SqlParameter[]? parameters = null)
            => CoerceScalar<T>(RunScalarRaw(query, parameters, CommandType.Text));

        public async Task<T?> ExecuteScalarAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => CoerceScalar<T>(await RunScalarRawAsync(query, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false));

        public T? ExecuteScalarInterpolated<T>(FormattableString query)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return CoerceScalar<T>(RunScalarRaw(sql, parameters, CommandType.Text));
        }

        public async Task<T?> ExecuteScalarInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query);
            return CoerceScalar<T>(await RunScalarRawAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false));
        }

        // ---- select ----

        public DataTable Select(string query, SqlParameter[]? parameters = null)
            => RunDataTable(query, parameters, CommandType.Text);

        public Task<DataTable> SelectAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => RunDataTableAsync(query, parameters, CommandType.Text, cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> Select<T>(string query, SqlParameter[]? parameters = null) where T : new()
            => RunList<T>(query, parameters, CommandType.Text);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> SelectAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new()
            => RunListAsync<T>(query, parameters, CommandType.Text, cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public IAsyncEnumerable<T> SelectStreamAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new()
            => RunStream<T>(query, parameters, cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> SelectPaged<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null) where T : new()
        {
            var sql = BuildPagedSql(query, page, pageSize, parameters, out var merged);
            return RunList<T>(sql, merged, CommandType.Text);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> SelectPagedAsync<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new()
        {
            var sql = BuildPagedSql(query, page, pageSize, parameters, out var merged);
            return RunListAsync<T>(sql, merged, CommandType.Text, cancellationToken);
        }

        // ---- model CRUD ----

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModel<T>(T model, string? tableName = null)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var identity = metadata.IdentityColumn;
            var sql = BuildInsertSql(metadata, tableName, includeIdentityOutput: identity is not null);
            var parameters = BuildInsertParameters(metadata, model!);
            if (identity is null)
                return RunNonQuery(sql, parameters, CommandType.Text);

            var generated = RunScalarRaw(sql, parameters, CommandType.Text);
            WriteBackIdentity(identity, model!, generated);
            return 1;
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public async Task<int> InsertModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var identity = metadata.IdentityColumn;
            var sql = BuildInsertSql(metadata, tableName, includeIdentityOutput: identity is not null);
            var parameters = BuildInsertParameters(metadata, model!);
            if (identity is null)
                return await RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);

            var generated = await RunScalarRawAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);
            WriteBackIdentity(identity, model!, generated);
            return 1;
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModels<T>(IReadOnlyList<T> models, string? tableName = null)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName, includeIdentityOutput: false);

            var connection = GetOpenConnection(out var owns);
            var localTransaction = owns ? connection.BeginTransaction() : null;
            try
            {
                using var command = new SqlCommand(sql, connection, localTransaction ?? CurrentTransaction) { CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var model in models)
                {
                    command.Parameters.Clear();
                    foreach (var column in metadata.InsertableColumns)
                        command.Parameters.Add(ParameterFactory.Create($"@{column.PropertyName}", column.Getter(model!)));
                    rowsAffected += command.ExecuteNonQuery();
                }
                localTransaction?.Commit();
                return rowsAffected;
            }
            catch
            {
                localTransaction?.Rollback();
                throw;
            }
            finally
            {
                localTransaction?.Dispose();
                if (owns) connection.Dispose();
            }
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public async Task<int> InsertModelsAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName, includeIdentityOutput: false);

            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var localTransaction = owns ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
            try
            {
                await using var command = new SqlCommand(sql, connection, localTransaction ?? CurrentTransaction) { CommandTimeout = _options.CommandTimeoutSeconds };
                int rowsAffected = 0;
                foreach (var model in models)
                {
                    command.Parameters.Clear();
                    foreach (var column in metadata.InsertableColumns)
                        command.Parameters.Add(ParameterFactory.Create($"@{column.PropertyName}", column.Getter(model!)));
                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                if (localTransaction is not null) await localTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch
            {
                if (localTransaction is not null) await localTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (localTransaction is not null) await localTransaction.DisposeAsync().ConfigureAwait(false);
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        public Task<int> BulkInsertAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default)
        {
            EnsureModels(models);
            return BulkInsertAsync((IEnumerable<T>)models, tableName, cancellationToken);
        }

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        public async Task<int> BulkInsertAsync<T>(IEnumerable<T> models, string? tableName = null, CancellationToken cancellationToken = default)
        {
            if (models is null) throw new ArgumentNullException(nameof(models));
            var metadata = ModelMetadataCache.Get<T>();
            var table = ResolveTableName(tableName, metadata);

            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, CurrentTransaction)
                {
                    DestinationTableName = table,
                    BatchSize = _options.BulkCopyBatchSize,
                    BulkCopyTimeout = _options.BulkCopyTimeoutSeconds,
                };
                foreach (var column in metadata.InsertableColumns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                using var reader = new ObjectDataReader<T>(models, metadata.InsertableColumns);
                await bulk.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
                return reader.RowsRead;
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> GetAllModels<T>(string? tableName = null) where T : new()
        {
            var metadata = ModelMetadataCache.Get<T>();
            return RunList<T>($"SELECT * FROM {ResolveTableName(tableName, metadata)}", null, CommandType.Text);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> GetAllModelsAsync<T>(string? tableName = null, CancellationToken cancellationToken = default) where T : new()
        {
            var metadata = ModelMetadataCache.Get<T>();
            return RunListAsync<T>($"SELECT * FROM {ResolveTableName(tableName, metadata)}", null, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public T? GetModelById<T>(string tableName, string idColumn, object id) where T : new()
        {
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id);
            var results = RunList<T>(sql, parameters, CommandType.Text);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id);
            var results = await RunListAsync<T>(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public T? GetModelByKey<T>(object id) where T : new()
        {
            var (sql, parameters) = BuildSelectByKey<T>(id);
            var results = RunList<T>(sql, parameters, CommandType.Text);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<T?> GetModelByKeyAsync<T>(object id, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = BuildSelectByKey<T>(id);
            var results = await RunListAsync<T>(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null)
        {
            var (sql, parameters) = BuildUpdate(model, tableName, idColumn);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildUpdate(model, tableName, idColumn);
            return RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        public int DeleteModel(string tableName, string idColumn, object id)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        public Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id);
            return RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public int DeleteByKey<T>(object id)
        {
            var (sql, parameters) = BuildDeleteByKey<T>(id);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public Task<int> DeleteByKeyAsync<T>(object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildDeleteByKey<T>(id);
            return RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        public long Count<T>(string? tableName = null)
        {
            var metadata = ModelMetadataCache.Get<T>();
            return ExecuteScalar<long>($"SELECT COUNT_BIG(*) FROM {ResolveTableName(tableName, metadata)}");
        }

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        public Task<long> CountAsync<T>(string? tableName = null, CancellationToken cancellationToken = default)
        {
            var metadata = ModelMetadataCache.Get<T>();
            return ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM {ResolveTableName(tableName, metadata)}", null, cancellationToken);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public bool Exists<T>(object id)
        {
            var (sql, parameters) = BuildExists<T>(id);
            return ExecuteScalar<int>(sql, parameters) == 1;
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public async Task<bool> ExistsAsync<T>(object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildExists<T>(id);
            return await ExecuteScalarAsync<int>(sql, parameters, cancellationToken).ConfigureAwait(false) == 1;
        }

        // ---- stored procedures ----

        public int ExecuteProcedure(string procedureName, SqlParameter[]? parameters = null)
            => RunNonQuery(procedureName, parameters, CommandType.StoredProcedure);

        public Task<int> ExecuteProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => RunNonQueryAsync(procedureName, parameters, CommandType.StoredProcedure, cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> QueryProcedure<T>(string procedureName, SqlParameter[]? parameters = null) where T : new()
            => RunList<T>(procedureName, parameters, CommandType.StoredProcedure);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> QueryProcedureAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new()
            => RunListAsync<T>(procedureName, parameters, CommandType.StoredProcedure, cancellationToken);

        public DataTable QueryProcedure(string procedureName, SqlParameter[]? parameters = null)
            => RunDataTable(procedureName, parameters, CommandType.StoredProcedure);

        public Task<DataTable> QueryProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => RunDataTableAsync(procedureName, parameters, CommandType.StoredProcedure, cancellationToken);

        public T? ExecuteProcedureScalar<T>(string procedureName, SqlParameter[]? parameters = null)
            => CoerceScalar<T>(RunScalarRaw(procedureName, parameters, CommandType.StoredProcedure));

        public async Task<T?> ExecuteProcedureScalarAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            => CoerceScalar<T>(await RunScalarRawAsync(procedureName, parameters, CommandType.StoredProcedure, cancellationToken).ConfigureAwait(false));

        // ---- builders & helpers ----

        private static T? CoerceScalar<T>(object? result)
        {
            if (result is null || result is DBNull) return default;
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target.IsInstanceOfType(result)) return (T)result;
            return (T)ValueCoercion.Coerce(result, target);
        }

        private static void WriteBackIdentity(ColumnBinding identity, object model, object? generated)
        {
            if (generated is null || generated is DBNull) return;
            identity.Setter(model, identity.Convert(generated));
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        private static SqlParameter[] BuildInsertParameters(ModelMetadata metadata, object model)
            => metadata.InsertableColumns
                .Select(c => ParameterFactory.Create($"@{c.PropertyName}", c.Getter(model)))
                .ToArray();

        internal static string BuildInsertSql(ModelMetadata metadata, string? tableName, bool includeIdentityOutput)
        {
            if (metadata.InsertableColumns.Length == 0)
                throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no insertable columns.");

            var table = ResolveTableName(tableName, metadata);
            var columnNames = string.Join(", ", metadata.InsertableColumns.Select(c => Identifier.Quote(c.ColumnName)));
            var parameterNames = string.Join(", ", metadata.InsertableColumns.Select(c => $"@{c.PropertyName}"));
            var output = includeIdentityOutput && metadata.IdentityColumn is { } identity
                ? $" OUTPUT INSERTED.{Identifier.Quote(identity.ColumnName)}"
                : string.Empty;
            return $"INSERT INTO {table} ({columnNames}){output} VALUES ({parameterNames})";
        }

        private static (string Sql, SqlParameter[] Parameters) BuildSelectById(string tableName, string idColumn, object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"SELECT * FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id) });
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildSelectByKey<T>(object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var metadata = ModelMetadataCache.Get<T>();
            var key = metadata.GetKeyColumn(null);
            var sql = $"SELECT * FROM {ResolveTableName(null, metadata)} WHERE {Identifier.Quote(key.ColumnName)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id) });
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        private static (string Sql, SqlParameter[] Parameters) BuildUpdate<T>(T model, string? tableName, string? idColumn)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            var metadata = ModelMetadataCache.Get<T>();
            var keyBinding = metadata.GetKeyColumn(idColumn);

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
                parameters[i] = ParameterFactory.Create($"@{updatable[i].PropertyName}", updatable[i].Getter(model!));
            parameters[^1] = ParameterFactory.Create($"@{keyBinding.PropertyName}", keyBinding.Getter(model!));
            return (sql, parameters);
        }

        private static (string Sql, SqlParameter[] Parameters) BuildDelete(string tableName, string idColumn, object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"DELETE FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id) });
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildDeleteByKey<T>(object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var metadata = ModelMetadataCache.Get<T>();
            var key = metadata.GetKeyColumn(null);
            var sql = $"DELETE FROM {ResolveTableName(null, metadata)} WHERE {Identifier.Quote(key.ColumnName)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id) });
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildExists<T>(object id)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var metadata = ModelMetadataCache.Get<T>();
            var key = metadata.GetKeyColumn(null);
            var sql = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {ResolveTableName(null, metadata)} WHERE {Identifier.Quote(key.ColumnName)} = @Id) THEN 1 ELSE 0 END";
            return (sql, new[] { ParameterFactory.Create("@Id", id) });
        }

        internal static string BuildPagedSql(string query, int page, int pageSize, SqlParameter[]? parameters, out SqlParameter[] merged)
        {
            EnsureQuery(query);
            if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
            if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be >= 1.");

            var paging = new[]
            {
                new SqlParameter("@__offset", (page - 1) * pageSize),
                new SqlParameter("@__fetch", pageSize),
            };
            merged = parameters is null || parameters.Length == 0
                ? paging
                : parameters.Concat(paging).ToArray();
            return $"{query} OFFSET @__offset ROWS FETCH NEXT @__fetch ROWS ONLY";
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

        internal static (string Sql, SqlParameter[] Parameters) ParameterizeInterpolation(FormattableString query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            var args = query.GetArguments();
            var names = new string[args.Length];
            var parameters = new SqlParameter[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var name = $"@p{i}";
                names[i] = name;
                parameters[i] = ParameterFactory.Create(name, args[i]);
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

        private static void EnsureInterpolatedQueries(IReadOnlyList<FormattableString> queries)
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
