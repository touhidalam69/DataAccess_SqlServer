using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

/// <summary>
/// A [Fact] that runs only when <see cref="EnvironmentVariable"/> holds a SQL Server connection string
/// (server + credentials, no database), for example
/// <c>Server=.;Integrated Security=True;TrustServerCertificate=True</c>. Otherwise the test is skipped.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "TA_SQLSERVER_TEST_CONNECTION";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Set {EnvironmentVariable} to a SQL Server connection string (no database) to run integration tests.";
    }
}

/// <summary>Creates a throw-away database with the table shapes the tests need, and drops it afterwards.</summary>
public sealed class SqlServerDatabaseFixture : IDisposable
{
    private readonly string? _serverConnectionString;
    private readonly string _databaseName = "TA_IT_" + Guid.NewGuid().ToString("N")[..12];

    public string ConnectionString { get; } = string.Empty;

    public SqlServerDatabaseFixture()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(_serverConnectionString)) return;

        Execute(_serverConnectionString, $"CREATE DATABASE [{_databaseName}]");
        ConnectionString = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = _databaseName }.ConnectionString;

        foreach (var batch in SchemaBatches)
            Execute(ConnectionString, batch);
    }

    private static readonly string[] SchemaBatches =
    {
        """
        CREATE TABLE dbo.Doc (
            DocId int IDENTITY PRIMARY KEY,
            Code varchar(20) NOT NULL,
            Styles int NOT NULL,
            Pieces int NOT NULL,
            PlannedQty AS (Styles * Pieces) PERSISTED,
            CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Doc_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy int NOT NULL,
            RowVer rowversion)
        """,
        "CREATE TABLE dbo.DocLine (LineId int IDENTITY PRIMARY KEY, DocId int NOT NULL REFERENCES dbo.Doc (DocId), Note varchar(50) NULL)",
        """
        CREATE TRIGGER dbo.trg_DocLine_Frozen ON dbo.DocLine AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM inserted WHERE Note = 'frozen') THROW 50912, 'Document is frozen.', 1;
        END
        """,
        "CREATE TABLE dbo.Matrix (DocId int NOT NULL, SizeId int NOT NULL, Qty int NOT NULL, CONSTRAINT PK_Matrix PRIMARY KEY (DocId, SizeId))",
        "CREATE TABLE dbo.Stamp (StampId int IDENTITY PRIMARY KEY, At datetime2(3) NOT NULL)",
        """
        CREATE PROCEDURE dbo.usp_Fail AS
        BEGIN
            SET NOCOUNT, XACT_ABORT ON;
            BEGIN TRANSACTION;
            THROW 50103, 'Insufficient stock.', 1;
            COMMIT TRANSACTION;
        END
        """,
    };

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_serverConnectionString)) return;
        SqlConnection.ClearAllPools();
        Execute(_serverConnectionString,
            $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END");
    }
}

[Table("Doc", Schema = "dbo")]
public sealed class ItDoc
{
    [Identity] public int DocId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Styles { get; set; }
    public int Pieces { get; set; }
    [Computed] public int PlannedQty { get; set; }
    [Computed] public DateTime CreatedAt { get; set; }
    [InsertOnly] public int CreatedBy { get; set; }
    [RowVersion] public byte[]? RowVer { get; set; }
}

[Table("DocLine", Schema = "dbo")]
public sealed class ItDocLine
{
    [Identity] public int LineId { get; set; }
    public int DocId { get; set; }
    public string? Note { get; set; }
}

[Table("Matrix", Schema = "dbo")]
public sealed class ItMatrix
{
    [Key(Order = 0)] public int DocId { get; set; }
    [Key(Order = 1)] public int SizeId { get; set; }
    public int Qty { get; set; }
}

[Table("Stamp", Schema = "dbo")]
public sealed class ItStamp
{
    [Identity] public int StampId { get; set; }
    public DateTime At { get; set; }
}

public class SqlServerIntegrationTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public SqlServerIntegrationTests(SqlServerDatabaseFixture db) => _db = db;

    private SqlServerHelper Helper(bool dateTime2 = false)
        => new(_db.ConnectionString, new SqlServerHelperOptions { UseDateTime2 = dateTime2 });

    private static ItDoc NewDoc(string code) => new() { Code = code, Styles = 2, Pieces = 50, CreatedBy = 1 };

    // ---- inserts ----

    [SqlServerFact]
    public void InsertModel_TableWithTrigger_WritesBackIdentity()
    {
        var helper = Helper();
        var doc = NewDoc("trigger");
        helper.InsertModel(doc);

        var line = new ItDocLine { DocId = doc.DocId, Note = "ok" };
        helper.InsertModel(line);

        Assert.True(line.LineId > 0);
        Assert.True(helper.Exists<ItDocLine>(line.LineId));
    }

    [SqlServerFact]
    public async Task InsertModelAsync_TableWithTrigger_WritesBackIdentity()
    {
        var helper = Helper();
        var doc = NewDoc("trigger-async");
        await helper.InsertModelAsync(doc);

        var line = new ItDocLine { DocId = doc.DocId, Note = "ok" };
        await helper.InsertModelAsync(line);

        Assert.True(line.LineId > 0);
    }

    [SqlServerFact]
    public void InsertModel_TriggerError_Surfaces()
    {
        var helper = Helper();
        var doc = NewDoc("frozen");
        helper.InsertModel(doc);

        var ex = Assert.Throws<SqlException>(() => helper.InsertModel(new ItDocLine { DocId = doc.DocId, Note = "frozen" }));
        Assert.Equal(50912, ex.Number);
        Assert.Equal(0, helper.ExecuteScalarInterpolated<int>($"SELECT COUNT(*) FROM dbo.DocLine WHERE DocId = {doc.DocId}"));
    }

    [SqlServerFact]
    public void InsertModel_ConstraintError_Surfaces()
    {
        var ex = Assert.Throws<SqlException>(() => Helper().InsertModel(new ItDocLine { DocId = -1, Note = "orphan" }));
        Assert.Equal(547, ex.Number);   // FK violation
    }

    [SqlServerFact]
    public void InsertModel_ComputedDefaultAndRowVersion()
    {
        var helper = Helper();
        var doc = NewDoc("computed");
        helper.InsertModel(doc);

        Assert.True(doc.DocId > 0);
        Assert.NotNull(doc.RowVer);
        var stored = helper.GetModelByKey<ItDoc>(doc.DocId)!;
        Assert.Equal(100, stored.PlannedQty);                          // computed column read back
        Assert.True(stored.CreatedAt > DateTime.UtcNow.AddMinutes(-5)); // database default applied
        Assert.Equal(doc.RowVer, stored.RowVer);
    }

    // ---- concurrency ----

    [SqlServerFact]
    public void UpdateModel_StaleRowVersion_ThrowsAndKeepsFirstChange()
    {
        var helper = Helper();
        var doc = NewDoc("concurrency");
        helper.InsertModel(doc);

        var userA = helper.GetModelByKey<ItDoc>(doc.DocId)!;
        var userB = helper.GetModelByKey<ItDoc>(doc.DocId)!;

        userA.Styles = 10;
        Assert.Equal(1, helper.UpdateModel(userA));
        Assert.NotEqual(userB.RowVer, userA.RowVer);   // new version written back

        userB.Pieces = 99;
        Assert.Throws<DBConcurrencyException>(() => helper.UpdateModel(userB));

        var stored = helper.GetModelByKey<ItDoc>(doc.DocId)!;
        Assert.Equal(10, stored.Styles);
        Assert.Equal(50, stored.Pieces);

        userA.Code = "again";
        Assert.Equal(1, helper.UpdateModel(userA));    // the written-back version allows the next update
    }

    [SqlServerFact]
    public async Task UpdateModelAsync_StaleRowVersion_Throws()
    {
        var helper = Helper();
        var doc = NewDoc("concurrency-async");
        await helper.InsertModelAsync(doc);
        var stale = (await helper.GetModelByKeyAsync<ItDoc>(doc.DocId))!;

        doc.Styles = 3;
        await helper.UpdateModelAsync(doc);
        await Assert.ThrowsAsync<DBConcurrencyException>(() => helper.UpdateModelAsync(stale));
    }

    [SqlServerFact]
    public void UpdateModel_DoesNotOverwriteInsertOnlyOrComputed()
    {
        var helper = Helper();
        var doc = NewDoc("insert-only");
        helper.InsertModel(doc);

        doc.CreatedBy = 999;
        doc.CreatedAt = new DateTime(2000, 1, 1);
        helper.UpdateModel(doc);

        var stored = helper.GetModelByKey<ItDoc>(doc.DocId)!;
        Assert.Equal(1, stored.CreatedBy);
        Assert.NotEqual(new DateTime(2000, 1, 1), stored.CreatedAt);
    }

    // ---- composite keys ----

    [SqlServerFact]
    public void CompositeKey_UpdateDeleteGetExists_TouchOnlyTheMatchingRow()
    {
        var helper = Helper();
        var doc = NewDoc("matrix");
        helper.InsertModel(doc);
        helper.InsertModels(new[]
        {
            new ItMatrix { DocId = doc.DocId, SizeId = 1, Qty = 100 },
            new ItMatrix { DocId = doc.DocId, SizeId = 2, Qty = 200 },
            new ItMatrix { DocId = doc.DocId, SizeId = 3, Qty = 300 },
        });

        Assert.Equal(1, helper.UpdateModel(new ItMatrix { DocId = doc.DocId, SizeId = 2, Qty = 999 }));
        Assert.Equal(999, helper.GetModelByKey<ItMatrix>(new object[] { doc.DocId, 2 })!.Qty);
        Assert.Equal(100, helper.GetModelByKey<ItMatrix>(new object[] { doc.DocId, 1 })!.Qty);

        Assert.Throws<ArgumentException>(() => helper.DeleteByKey<ItMatrix>(doc.DocId));
        Assert.Equal(3, helper.ExecuteScalarInterpolated<int>($"SELECT COUNT(*) FROM dbo.Matrix WHERE DocId = {doc.DocId}"));

        Assert.Equal(1, helper.DeleteByKey<ItMatrix>(new object[] { doc.DocId, 2 }));
        Assert.False(helper.Exists<ItMatrix>(new object[] { doc.DocId, 2 }));
        Assert.True(helper.Exists<ItMatrix>(new object[] { doc.DocId, 3 }));
    }

    // ---- datetime2 ----

    [SqlServerFact]
    public void UseDateTime2_KeepsMilliseconds()
    {
        var at = new DateTime(2026, 10, 26, 10, 0, 0, 1);

        var precise = Helper(dateTime2: true);
        var a = new ItStamp { At = at };
        precise.InsertModel(a);
        Assert.Equal(at, precise.ExecuteScalarInterpolated<DateTime>($"SELECT At FROM dbo.Stamp WHERE StampId = {a.StampId}"));

        var legacy = Helper();
        var b = new ItStamp { At = at };
        legacy.InsertModel(b);
        Assert.Equal(at.AddMilliseconds(-1), legacy.ExecuteScalarInterpolated<DateTime>($"SELECT At FROM dbo.Stamp WHERE StampId = {b.StampId}"));
    }

    // ---- multiple result sets ----

    [SqlServerFact]
    public async Task SelectMultiple_HeaderAndLinesInOneCall()
    {
        var helper = Helper();
        var doc = NewDoc("multi");
        helper.InsertModel(doc);
        helper.InsertModel(new ItDocLine { DocId = doc.DocId, Note = "one" });
        helper.InsertModel(new ItDocLine { DocId = doc.DocId, Note = "two" });

        var (headers, lines) = helper.SelectMultipleInterpolated<ItDoc, ItDocLine>(
            $"SELECT * FROM dbo.Doc WHERE DocId = {doc.DocId}; SELECT * FROM dbo.DocLine WHERE DocId = {doc.DocId} ORDER BY LineId;");
        Assert.Single(headers);
        Assert.Equal(new[] { "one", "two" }, lines.Select(l => l.Note));

        var (h2, l2, counts) = await helper.SelectMultipleAsync<ItDoc, ItDocLine, ItStamp>(
            "SELECT * FROM dbo.Doc WHERE DocId = @Id; SELECT * FROM dbo.DocLine WHERE DocId = @Id; SELECT TOP (0) * FROM dbo.Stamp;",
            new[] { new SqlParameter("@Id", doc.DocId) });
        Assert.Single(h2);
        Assert.Equal(2, l2.Count);
        Assert.Empty(counts);
    }

    [SqlServerFact]
    public void SelectMultiple_TooFewResultSets_Throws()
        => Assert.Throws<InvalidOperationException>(() => Helper().SelectMultiple<ItDoc, ItDocLine>("SELECT TOP (1) * FROM dbo.Doc"));

    // ---- unit of work ----

    [SqlServerFact]
    public void UnitOfWork_ProcedureFailureWithXactAbort_RollsBackEverything()
    {
        var helper = Helper();
        var code = "uow-fail-" + Guid.NewGuid().ToString("N")[..6];

        var ex = Assert.Throws<SqlException>(() =>
        {
            using var uow = helper.BeginTransaction();
            uow.InsertModel(NewDoc(code));
            uow.ExecuteProcedure("dbo.usp_Fail");
            uow.Commit();
        });

        Assert.Equal(50103, ex.Number);
        Assert.Equal(0, helper.ExecuteScalarInterpolated<int>($"SELECT COUNT(*) FROM dbo.Doc WHERE Code = {code}"));
    }

    [SqlServerFact]
    public async Task UnitOfWork_CommitsHeaderLinesAndVersionedUpdate()
    {
        var helper = Helper();
        var doc = NewDoc("uow-ok");

        await using (var uow = await helper.BeginTransactionAsync())
        {
            await uow.InsertModelAsync(doc);
            await uow.InsertModelAsync(new ItDocLine { DocId = doc.DocId, Note = "in uow" });
            doc.Styles = 4;
            await uow.UpdateModelAsync(doc);
            await uow.CommitAsync();
        }

        var stored = (await helper.GetModelByKeyAsync<ItDoc>(doc.DocId))!;
        Assert.Equal(4, stored.Styles);
        Assert.Equal(doc.RowVer, stored.RowVer);
        Assert.Equal(1, await helper.ExecuteScalarInterpolatedAsync<int>($"SELECT COUNT(*) FROM dbo.DocLine WHERE DocId = {doc.DocId}"));
    }
}
