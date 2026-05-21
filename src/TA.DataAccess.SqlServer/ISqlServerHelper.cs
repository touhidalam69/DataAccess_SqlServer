using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    public interface ISqlServerHelper
    {
        int ExecuteNonQuery(string query, SqlParameter[]? parameters = null);
        Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        int ExecuteNonQuery(IReadOnlyList<string> queries);
        Task<int> ExecuteNonQueryAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken = default);

        int ExecuteInterpolated(FormattableString query);
        Task<int> ExecuteInterpolatedAsync(FormattableString query, CancellationToken cancellationToken = default);

        DataTable Select(string query, SqlParameter[]? parameters = null);
        Task<DataTable> SelectAsync(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default);

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> Select<T>(string query, SqlParameter[]? parameters = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> SelectAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        IAsyncEnumerable<T> SelectStreamAsync<T>(string query, SqlParameter[]? parameters = null, CancellationToken cancellationToken = default) where T : new();

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

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        List<T> GetAllModels<T>(string? tableName = null) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<List<T>> GetAllModelsAsync<T>(string? tableName = null, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        T? GetModelById<T>(string tableName, string idColumn, object id) where T : new();

        [RequiresUnreferencedCode("Maps SqlDataReader columns to T via reflection.")]
        Task<T?> GetModelByIdAsync<T>(string tableName, string idColumn, object id, CancellationToken cancellationToken = default) where T : new();

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        int UpdateModel<T>(T model, string? tableName = null, string? idColumn = null);

        [RequiresUnreferencedCode("Updates properties of T via reflection.")]
        Task<int> UpdateModelAsync<T>(T model, string? tableName = null, string? idColumn = null, CancellationToken cancellationToken = default);

        int DeleteModel(string tableName, string idColumn, object id);
        Task<int> DeleteModelAsync(string tableName, string idColumn, object id, CancellationToken cancellationToken = default);
    }
}
