using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;
using TA.DataAccess.SqlServer;

namespace SqlServerHelperTest
{
    internal static class Program
    {
        private static async Task Main()
        {
            using var cts = new ConsoleCancellation();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            await using var serviceProvider = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug))
                .AddSingleton<ISqlServerHelper>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var logger = sp.GetRequiredService<ILogger<SqlServerHelper>>();
                    return new SqlServerHelper(config, "DefaultConnection", new SqlServerHelperOptions { LogParameters = true }, logger);
                })
                .BuildServiceProvider();

            var helper = serviceProvider.GetRequiredService<ISqlServerHelper>();
            var token = cts.Token;

            try
            {
                await helper.ExecuteNonQueryAsync(
                    "CREATE TABLE TestTable (id int identity(1,1), PId INT PRIMARY KEY, Name NVARCHAR(50))",
                    cancellationToken: token);

                await helper.ExecuteNonQueryAsync(new[]
                {
                    "INSERT INTO TestTable (PId, Name) VALUES (1, 'Test Name 1')",
                    "INSERT INTO TestTable (PId, Name) VALUES (2, 'Test Name 2')",
                }, token);

                var dataTable = await helper.SelectAsync("SELECT * FROM TestTable", cancellationToken: token);
                foreach (DataRow row in dataTable.Rows)
                    Console.WriteLine($"PId: {row["PId"]}, Name: {row["Name"]}");

                await foreach (var item in helper.SelectStreamAsync<TestModel>("SELECT * FROM TestTable", cancellationToken: token))
                    Console.WriteLine($"streamed PId: {item.PId}, Name: {item.Name}");

                await helper.InsertModelAsync(new TestModel { PId = 3, Name = "Test Name 3" }, "TestTable", token);

                var batch = new List<TestModel>
                {
                    new() { PId = 4, Name = "Test Name 4" },
                    new() { PId = 5, Name = "Test Name 5" },
                };
                await helper.InsertModelsAsync(batch, "TestTable", token);

                var modelById = await helper.GetModelByIdAsync<TestModel>("TestTable", "PId", 1, token)
                    ?? throw new InvalidOperationException("Row PId=1 not found.");
                modelById.Name = "Updated Test Name 1";
                await helper.UpdateModelAsync(modelById, "TestTable", nameof(TestModel.Id), token);

                var deletedRows = await helper.DeleteModelAsync("TestTable", "PId", 1, token);
                Console.WriteLine($"Deleted {deletedRows} row(s).");

                await helper.ExecuteInterpolatedAsync($"DROP TABLE TestTable", token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        public sealed class TestModel
        {
            [Identity]
            public int Id { get; set; }
            public int PId { get; set; }
            public string Name { get; set; } = string.Empty;
            [NoCrud]
            public string? FName { get; set; }
        }

        private sealed class ConsoleCancellation : IDisposable
        {
            private readonly CancellationTokenSource _cts = new();

            public ConsoleCancellation()
            {
                Console.CancelKeyPress += OnCancelKeyPress;
            }

            public CancellationToken Token => _cts.Token;

            private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                _cts.Cancel();
            }

            public void Dispose()
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts.Dispose();
            }
        }
    }
}
