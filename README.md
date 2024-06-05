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
        // Execute a non-query command
        _sqlServerHelper.ExecuteNonQuery("INSERT INTO MyTable (Column1) VALUES (@Value)", new SqlParameter[] { new SqlParameter("@Value", "SampleValue") });

        // Execute a query and get results as DataTable
        DataTable dt = _sqlServerHelper.Select("SELECT * FROM MyTable");

        // Execute a query and get results as a list of strongly-typed models
        List<MyModel> models = _sqlServerHelper.Select<MyModel>("SELECT * FROM MyTable");

        // Insert a model
        MyModel model = new MyModel { Column1 = "Value1", Column2 = "Value2" };
        _sqlServerHelper.InsertModel(model, "MyTable");

        // Insert multiple models
        List<MyModel> modelList = new List<MyModel> { model, model };
        _sqlServerHelper.InsertModels(modelList, "MyTable");
    }
}


public class MyModel
{
    public int Id { get; set; }
    public string Column1 { get; set; }
    public string Column2 { get; set; }
}

```
## Custom Attributes
You can use the NoCrudAttribute to exclude properties from CRUD operations:

```sh
public class MyModel
{
    public int Id { get; set; }
    
    [NoCrud]
    public string ExcludedColumn { get; set; }
    
    public string Column1 { get; set; }
    public string Column2 { get; set; }
}
```

## Contributing
Contributions are welcome! Please fork the repository and submit a pull request.

## License
This project is licensed under the MIT License.