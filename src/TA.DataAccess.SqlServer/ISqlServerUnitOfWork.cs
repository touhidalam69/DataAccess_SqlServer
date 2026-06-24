using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// A transactional scope over a single connection. All operations enlist in the same
    /// transaction; call <see cref="Commit"/>/<see cref="CommitAsync"/> to persist, otherwise
    /// disposal rolls back. Obtained from <see cref="ISqlServerHelper.BeginTransaction"/>.
    /// </summary>
    public interface ISqlServerUnitOfWork : IDisposable, IAsyncDisposable
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

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> SelectPaged<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> SelectPagedAsync<T>(string query, int page, int pageSize, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

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

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null);

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default);

        int DeleteModel(string tableName, string idColumn, object id);
        Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default);

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

        void Commit();
        Task CommitAsync(CancellationToken cancellationToken = default);
        void Rollback();
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
