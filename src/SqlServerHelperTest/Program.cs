using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using TA.DataAccess.SqlServer;

class Program
{
    static void Main(string[] args)
    {
        // Build configuration
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        IConfiguration configuration = builder.Build();

        // Initialize the SQL Server helper
        // Set up dependency injection
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ISqlServerHelper>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var connectionStringName = "DefaultConnection"; // or get this from settings
                return new SqlServerHelper(config, connectionStringName);
            })
            .BuildServiceProvider();
        var _sqlServerHelper = serviceProvider.GetService<ISqlServerHelper>();

        try
        {
            // Test ExecuteNonQuery with a single query
            string createTableQuery = "CREATE TABLE TestTable (id int identity(1,1), PId INT PRIMARY KEY, Name NVARCHAR(50))";
            _sqlServerHelper.ExecuteNonQuery(createTableQuery);
            Console.WriteLine("Table created successfully.");

            // Test ExecuteNonQuery with multiple queries
            var queries = new List<string>
            {
                "INSERT INTO TestTable (PId, Name) VALUES (1, 'Test Name 1')",
                "INSERT INTO TestTable (PId, Name) VALUES (2, 'Test Name 2')"
            };
            _sqlServerHelper.ExecuteNonQuery(queries);
            Console.WriteLine("Multiple queries executed successfully.");

            // Test Select with a single query
            string selectQuery = "SELECT * FROM TestTable";
            var dataTable = _sqlServerHelper.Select(selectQuery);
            Console.WriteLine("Select query executed successfully.");
            foreach (DataRow row in dataTable.Rows)
            {
                Console.WriteLine($"PId: {row["PId"]}, Name: {row["Name"]}");
            }

            // Test Select<T> with a single query
            var results = _sqlServerHelper.Select<TestModel>("SELECT * FROM TestTable");
            Console.WriteLine("Select<T> query executed successfully.");
            foreach (var result in results)
            {
                Console.WriteLine($"PId: {result.PId}, Name: {result.Name}");
            }

            // Test InsertModel
            var newModel = new TestModel { PId = 3, Name = "Test Name 3" };
            _sqlServerHelper.InsertModel(newModel, "TestTable");
            Console.WriteLine("Model inserted successfully.");

            // Test InsertModels
            var newModels = new List<TestModel>
            {
                new TestModel { PId = 4, Name = "Test Name 4" },
                new TestModel { PId = 5, Name = "Test Name 5" }
            };
            _sqlServerHelper.InsertModels(newModels, "TestTable");
            Console.WriteLine("Models inserted successfully.");

            // Test GetAllModels
            var allModels = _sqlServerHelper.GetAllModels<TestModel>("TestTable");
            Console.WriteLine("GetAllModels executed successfully.");
            foreach (var model in allModels)
            {
                Console.WriteLine($"PId: {model.PId}, Name: {model.Name}");
            }

            // Test GetModelById
            var modelById = _sqlServerHelper.GetModelById<TestModel>("TestTable", "PId", 1);
            Console.WriteLine("GetModelById executed successfully.");
            Console.WriteLine($"PId: {modelById.PId}, Name: {modelById.Name}");

            // Test UpdateModel
            modelById.Name = "Updated Test Name 1";
            _sqlServerHelper.UpdateModel(modelById, "TestTable", "id");
            Console.WriteLine("Model updated successfully.");
            var updatedModel = _sqlServerHelper.GetModelById<TestModel>("TestTable", "PId", 1);
            Console.WriteLine($"PId: {updatedModel.PId}, Name: {updatedModel.Name}");

            // Test DeleteModel
            _sqlServerHelper.DeleteModel("TestTable", "PId",1);
            Console.WriteLine("Model deleted successfully.");
            var remainingModels = _sqlServerHelper.GetAllModels<TestModel>("TestTable");
            Console.WriteLine("Remaining models after deletion:");
            foreach (var model in remainingModels)
            {
                Console.WriteLine($"PId: {model.PId}, Name: {model.Name}");
            }

            // Clean up
            _sqlServerHelper.ExecuteNonQuery("DROP TABLE TestTable");
            Console.WriteLine("Table dropped successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    public class TestModel
    {
        [Identity]
        public int id { get; set; }
        public int PId { get; set; }
        public string Name { get; set; }
        [NoCrud]
        public string FName { get; set; }
    }
}
