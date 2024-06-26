# TA.DataAccess.SqlServer

TA.DataAccess.SqlServer is a comprehensive .NET library designed to simplify database operations with SQL Server. It provides a range of methods to execute queries, retrieve data, and perform CRUD operations using strongly-typed models.

## Features

- Execute non-query commands
- Execute multiple non-query commands within a transaction
- Retrieve data as `DataTable`
- Retrieve data as a list of strongly-typed models
- Insert, update, and delete models
- Use custom attributes to exclude properties from CRUD operations or mark properties as identity columns

## Installation

You can install the package via NuGet Package Manager:

```sh
dotnet add package TA.DataAccess.SqlServer

PM> NuGet\Install-Package TA.DataAccess.SqlServer
```

## Usage
### Configuration
Ensure your appsettings.json has the connection string:

```sh
{
  "ConnectionStrings": {
    "DefaultConnection": "Your Connection String Here"
  }
}

```

## Dependency Injection
Register the SqlServerHelper in your Startup.cs or Program.cs:

```sh
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<ISqlServerHelper, SqlServerHelper>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return new SqlServerHelper(configuration, "DefaultConnection");
    });
}

```

For console applications, configure dependency injection like this:

```sh
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace YourNamespace
{
    class Program
    {
        static void Main(string[] args)
        {
            // Set up configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Set up dependency injection
            var serviceProvider = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<ISqlServerHelper, SqlServerHelper>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    return new SqlServerHelper(config, "DefaultConnection");
                })
                .BuildServiceProvider();

            // Resolve and use your service
            var sqlServerHelper = serviceProvider.GetService<ISqlServerHelper>();

            // Example usage
            var myService = new MyService(sqlServerHelper);
            myService.ExecuteSampleQueries();

            // Keep the console window open
            Console.ReadLine();
        }
    }
}


```

## Example Usage
```sh
using Microsoft.Extensions.Configuration;
using TA.DataAccess.SqlServer;
using System.Collections.Generic;
using System.Data;

public class MyService
{
    private readonly ISqlServerHelper _sqlServerHelper;

    public MyService(ISqlServerHelper sqlServerHelper)
    {
        _sqlServerHelper = sqlServerHelper;
    }

    public void ExecuteSampleQueries()
    {
            // ExecuteNonQuery with a single query
            string createTableQuery = "CREATE TABLE TestTable (id int identity(1,1), PId INT PRIMARY KEY, Name NVARCHAR(50))";
            _sqlServerHelper.ExecuteNonQuery(createTableQuery);
            Console.WriteLine("Table created successfully.");

            // ExecuteNonQuery with multiple queries
            var queries = new List<string>
            {
                "INSERT INTO TestTable (PId, Name) VALUES (1, 'Test Name 1')",
                "INSERT INTO TestTable (PId, Name) VALUES (2, 'Test Name 2')"
            };
            _sqlServerHelper.ExecuteNonQuery(queries);
            Console.WriteLine("Multiple queries executed successfully.");

            // Select with a single query
            string selectQuery = "SELECT * FROM TestTable";
            var dataTable = _sqlServerHelper.Select(selectQuery);
            Console.WriteLine("Select query executed successfully.");
            foreach (DataRow row in dataTable.Rows)
            {
                Console.WriteLine($"PId: {row["PId"]}, Name: {row["Name"]}");
            }

            // Select<T> with a single query
            var results = _sqlServerHelper.Select<TestModel>("SELECT * FROM TestTable");
            Console.WriteLine("Select<T> query executed successfully.");
            foreach (var result in results)
            {
                Console.WriteLine($"PId: {result.PId}, Name: {result.Name}");
            }

            // InsertModel
            var newModel = new TestModel { PId = 3, Name = "Test Name 3" };
            _sqlServerHelper.InsertModel(newModel, "TestTable");
            Console.WriteLine("Model inserted successfully.");

            // InsertModels
            var newModels = new List<TestModel>
            {
                new TestModel { PId = 4, Name = "Test Name 4" },
                new TestModel { PId = 5, Name = "Test Name 5" }
            };
            _sqlServerHelper.InsertModels(newModels, "TestTable");
            Console.WriteLine("Models inserted successfully.");

            // GetAllModels
            var allModels = _sqlServerHelper.GetAllModels<TestModel>("TestTable");
            Console.WriteLine("GetAllModels executed successfully.");
            foreach (var model in allModels)
            {
                Console.WriteLine($"PId: {model.PId}, Name: {model.Name}");
            }

            // GetModelById
            var modelById = _sqlServerHelper.GetModelById<TestModel>("TestTable", "PId", 1);
            Console.WriteLine("GetModelById executed successfully.");
            Console.WriteLine($"PId: {modelById.PId}, Name: {modelById.Name}");

            // UpdateModel
            modelById.Name = "Updated Test Name 1";
            _sqlServerHelper.UpdateModel(modelById, "TestTable", "id");
            Console.WriteLine("Model updated successfully.");
            var updatedModel = _sqlServerHelper.GetModelById<TestModel>("TestTable", "PId", 1);
            Console.WriteLine($"PId: {updatedModel.PId}, Name: {updatedModel.Name}");

            // DeleteModel
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
}


```
## Custom Attributes
You can use the NoCrudAttribute to exclude properties from CRUD operations:

```sh
public class TestModel
    {
        [Identity]
        public int id { get; set; }
        public int PId { get; set; }
        public string Name { get; set; }
        [NoCrud]
        public string FName { get; set; }
    }
```

## Contributing
Contributions are welcome! Please fork the repository and submit a pull request.

## License
This project is licensed under the MIT License.