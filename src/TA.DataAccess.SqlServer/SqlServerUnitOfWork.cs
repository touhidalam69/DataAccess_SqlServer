using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// A unit of work that runs every operation on a single open connection enlisted in one
    /// transaction. Created via <see cref="SqlServerHelper.BeginTransaction"/>. Dispose without
    /// committing to roll back.
    /// </summary>
    public sealed class SqlServerUnitOfWork : SqlServerOperations, ISqlServerUnitOfWork
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private bool _completed;

        internal SqlServerUnitOfWork(SqlConnection connection, SqlTransaction transaction, SqlServerHelperOptions options, ILogger logger)
            : base(options, logger)
        {
            _connection = connection;
            _transaction = transaction;
        }

        private protected override SqlTransaction? CurrentTransaction => _transaction;

        private protected override SqlConnection GetOpenConnection(out bool owns)
        {
            owns = false;
            return _connection;
        }

        private protected override ValueTask<(SqlConnection Connection, bool Owns)> GetOpenConnectionAsync(CancellationToken cancellationToken)
            => new((_connection, false));

        public void Commit()
        {
            _transaction.Commit();
            _completed = true;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public void Rollback()
        {
            _transaction.Rollback();
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public void Dispose()
        {
            if (!_completed)
            {
                try { _transaction.Rollback(); } catch { /* connection may already be gone */ }
            }
            _transaction.Dispose();
            _connection.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                try { await _transaction.RollbackAsync().ConfigureAwait(false); } catch { /* connection may already be gone */ }
            }
            await _transaction.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
