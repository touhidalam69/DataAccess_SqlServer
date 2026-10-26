using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    public abstract class SqlServerOperations : ISqlServerOperations
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

        private TResult RunReader<TResult>(string sql, SqlParameter[]? parameters, Func<SqlDataReader, TResult> read)
        {
            EnsureQuery(sql);
            var connection = GetOpenConnection(out var owns);
            try
            {
                using var command = NewCommand(sql, connection, CommandType.Text);
                AddParameters(command, parameters);
                return Time(sql, parameters, () =>
                {
                    using var reader = command.ExecuteReader();
                    return read(reader);
                });
            }
            finally
            {
                if (owns) connection.Dispose();
            }
        }

        private async Task<TResult> RunReaderAsync<TResult>(string sql, SqlParameter[]? parameters, Func<SqlDataReader, CancellationToken, Task<TResult>> read, CancellationToken cancellationToken)
        {
            EnsureQuery(sql);
            var (connection, owns) = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = NewCommand(sql, connection, CommandType.Text);
                AddParameters(command, parameters);
                return await TimeAsync(sql, parameters, async () =>
                {
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    return await read(reader, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                if (owns) await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Values of the first row of the first result set, or null when there is no row.</summary>
        private object[]? RunFirstRow(string sql, SqlParameter[]? parameters)
            => RunReader(sql, parameters, reader => reader.Read() ? RowValues(reader) : null);

        private Task<object[]?> RunFirstRowAsync(string sql, SqlParameter[]? parameters, CancellationToken cancellationToken)
            => RunReaderAsync<object[]?>(sql, parameters, async (reader, ct) =>
                await reader.ReadAsync(ct).ConfigureAwait(false) ? RowValues(reader) : null, cancellationToken);

        /// <summary>Values of the first row of every result set, in order (null for an empty one).</summary>
        private List<object[]?> RunFirstRows(string sql, SqlParameter[]? parameters)
            => RunReader(sql, parameters, reader =>
            {
                var rows = new List<object[]?>(2);
                do rows.Add(reader.Read() ? RowValues(reader) : null);
                while (reader.NextResult());
                return rows;
            });

        private Task<List<object[]?>> RunFirstRowsAsync(string sql, SqlParameter[]? parameters, CancellationToken cancellationToken)
            => RunReaderAsync(sql, parameters, async (reader, ct) =>
            {
                var rows = new List<object[]?>(2);
                do rows.Add(await reader.ReadAsync(ct).ConfigureAwait(false) ? RowValues(reader) : null);
                while (await reader.NextResultAsync(ct).ConfigureAwait(false));
                return rows;
            }, cancellationToken);

        private static object[] RowValues(SqlDataReader reader)
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            return values;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        private static List<TModel> ReadSet<TModel>(SqlDataReader reader, int index) where TModel : new()
        {
            if (index > 0 && !reader.NextResult())
                throw new InvalidOperationException($"The query returned {index} result set(s); at least {index + 1} were expected.");
            return ReaderMapper.MapAll<TModel>(reader);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        private static async Task<List<TModel>> ReadSetAsync<TModel>(SqlDataReader reader, int index, CancellationToken cancellationToken) where TModel : new()
        {
            if (index > 0 && !await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"The query returned {index} result set(s); at least {index + 1} were expected.");
            return await ReaderMapper.MapAllAsync<TModel>(reader, cancellationToken).ConfigureAwait(false);
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
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        public Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
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
                    var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
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
                    var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
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
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return CoerceScalar<T>(RunScalarRaw(sql, parameters, CommandType.Text));
        }

        public async Task<T?> ExecuteScalarInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
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

        public DataTable SelectInterpolated(FormattableString query)
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunDataTable(sql, parameters, CommandType.Text);
        }

        public Task<DataTable> SelectInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunDataTableAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public List<T> SelectInterpolated<T>(FormattableString query) where T : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunList<T>(sql, parameters, CommandType.Text);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<List<T>> SelectInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunListAsync<T>(sql, parameters, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public IAsyncEnumerable<T> SelectStreamInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return RunStream<T>(sql, parameters, cancellationToken);
        }

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

        // ---- multiple result sets ----

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public (List<T1> First, List<T2> Second) SelectMultiple<T1, T2>(string query, SqlParameter[]? parameters = null)
            where T1 : new() where T2 : new()
            => RunReader(query, parameters, reader => (ReadSet<T1>(reader, 0), ReadSet<T2>(reader, 1)));

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<(List<T1> First, List<T2> Second)> SelectMultipleAsync<T1, T2>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            where T1 : new() where T2 : new()
            => RunReaderAsync(query, parameters, async (reader, ct) =>
                (await ReadSetAsync<T1>(reader, 0, ct).ConfigureAwait(false),
                 await ReadSetAsync<T2>(reader, 1, ct).ConfigureAwait(false)), cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public (List<T1> First, List<T2> Second, List<T3> Third) SelectMultiple<T1, T2, T3>(string query, SqlParameter[]? parameters = null)
            where T1 : new() where T2 : new() where T3 : new()
            => RunReader(query, parameters, reader => (ReadSet<T1>(reader, 0), ReadSet<T2>(reader, 1), ReadSet<T3>(reader, 2)));

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleAsync<T1, T2, T3>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default)
            where T1 : new() where T2 : new() where T3 : new()
            => RunReaderAsync(query, parameters, async (reader, ct) =>
                (await ReadSetAsync<T1>(reader, 0, ct).ConfigureAwait(false),
                 await ReadSetAsync<T2>(reader, 1, ct).ConfigureAwait(false),
                 await ReadSetAsync<T3>(reader, 2, ct).ConfigureAwait(false)), cancellationToken);

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public (List<T1> First, List<T2> Second) SelectMultipleInterpolated<T1, T2>(FormattableString query)
            where T1 : new() where T2 : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return SelectMultiple<T1, T2>(sql, parameters);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<(List<T1> First, List<T2> Second)> SelectMultipleInterpolatedAsync<T1, T2>(FormattableString query, CancellationToken cancellationToken = default)
            where T1 : new() where T2 : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return SelectMultipleAsync<T1, T2>(sql, parameters, cancellationToken);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public (List<T1> First, List<T2> Second, List<T3> Third) SelectMultipleInterpolated<T1, T2, T3>(FormattableString query)
            where T1 : new() where T2 : new() where T3 : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return SelectMultiple<T1, T2, T3>(sql, parameters);
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleInterpolatedAsync<T1, T2, T3>(FormattableString query, CancellationToken cancellationToken = default)
            where T1 : new() where T2 : new() where T3 : new()
        {
            var (sql, parameters) = ParameterizeInterpolation(query, _options.UseDateTime2);
            return SelectMultipleAsync<T1, T2, T3>(sql, parameters, cancellationToken);
        }

        // ---- model CRUD ----

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModel<T>(T model, string? tableName = null)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var writeBack = HasWriteBack(metadata);
            var sql = BuildInsertSql(metadata, tableName, writeBack: writeBack);
            var parameters = BuildInsertParameters(metadata, model!, _options.UseDateTime2);
            if (!writeBack)
                return RunNonQuery(sql, parameters, CommandType.Text);

            WriteBack(metadata, model!, RunFirstRows(sql, parameters));
            return 1;
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public async Task<int> InsertModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var writeBack = HasWriteBack(metadata);
            var sql = BuildInsertSql(metadata, tableName, writeBack: writeBack);
            var parameters = BuildInsertParameters(metadata, model!, _options.UseDateTime2);
            if (!writeBack)
                return await RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);

            WriteBack(metadata, model!, await RunFirstRowsAsync(sql, parameters, cancellationToken).ConfigureAwait(false));
            return 1;
        }

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        public int InsertModels<T>(IReadOnlyList<T> models, string? tableName = null)
        {
            EnsureModels(models);
            var metadata = ModelMetadataCache.Get<T>();
            var sql = BuildInsertSql(metadata, tableName, writeBack: false);

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
                        command.Parameters.Add(ParameterFactory.Create($"@{column.PropertyName}", column.Getter(model!), _options.UseDateTime2));
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
            var sql = BuildInsertSql(metadata, tableName, writeBack: false);

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
                        command.Parameters.Add(ParameterFactory.Create($"@{column.PropertyName}", column.Getter(model!), _options.UseDateTime2));
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
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id, _options.UseDateTime2);
            var results = RunList<T>(sql, parameters, CommandType.Text);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = BuildSelectById(tableName, idColumn, id, _options.UseDateTime2);
            var results = await RunListAsync<T>(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public T? GetModelByKey<T>(object id) where T : new()
        {
            var (sql, parameters) = BuildSelectByKey<T>(id, _options.UseDateTime2);
            var results = RunList<T>(sql, parameters, CommandType.Text);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Maps reader columns via reflection.")]
        public async Task<T?> GetModelByKeyAsync<T>(object id, CancellationToken cancellationToken = default) where T : new()
        {
            var (sql, parameters) = BuildSelectByKey<T>(id, _options.UseDateTime2);
            var results = await RunListAsync<T>(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : default;
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null)
        {
            var (sql, parameters, rowVersion) = BuildUpdate(model, tableName, idColumn, _options.UseDateTime2);
            if (rowVersion is null)
                return RunNonQuery(sql, parameters, CommandType.Text);

            return CompleteVersionedUpdate(model!, rowVersion, RunFirstRow(sql, parameters));
        }

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        public async Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default)
        {
            var (sql, parameters, rowVersion) = BuildUpdate(model, tableName, idColumn, _options.UseDateTime2);
            if (rowVersion is null)
                return await RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);

            return CompleteVersionedUpdate(model!, rowVersion, await RunFirstRowAsync(sql, parameters, cancellationToken).ConfigureAwait(false));
        }

        public int DeleteModel(string tableName, string idColumn, object id)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id, _options.UseDateTime2);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        public Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildDelete(tableName, idColumn, id, _options.UseDateTime2);
            return RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public int DeleteModel<T>(T model, string? tableName = null)
        {
            var (sql, parameters, rowVersion) = BuildDeleteModel(model, tableName, _options.UseDateTime2);
            if (rowVersion is null)
                return RunNonQuery(sql, parameters, CommandType.Text);

            return CompleteVersionedDelete(model!, rowVersion, RunScalarRaw(sql, parameters, CommandType.Text));
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public async Task<int> DeleteModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var (sql, parameters, rowVersion) = BuildDeleteModel(model, tableName, _options.UseDateTime2);
            if (rowVersion is null)
                return await RunNonQueryAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false);

            return CompleteVersionedDelete(model!, rowVersion, await RunScalarRawAsync(sql, parameters, CommandType.Text, cancellationToken).ConfigureAwait(false));
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public int DeleteByKey<T>(object id)
        {
            var (sql, parameters) = BuildDeleteByKey<T>(id, _options.UseDateTime2);
            return RunNonQuery(sql, parameters, CommandType.Text);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public Task<int> DeleteByKeyAsync<T>(object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildDeleteByKey<T>(id, _options.UseDateTime2);
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
            var (sql, parameters) = BuildExists<T>(id, _options.UseDateTime2);
            return ExecuteScalar<int>(sql, parameters) == 1;
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        public async Task<bool> ExistsAsync<T>(object id, CancellationToken cancellationToken = default)
        {
            var (sql, parameters) = BuildExists<T>(id, _options.UseDateTime2);
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

        private static bool HasGeneratedOutput(ModelMetadata metadata)
            => metadata.IdentityColumn is not null || metadata.RowVersionColumn is not null;

        private static bool HasWriteBack(ModelMetadata metadata)
            => HasGeneratedOutput(metadata) || metadata.InsertReadBackColumns.Length > 0;

        /// <summary>
        /// Writes back the first row of each result set of the insert batch: the generated identity and
        /// rowversion, then the re-read [Computed]/[RowVersion] values. The re-read row is the stored row,
        /// so it also carries changes an insert trigger made, including the version that change produced.
        /// </summary>
        private static void WriteBack(ModelMetadata metadata, object model, List<object[]?> rows)
        {
            var index = 0;
            if (HasGeneratedOutput(metadata))
                WriteBackGenerated(metadata, model, rows[index++]);

            if (metadata.InsertReadBackColumns.Length == 0 || index >= rows.Count || rows[index] is not { } row)
                return;
            for (int i = 0; i < row.Length; i++)
                SetReadBack(metadata.InsertReadBackColumns[i], model, row[i]);
        }

        private static void SetReadBack(ColumnBinding binding, object model, object? value)
        {
            if (value is null || value is DBNull)
            {
                if (!binding.PropertyType.IsValueType || Nullable.GetUnderlyingType(binding.PropertyType) is not null)
                    binding.Setter(model, null);
                return;
            }
            binding.Setter(model, binding.Convert(value));
        }

        /// <summary>Writes the generated identity and rowversion (in that order, when present) back onto the model.</summary>
        private static void WriteBackGenerated(ModelMetadata metadata, object model, object[]? row)
        {
            if (row is null) return;
            int index = 0;
            if (metadata.IdentityColumn is { } identity) SetGenerated(identity, model, row[index++]);
            if (metadata.RowVersionColumn is { } rowVersion) SetGenerated(rowVersion, model, row[index]);
        }

        private static void SetGenerated(ColumnBinding binding, object model, object? value)
        {
            if (value is null || value is DBNull) return;
            binding.Setter(model, binding.Convert(value));
        }

        /// <summary>Reads the (count, new rowversion) row of a versioned update; no row matched means a concurrency conflict.</summary>
        private static int CompleteVersionedUpdate(object model, ColumnBinding rowVersion, object[]? row)
        {
            var count = row is null || row[0] is DBNull ? 0L : Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
            if (count == 0)
                throw ConcurrencyConflict(model, rowVersion);
            SetGenerated(rowVersion, model, row![1]);
            return (int)count;
        }

        /// <summary>Reads the deleted-row count of a versioned delete; zero means a concurrency conflict.</summary>
        private static int CompleteVersionedDelete(object model, ColumnBinding rowVersion, object? result)
        {
            var count = result is null || result is DBNull ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
            if (count == 0)
                throw ConcurrencyConflict(model, rowVersion);
            return (int)count;
        }

        private static DBConcurrencyException ConcurrencyConflict(object model, ColumnBinding rowVersion)
            => new($"No '{model.GetType().Name}' row matched the key and {rowVersion.PropertyName}: it was changed or deleted by someone else. Reload it and try again.");

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        private static SqlParameter[] BuildInsertParameters(ModelMetadata metadata, object model, bool dateTime2 = false)
            => metadata.InsertableColumns
                .Select(c => ParameterFactory.Create($"@{c.PropertyName}", c.Getter(model), dateTime2))
                .ToArray();

        /// <summary>
        /// Builds the INSERT. With <paramref name="writeBack"/>, the generated [Identity] and
        /// [RowVersion] values are returned through <c>OUTPUT ... INTO</c> a table variable; a bare
        /// <c>OUTPUT</c> clause is rejected by SQL Server on tables that have triggers. When the model has
        /// <see cref="ModelMetadata.InsertReadBackColumns"/>, a last SELECT re-reads them from the new row.
        /// </summary>
        internal static string BuildInsertSql(ModelMetadata metadata, string? tableName, bool writeBack)
        {
            if (metadata.InsertableColumns.Length == 0)
                throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no insertable columns.");

            var table = ResolveTableName(tableName, metadata);
            var columnNames = string.Join(", ", metadata.InsertableColumns.Select(c => Identifier.Quote(c.ColumnName)));
            var parameterNames = string.Join(", ", metadata.InsertableColumns.Select(c => $"@{c.PropertyName}"));

            var declarations = new List<string>(2);
            var outputs = new List<string>(2);
            var selected = new List<string>(2);
            if (writeBack && metadata.IdentityColumn is { } identity)
            {
                declarations.Add("[__identity] sql_variant");
                outputs.Add($"INSERTED.{Identifier.Quote(identity.ColumnName)}");
                selected.Add("[__identity]");
            }
            if (writeBack && metadata.RowVersionColumn is { } rowVersion)
            {
                declarations.Add("[__rowversion] binary(8)");
                outputs.Add($"INSERTED.{Identifier.Quote(rowVersion.ColumnName)}");
                selected.Add("[__rowversion]");
            }

            var statements = new List<string>(4);
            if (outputs.Count == 0)
            {
                statements.Add($"INSERT INTO {table} ({columnNames}) VALUES ({parameterNames})");
            }
            else
            {
                statements.Add($"DECLARE @__generated TABLE ({string.Join(", ", declarations)})");
                statements.Add($"INSERT INTO {table} ({columnNames}) OUTPUT {string.Join(", ", outputs)} INTO @__generated VALUES ({parameterNames})");
                statements.Add($"SELECT {string.Join(", ", selected)} FROM @__generated");
            }

            if (writeBack && metadata.InsertReadBackColumns.Length > 0)
            {
                var readBack = string.Join(", ", metadata.InsertReadBackColumns.Select(c => Identifier.Quote(c.ColumnName)));
                statements.Add($"SELECT {readBack} FROM {table} WHERE {NewRowPredicate(metadata)}");
            }

            return statements.Count == 1 ? statements[0] : string.Join("; ", statements) + ";";
        }

        /// <summary>Finds the row just inserted: by the identity captured in @__generated, otherwise by the [Key] parameters.</summary>
        private static string NewRowPredicate(ModelMetadata metadata)
        {
            if (metadata.IdentityColumn is not { } identity)
                return string.Join(" AND ", metadata.KeyColumns.Select(k => $"{Identifier.Quote(k.ColumnName)} = @{k.PropertyName}"));

            // @__generated holds the identity as sql_variant; cast it back so the lookup can seek on the column.
            var sqlType = Type.GetTypeCode(identity.UnderlyingType) switch
            {
                TypeCode.Int32 => "int",
                TypeCode.Int64 => "bigint",
                TypeCode.Int16 => "smallint",
                TypeCode.Byte => "tinyint",
                TypeCode.Decimal => "decimal(38, 0)",
                _ => null,
            };
            var value = sqlType is null ? "[__identity]" : $"CAST([__identity] AS {sqlType})";
            return $"{Identifier.Quote(identity.ColumnName)} = (SELECT {value} FROM @__generated)";
        }

        private static (string Sql, SqlParameter[] Parameters) BuildSelectById(string tableName, string idColumn, object id, bool dateTime2 = false)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"SELECT * FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id, dateTime2) });
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildSelectByKey<T>(object id, bool dateTime2 = false)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var (where, parameters) = BuildKeyPredicate(metadata, id, dateTime2);
            return ($"SELECT * FROM {ResolveTableName(null, metadata)} WHERE {where}", parameters);
        }

        /// <summary>
        /// Builds the UPDATE. Keys come from <paramref name="idColumn"/> or all [Key] columns; the SET list
        /// skips keys, [Identity], [Computed], [RowVersion] and [InsertOnly] columns. With a [RowVersion]
        /// column the WHERE also matches the version, and the batch returns (rows updated, new version).
        /// </summary>
        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters, ColumnBinding? RowVersion) BuildUpdate<T>(T model, string? tableName, string? idColumn, bool dateTime2 = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            var metadata = ModelMetadataCache.Get<T>();
            var keys = metadata.GetKeyColumns(idColumn);

            var updatable = metadata.UpdatableColumns
                .Where(c => !keys.Any(k => k.PropertyName == c.PropertyName))
                .ToArray();
            if (updatable.Length == 0)
                throw new InvalidOperationException($"Type '{metadata.ModelType.FullName}' has no updatable columns.");

            var table = ResolveTableName(tableName, metadata);
            var setClause = string.Join(", ", updatable.Select(c => $"{Identifier.Quote(c.ColumnName)} = @{c.PropertyName}"));
            var where = string.Join(" AND ", keys.Select(k => $"{Identifier.Quote(k.ColumnName)} = @{k.PropertyName}"));

            var parameters = new List<SqlParameter>(updatable.Length + keys.Length + 1);
            parameters.AddRange(updatable.Select(c => ParameterFactory.Create($"@{c.PropertyName}", c.Getter(model!), dateTime2)));
            parameters.AddRange(keys.Select(k => ParameterFactory.Create($"@{k.PropertyName}", k.Getter(model!), dateTime2)));

            var rowVersion = metadata.RowVersionColumn;
            if (rowVersion is null)
                return ($"UPDATE {table} SET {setClause} WHERE {where}", parameters.ToArray(), null);

            var currentVersion = rowVersion.Getter(model!)
                ?? throw new InvalidOperationException(
                    $"[RowVersion] property '{metadata.ModelType.FullName}.{rowVersion.PropertyName}' is null; load the row (or carry its version from the client) before updating.");
            parameters.Add(ParameterFactory.Create($"@{rowVersion.PropertyName}", currentVersion));

            var versionColumn = Identifier.Quote(rowVersion.ColumnName);
            var sql = "DECLARE @__updated TABLE ([__rowversion] binary(8)); "
                    + $"UPDATE {table} SET {setClause} OUTPUT INSERTED.{versionColumn} INTO @__updated "
                    + $"WHERE {where} AND {versionColumn} = @{rowVersion.PropertyName}; "
                    + "SELECT COUNT_BIG(*), MAX([__rowversion]) FROM @__updated;";
            return (sql, parameters.ToArray(), rowVersion);
        }

        private static (string Sql, SqlParameter[] Parameters) BuildDelete(string tableName, string idColumn, object id, bool dateTime2 = false)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var sql = $"DELETE FROM {Identifier.Quote(tableName)} WHERE {Identifier.Quote(idColumn)} = @Id";
            return (sql, new[] { ParameterFactory.Create("@Id", id, dateTime2) });
        }

        /// <summary>
        /// Builds the DELETE for a model: every key column must match its value on the model. With a
        /// [RowVersion] column the version must match too, and the batch returns the number of rows deleted.
        /// </summary>
        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters, ColumnBinding? RowVersion) BuildDeleteModel<T>(T model, string? tableName, bool dateTime2 = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            var metadata = ModelMetadataCache.Get<T>();
            var keys = metadata.GetKeyColumns(null);

            var parameters = new List<SqlParameter>(keys.Length + 1);
            foreach (var key in keys)
            {
                var value = key.Getter(model!)
                    ?? throw new InvalidOperationException(
                        $"Key property '{metadata.ModelType.FullName}.{key.PropertyName}' is null; load the row before deleting it.");
                parameters.Add(ParameterFactory.Create($"@{key.PropertyName}", value, dateTime2));
            }

            var table = ResolveTableName(tableName, metadata);
            var where = string.Join(" AND ", keys.Select(k => $"{Identifier.Quote(k.ColumnName)} = @{k.PropertyName}"));

            var rowVersion = metadata.RowVersionColumn;
            if (rowVersion is null)
                return ($"DELETE FROM {table} WHERE {where}", parameters.ToArray(), null);

            var currentVersion = rowVersion.Getter(model!)
                ?? throw new InvalidOperationException(
                    $"[RowVersion] property '{metadata.ModelType.FullName}.{rowVersion.PropertyName}' is null; load the row (or carry its version from the client) before deleting it.");
            parameters.Add(ParameterFactory.Create($"@{rowVersion.PropertyName}", currentVersion));

            var versionColumn = Identifier.Quote(rowVersion.ColumnName);
            var sql = "DECLARE @__deleted TABLE ([__rowversion] binary(8)); "
                    + $"DELETE FROM {table} OUTPUT DELETED.{versionColumn} INTO @__deleted "
                    + $"WHERE {where} AND {versionColumn} = @{rowVersion.PropertyName}; "
                    + "SELECT COUNT_BIG(*) FROM @__deleted;";
            return (sql, parameters.ToArray(), rowVersion);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildDeleteByKey<T>(object id, bool dateTime2 = false)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var (where, parameters) = BuildKeyPredicate(metadata, id, dateTime2);
            return ($"DELETE FROM {ResolveTableName(null, metadata)} WHERE {where}", parameters);
        }

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        internal static (string Sql, SqlParameter[] Parameters) BuildExists<T>(object id, bool dateTime2 = false)
        {
            var metadata = ModelMetadataCache.Get<T>();
            var (where, parameters) = BuildKeyPredicate(metadata, id, dateTime2);
            return ($"SELECT CASE WHEN EXISTS (SELECT 1 FROM {ResolveTableName(null, metadata)} WHERE {where}) THEN 1 ELSE 0 END", parameters);
        }

        /// <summary>
        /// WHERE predicate for the by-key methods. A single key takes the value itself (<c>[k] = @Id</c>);
        /// a composite key takes an <c>object[]</c> with one value per key column, in key order
        /// (<c>[a] = @Id0 AND [b] = @Id1</c>). Anything else is rejected rather than matching on part of the key.
        /// </summary>
        internal static (string Where, SqlParameter[] Parameters) BuildKeyPredicate(ModelMetadata metadata, object id, bool dateTime2 = false)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            var keys = metadata.GetKeyColumns(null);

            if (keys.Length == 1)
            {
                var value = id;
                if (id is object[] array)
                {
                    if (array.Length != 1)
                        throw new ArgumentException(
                            $"Type '{metadata.ModelType.FullName}' has a single key ({keys[0].PropertyName}) but {array.Length} key values were passed.", nameof(id));
                    value = array[0] ?? throw new ArgumentNullException(nameof(id));
                }
                return ($"{Identifier.Quote(keys[0].ColumnName)} = @Id", new[] { ParameterFactory.Create("@Id", value, dateTime2) });
            }

            if (id is not object[] values || values.Length != keys.Length)
                throw new ArgumentException(
                    $"Type '{metadata.ModelType.FullName}' has a composite key ({metadata.KeyColumnList}); pass an object[] with one value per key column, in that order.", nameof(id));

            var predicates = new string[keys.Length];
            var parameters = new SqlParameter[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                if (values[i] is null)
                    throw new ArgumentException($"Key value for '{keys[i].PropertyName}' is null.", nameof(id));
                predicates[i] = $"{Identifier.Quote(keys[i].ColumnName)} = @Id{i}";
                parameters[i] = ParameterFactory.Create($"@Id{i}", values[i], dateTime2);
            }
            return (string.Join(" AND ", predicates), parameters);
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

        internal static (string Sql, SqlParameter[] Parameters) ParameterizeInterpolation(FormattableString query, bool dateTime2 = false)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            var args = query.GetArguments();
            var names = new string[args.Length];
            var parameters = new SqlParameter[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var name = $"@p{i}";
                names[i] = name;
                parameters[i] = ParameterFactory.Create(name, args[i], dateTime2);
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
