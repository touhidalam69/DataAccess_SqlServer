namespace TA.DataAccess.SqlServer
{
    public sealed class SqlServerHelperOptions
    {
        public int CommandTimeoutSeconds { get; set; } = 30;
        public int BulkCopyTimeoutSeconds { get; set; } = 60;
        public int BulkCopyBatchSize { get; set; } = 1000;
        public bool LogParameters { get; set; }

        /// <summary>
        /// Send <see cref="DateTime"/> values the library parameterizes (model CRUD and interpolated
        /// queries) as <c>datetime2</c> instead of <c>datetime</c>, keeping full precision and the full
        /// date range. Turn on when your columns are <c>datetime2</c>. Default <c>false</c> keeps the
        /// earlier <c>datetime</c> behavior, which suits <c>datetime</c> columns. Parameters you build
        /// yourself are sent as you typed them.
        /// </summary>
        public bool UseDateTime2 { get; set; }
    }
}
