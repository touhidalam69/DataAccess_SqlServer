using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TA.DataAccess.SqlServer
{
    public sealed class SqlServerHelper : SqlServerOperations, ISqlServerHelper
    {
        private readonly string _connectionString;

        public SqlServerHelper(IConfiguration configuration, string connectionStringName)
            : this(configuration, connectionStringName, options: null, logger: null) { }

        public SqlServerHelper(
            IConfiguration configuration,
            string connectionStringName,
            SqlServerHelperOptions? options,
            ILogger<SqlServerHelper>? logger)
            : base(options ?? new SqlServerHelperOptions(), logger ?? NullLogger<SqlServerHelper>.Instance)
        {
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(connectionStringName)) throw new ArgumentException("Connection string name required.", nameof(connectionStringName));

            _connectionString = configuration.GetConnectionString(connectionStringName)
                ?? throw new ArgumentException($"Connection string '{connectionStringName}' not found.", nameof(connectionStringName));
        }

        public SqlServerHelper(string connectionString, SqlServerHelperOptions? options = null, ILogger<SqlServerHelper>? logger = null)
            : base(options ?? new SqlServerHelperOptions(), logger ?? NullLogger<SqlServerHelper>.Instance)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string required.", nameof(connectionString));
            _connectionString = connectionString;
        }

        private protected override SqlTransaction? CurrentTransaction => null;

        private protected override SqlConnection GetOpenConnection(out bool owns)
        {
            owns = true;
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private protected override async ValueTask<(SqlConnection Connection, bool Owns)> GetOpenConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return (connection, true);
        }

        public ISqlServerUnitOfWork BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            try
            {
                var transaction = connection.BeginTransaction(isolationLevel);
                return new SqlServerUnitOfWork(connection, transaction, _options, _logger);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public async Task<ISqlServerUnitOfWork> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
                return new SqlServerUnitOfWork(connection, transaction, _options, _logger);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
