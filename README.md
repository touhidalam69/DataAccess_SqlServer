# TA.DataAccess.SqlServer

TA.DataAccess.SqlServer is a comprehensive .NET library designed to simplify database operations with SQL Server. It provides a range of methods to execute queries, retrieve data, and perform CRUD operations using strongly-typed models.

## Features

- Execute non-query commands
- Execute multiple non-query commands within a transaction
- Retrieve data as `DataTable`
- Retrieve data as a list of strongly-typed models
- Insert, update, and delete models
- Use custom attributes to exclude properties from CRUD operations

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
    services.AddSingleton<ISqlServerHelper, SqlServerHelper>();  
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
            // Test ExecuteNonQuery with a single query
            string createTableQuery = "CREATE TABLE TestTable (Id INT PRIMARY KEY, Name NVARCHAR(50))";
            _sqlServerHelper.ExecuteNonQuery(createTableQuery);

            // Test ExecuteNonQuery with multiple queries
            var queries = new List<string>
            {
                "INSERT INTO TestTable (Id, Name) VALUES (1, 'Test Name 1')",
                "INSERT INTO TestTable (Id, Name) VALUES (2, 'Test Name 2')"
            };
            _sqlServerHelper.ExecuteNonQuery(queries);

            // Test Select with a single query
            string selectQuery = "SELECT * FROM TestTable";
            var dataTable = _sqlServerHelper.Select(selectQuery);

            // Test Select<T> with a single query
            var results = _sqlServerHelper.Select<TestModel>("SELECT * FROM TestTable");
            

            // Test InsertModel
            var newModel = new TestModel { Id = 3, Name = "Test Name 3" };
            _sqlServerHelper.InsertModel(newModel, "TestTable");

            // Test InsertModels
            var newModels = new List<TestModel>
            {
                new TestModel { Id = 4, Name = "Test Name 4" },
                new TestModel { Id = 5, Name = "Test Name 5" }
            };
            _sqlServerHelper.InsertModels(newModels, "TestTable");

            // Test GetAllModels
            var allModels = _sqlServerHelper.GetAllModels<TestModel>("TestTable");

            // Test GetModelById
            var modelById = _sqlServerHelper.GetModelById<TestModel>("TestTable", "Id", 1);

            // Test UpdateModel
            modelById.Name = "Updated Test Name 1";
            _sqlServerHelper.UpdateModel(modelById, "TestTable", "Id");

            // Test DeleteModel
            _sqlServerHelper.DeleteModel("TestTable", "Id", 1);

            // Clean up
            _sqlServerHelper.ExecuteNonQuery("DROP TABLE TestTable");
    }
}


public class TestModel
{
    public int Id { get; set; }
    public string Name { get; set; }
}

```
## Custom Attributes
You can use the NoCrudAttribute to exclude properties from CRUD operations:

```sh
public class TestModel
{
    public int Id { get; set; }
    
    [NoCrud]
    public string ExcludedColumn { get; set; }
    
    public string Name { get; set; }
}
```

## Contributing
Contributions are welcome! Please fork the repository and submit a pull request.

## License
This project is licensed under the MIT License.