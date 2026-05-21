namespace TA.DataAccess.SqlServer
{
    public sealed class SqlServerHelperOptions
    {
        public int CommandTimeoutSeconds { get; set; } = 30;
        public int BulkCopyTimeoutSeconds { get; set; } = 60;
        public int BulkCopyBatchSize { get; set; } = 1000;
        public bool LogParameters { get; set; }
    }
}
