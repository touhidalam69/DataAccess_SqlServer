using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// Runs every operation of <see cref="ISqlServerOperations"/> on its own pooled connection, and
    /// starts transactions that run the same operations on one connection.
    /// </summary>
    public interface ISqlServerHelper : ISqlServerOperations
    {
        // The operations below are the ones ISqlServerHelper declared before 2.4. They are repeated from
        // ISqlServerOperations so that assemblies compiled against older versions still bind to them.
        // Add new operations to ISqlServerOperations only.

        new int ExecuteNonQuery(string query, SqlParameter[]? parameters = null);
        new Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        new int ExecuteNonQuery(IReadOnlyList<string> queries);
        new Task<int> ExecuteNonQueryAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken = default);

        new int ExecuteInterpolated(FormattableString query);
        new Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default);

        new int ExecuteInterpolated(IReadOnlyList<FormattableString> queries);
        new Task<int> ExecuteInterpolatedAsync(IReadOnlyList<FormattableString> queries, CancellationToken cancellationToken = default);

        new T? ExecuteScalar<T>(string query, SqlParameter[]? parameters = null);
        new Task<T?> ExecuteScalarAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);
        new T? ExecuteScalarInterpolated<T>(FormattableString query);
        new Task<T?> ExecuteScalarInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default);

        new DataTable Select(string query, SqlParameter[]? parameters = null);
        new Task<DataTable> SelectAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new List<T> Select<T>(string query, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<List<T>> SelectAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new IAsyncEnumerable<T> SelectStreamAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        new DataTable SelectInterpolated(FormattableString query);
        new Task<DataTable> SelectInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new List<T> SelectInterpolated<T>(FormattableString query) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<List<T>> SelectInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new IAsyncEnumerable<T> SelectStreamInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new List<T> SelectPaged<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<List<T>> SelectPagedAsync<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        /// <summary>
        /// Runs a batch that returns two result sets (for example a header and its lines) in one round
        /// trip and maps them in order. Throws if the batch returns fewer result sets.
        /// </summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new (List<T1> First, List<T2> Second) SelectMultiple<T1, T2>(string query, SqlParameter[]? parameters = null) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<(List<T1> First, List<T2> Second)> SelectMultipleAsync<T1, T2>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T1 : new() where T2 : new();

        /// <summary>Like <see cref="SelectMultiple{T1, T2}"/> for three result sets.</summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new (List<T1> First, List<T2> Second, List<T3> Third) SelectMultiple<T1, T2, T3>(string query, SqlParameter[]? parameters = null) where T1 : new() where T2 : new() where T3 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleAsync<T1, T2, T3>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T1 : new() where T2 : new() where T3 : new();

        /// <summary>Parameterized (interpolated) form of <see cref="SelectMultiple{T1, T2}"/>.</summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new (List<T1> First, List<T2> Second) SelectMultipleInterpolated<T1, T2>(FormattableString query) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<(List<T1> First, List<T2> Second)> SelectMultipleInterpolatedAsync<T1, T2>(FormattableString query, CancellationToken cancellationToken = default) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new (List<T1> First, List<T2> Second, List<T3> Third) SelectMultipleInterpolated<T1, T2, T3>(FormattableString query) where T1 : new() where T2 : new() where T3 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleInterpolatedAsync<T1, T2, T3>(FormattableString query, CancellationToken cancellationToken = default) where T1 : new() where T2 : new() where T3 : new();

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        new int InsertModel<T>(T model, string? tableName = null);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        new Task<int> InsertModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        new int InsertModels<T>(IReadOnlyList<T> models, string? tableName = null);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        new Task<int> InsertModelsAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        new Task<int> BulkInsertAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        new Task<int> BulkInsertAsync<T>(IEnumerable<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new List<T> GetAllModels<T>(string? tableName = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<List<T>> GetAllModelsAsync<T>(string? tableName = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new T? GetModelById<T>(string tableName, string idColumn, object id) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new T? GetModelByKey<T>(object id) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<T?> GetModelByKeyAsync<T>(object id, CancellationToken cancellationToken = default) where T : new();

        /// <summary>
        /// Updates the row matching the model's key: all [Key] columns (composite keys supported), or
        /// <paramref name="idColumn"/> when given. [Identity], [Computed], [RowVersion] and [InsertOnly]
        /// columns are not written. With a [RowVersion] column the stored version must also match;
        /// otherwise <see cref="DBConcurrencyException"/> is thrown, and on success the new version is
        /// written back onto the model.
        /// </summary>
        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        new int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null);

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        new Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default);

        new int DeleteModel(string tableName, string idColumn, object id);
        new Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes by the [Key]/[Identity] of <typeparamref name="T"/>. For a composite key pass an
        /// <c>object[]</c> with one value per key column in key order; a single value is rejected.
        /// The same applies to <c>GetModelByKey</c> and <c>Exists</c>.
        /// </summary>
        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        new int DeleteByKey<T>(object id);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        new Task<int> DeleteByKeyAsync<T>(object id, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        new long Count<T>(string? tableName = null);

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        new Task<long> CountAsync<T>(string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        new bool Exists<T>(object id);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        new Task<bool> ExistsAsync<T>(object id, CancellationToken cancellationToken = default);

        new int ExecuteProcedure(string procedureName, SqlParameter[]? parameters = null);
        new Task<int> ExecuteProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new List<T> QueryProcedure<T>(string procedureName, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        new Task<List<T>> QueryProcedureAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        new DataTable QueryProcedure(string procedureName, SqlParameter[]? parameters = null);
        new Task<DataTable> QueryProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        new T? ExecuteProcedureScalar<T>(string procedureName, SqlParameter[]? parameters = null);
        new Task<T?> ExecuteProcedureScalarAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        ISqlServerUnitOfWork BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
        Task<ISqlServerUnitOfWork> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    }
}
