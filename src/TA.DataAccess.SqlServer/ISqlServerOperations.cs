using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// The data operations shared by <see cref="ISqlServerHelper"/> (each call on its own connection)
    /// and <see cref="ISqlServerUnitOfWork"/> (every call in one transaction). Code written against this
    /// interface, such as a repository, runs the same way inside or outside a transaction.
    /// </summary>
    public interface ISqlServerOperations
    {
        int ExecuteNonQuery(string query, SqlParameter[]? parameters = null);
        Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        int ExecuteNonQuery(IReadOnlyList<string> queries);
        Task<int> ExecuteNonQueryAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken = default);

        int ExecuteInterpolated(FormattableString query);
        Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default);

        int ExecuteInterpolated(IReadOnlyList<FormattableString> queries);
        Task<int> ExecuteInterpolatedAsync(IReadOnlyList<FormattableString> queries, CancellationToken cancellationToken = default);

        T? ExecuteScalar<T>(string query, SqlParameter[]? parameters = null);
        Task<T?> ExecuteScalarAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);
        T? ExecuteScalarInterpolated<T>(FormattableString query);
        Task<T?> ExecuteScalarInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default);

        DataTable Select(string query, SqlParameter[]? parameters = null);
        Task<DataTable> SelectAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> Select<T>(string query, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> SelectAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        IAsyncEnumerable<T> SelectStreamAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        DataTable SelectInterpolated(FormattableString query);
        Task<DataTable> SelectInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> SelectInterpolated<T>(FormattableString query) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> SelectInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        IAsyncEnumerable<T> SelectStreamInterpolatedAsync<T>(FormattableString query, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> SelectPaged<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> SelectPagedAsync<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        /// <summary>
        /// Runs a batch that returns two result sets (for example a header and its lines) in one round
        /// trip and maps them in order. Throws if the batch returns fewer result sets.
        /// </summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        (List<T1> First, List<T2> Second) SelectMultiple<T1, T2>(string query, SqlParameter[]? parameters = null) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<(List<T1> First, List<T2> Second)> SelectMultipleAsync<T1, T2>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T1 : new() where T2 : new();

        /// <summary>Like <see cref="SelectMultiple{T1, T2}"/> for three result sets.</summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        (List<T1> First, List<T2> Second, List<T3> Third) SelectMultiple<T1, T2, T3>(string query, SqlParameter[]? parameters = null) where T1 : new() where T2 : new() where T3 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleAsync<T1, T2, T3>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T1 : new() where T2 : new() where T3 : new();

        /// <summary>Parameterized (interpolated) form of <see cref="SelectMultiple{T1, T2}"/>.</summary>
        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        (List<T1> First, List<T2> Second) SelectMultipleInterpolated<T1, T2>(FormattableString query) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<(List<T1> First, List<T2> Second)> SelectMultipleInterpolatedAsync<T1, T2>(FormattableString query, CancellationToken cancellationToken = default) where T1 : new() where T2 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        (List<T1> First, List<T2> Second, List<T3> Third) SelectMultipleInterpolated<T1, T2, T3>(FormattableString query) where T1 : new() where T2 : new() where T3 : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<(List<T1> First, List<T2> Second, List<T3> Third)> SelectMultipleInterpolatedAsync<T1, T2, T3>(FormattableString query, CancellationToken cancellationToken = default) where T1 : new() where T2 : new() where T3 : new();

        /// <summary>
        /// Inserts the model. [Identity], [Computed] and [RowVersion] columns are not written; the values
        /// the database stored for them are written back onto the model in the same round trip.
        /// [Computed] values are read back when the new row can be found by its [Identity] or by [Key]
        /// values the model supplies.
        /// </summary>
        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        int InsertModel<T>(T model, string? tableName = null);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        Task<int> InsertModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        int InsertModels<T>(IReadOnlyList<T> models, string? tableName = null);

        [RequiresUnreferencedCode("Inserts properties of T via reflection.")]
        Task<int> InsertModelsAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        Task<int> BulkInsertAsync<T>(IReadOnlyList<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Bulk inserts properties of T via reflection.")]
        Task<int> BulkInsertAsync<T>(IEnumerable<T> models, string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> GetAllModels<T>(string? tableName = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> GetAllModelsAsync<T>(string? tableName = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        T? GetModelById<T>(string tableName, string idColumn, object id) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        T? GetModelByKey<T>(object id) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<T?> GetModelByKeyAsync<T>(object id, CancellationToken cancellationToken = default) where T : new();

        /// <summary>
        /// Updates the row matching the model's key: all [Key] columns (composite keys supported), or
        /// <paramref name="idColumn"/> when given. [Identity], [Computed], [RowVersion] and [InsertOnly]
        /// columns are not written. With a [RowVersion] column the stored version must also match;
        /// otherwise <see cref="DBConcurrencyException"/> is thrown, and on success the new version is
        /// written back onto the model.
        /// </summary>
        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null);

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default);

        int DeleteModel(string tableName, string idColumn, object id);
        Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the row matching the model's key (all [Key] columns, or the [Identity] column).
        /// With a [RowVersion] column the stored version must also match; otherwise nothing is deleted
        /// and <see cref="DBConcurrencyException"/> is thrown, as for <c>UpdateModel</c>.
        /// </summary>
        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        int DeleteModel<T>(T model, string? tableName = null);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        Task<int> DeleteModelAsync<T>(T model, string? tableName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes by the [Key]/[Identity] of <typeparamref name="T"/>. For a composite key pass an
        /// <c>object[]</c> with one value per key column in key order; a single value is rejected.
        /// The same applies to <c>GetModelByKey</c> and <c>Exists</c>.
        /// </summary>
        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        int DeleteByKey<T>(object id);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        Task<int> DeleteByKeyAsync<T>(object id, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        long Count<T>(string? tableName = null);

        [RequiresUnreferencedCode("Resolves T table via reflection.")]
        Task<long> CountAsync<T>(string? tableName = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        bool Exists<T>(object id);

        [RequiresUnreferencedCode("Resolves T key via reflection.")]
        Task<bool> ExistsAsync<T>(object id, CancellationToken cancellationToken = default);

        int ExecuteProcedure(string procedureName, SqlParameter[]? parameters = null);
        Task<int> ExecuteProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> QueryProcedure<T>(string procedureName, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> QueryProcedureAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        DataTable QueryProcedure(string procedureName, SqlParameter[]? parameters = null);
        Task<DataTable> QueryProcedureAsync(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        T? ExecuteProcedureScalar<T>(string procedureName, SqlParameter[]? parameters = null);
        Task<T?> ExecuteProcedureScalarAsync<T>(string procedureName, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);
    }
}
