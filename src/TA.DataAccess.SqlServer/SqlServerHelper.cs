using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Reflection;

namespace TA.DataAccess.SqlServer
{
    public class SqlServerHelper : ISqlServerHelper
    {
        private readonly string _connectionString;

        public SqlServerHelper(IConfiguration configuration, string connectionStringName)
        {
            _connectionString = configuration.GetConnectionString(connectionStringName);
        }

        private SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }

        public int ExecuteNonQuery(string query, SqlParameter[] parameters = null)
        {
            using (var connection = GetConnection())
            {
                using (var command = new SqlCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    connection.Open();
                    return command.ExecuteNonQuery();
                }
            }
        }
        public int ExecuteNonQuery(List<string> queries)
        {
            if (queries == null || queries.Count == 0)
                throw new ArgumentException("Queries must not be null or empty.");

            using (var connection = GetConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        try
                        {
                            int rowsAffected = 0;
                            foreach (var query in queries)
                            {
                                command.CommandText = query;
                                command.Parameters.Clear();
                                rowsAffected += command.ExecuteNonQuery();
                            }
                            transaction.Commit();
                            return rowsAffected;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
        }

        public DataTable Select(string query, SqlParameter[] parameters = null)
        {
            using (var connection = GetConnection())
            {
                using (var command = new SqlCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    using (var adapter = new SqlDataAdapter(command))
                    {
                        var dataTable = new DataTable();
                        adapter.Fill(dataTable);
                        return dataTable;
                    }
                }
            }
        }

        public List<T> Select<T>(string query, SqlParameter[] parameters = null) where T : new()
        {
            var dataTable = Select(query, parameters);
            var models = new List<T>();
            foreach (DataRow row in dataTable.Rows)
            {
                var model = new T();
                foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (dataTable.Columns.Contains(prop.Name) && row[prop.Name] != DBNull.Value)
                    {
                        if (prop.PropertyType == typeof(DateTime))
                            prop.SetValue(model, Convert.ToDateTime(row[prop.Name]));
                        else
                            prop.SetValue(model, row[prop.Name]);
                    }
                }
                models.Add(model);
            }

            return models;
        }

        public int InsertModel<T>(T model, string tableName)
        {
            var properties = GetCrudProperties<T>().Where(p => p.GetCustomAttribute<IdentityAttribute>() == null);
            var columnNames = string.Join(", ", properties.Select(p => p.Name));
            var parameterNames = string.Join(", ", properties.Select(p => $"@{p.Name}"));

            var query = $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameterNames})";

            var parameters = properties.Select(p => new SqlParameter($"@{p.Name}", p.GetValue(model) ?? DBNull.Value)).ToArray();

            return ExecuteNonQuery(query, parameters);
        }

        public int InsertModels<T>(List<T> models, string tableName)
        {
            if (models == null || models.Count == 0)
                throw new ArgumentException("Models must not be null or empty.");

            var properties = GetCrudProperties<T>().Where(p => p.GetCustomAttribute<IdentityAttribute>() == null);
            var columnNames = string.Join(", ", properties.Select(p => p.Name));
            var parameterNames = string.Join(", ", properties.Select(p => $"@{p.Name}"));

            var query = $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameterNames})";

            using (var connection = GetConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = new SqlCommand(query, connection, transaction))
                    {
                        try
                        {
                            int rowsAffected = 0;
                            foreach (var model in models)
                            {
                                command.Parameters.Clear();
                                var parameters = properties.Select(p => new SqlParameter($"@{p.Name}", p.GetValue(model) ?? DBNull.Value)).ToArray();
                                command.Parameters.AddRange(parameters);
                                rowsAffected += command.ExecuteNonQuery();
                            }
                            transaction.Commit();
                            return rowsAffected;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
        }

        public List<T> GetAllModels<T>(string tableName) where T : new()
        {
            var query = $"SELECT * FROM {tableName}";

            var dataTable = Select(query);

            var models = new List<T>();

            foreach (DataRow row in dataTable.Rows)
            {
                var model = new T();
                foreach (var prop in GetCrudProperties<T>())
                {
                    if (dataTable.Columns.Contains(prop.Name) && row[prop.Name] != DBNull.Value)
                    {
                        prop.SetValue(model, row[prop.Name]);
                    }
                }
                models.Add(model);
            }

            return models;
        }

        public T GetModelById<T>(string tableName, string idColumn, dynamic id) where T : new()
        {
            var query = $"SELECT * FROM {tableName} WHERE {idColumn} = @Id";
            var parameters = new[] { new SqlParameter("@Id", id) };

            var dataTable = Select(query, parameters);

            if (dataTable.Rows.Count == 0)
            {
                return default;
            }

            var model = new T();
            var row = dataTable.Rows[0];
            foreach (var prop in GetCrudProperties<T>())
            {
                if (dataTable.Columns.Contains(prop.Name) && row[prop.Name] != DBNull.Value)
                {
                    prop.SetValue(model, row[prop.Name]);
                }
            }

            return model;
        }

        public int UpdateModel<T>(T model, string tableName, string idColumn)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (string.IsNullOrEmpty(idColumn))
                throw new ArgumentException("Property name cannot be null or empty.", nameof(idColumn));

            var propertyInfo = typeof(T).GetProperty(idColumn, BindingFlags.Public | BindingFlags.Instance);
            if (propertyInfo == null)
                throw new ArgumentException($"Property '{idColumn}' not found on type '{typeof(T).FullName}'.");


            var properties = GetCrudProperties<T>().Where(p => p.GetCustomAttribute<IdentityAttribute>() == null && p.Name != idColumn);
            var setClause = string.Join(", ", properties.Select(p => $"{p.Name} = @{p.Name}"));
            var query = $"UPDATE {tableName} SET {setClause} WHERE {idColumn} = @{idColumn}";
            var parameters = properties.Select(p => new SqlParameter($"@{p.Name}", p.GetValue(model) ?? DBNull.Value)).ToList();
            parameters.AddRange(new[] { new SqlParameter($"@{idColumn}", propertyInfo.GetValue(model)) });
            return ExecuteNonQuery(query, parameters.ToArray());
        }

        public int DeleteModel(string tableName, string idColumn, dynamic id)
        {
            var query = $"DELETE FROM {tableName} WHERE {idColumn} = @Id";
            var parameters = new[] { new SqlParameter("@Id", id) };
            return ExecuteNonQuery(query, parameters);
        }

        private static IEnumerable<PropertyInfo> GetCrudProperties<T>()
        {
            return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<NoCrudAttribute>() == null);
        }
    }
    public interface ISqlServerHelper
    {
        int ExecuteNonQuery(string query, SqlParameter[] parameters = null);
        int ExecuteNonQuery(List<string> queries);
        DataTable Select(string query, SqlParameter[] parameters = null);
        List<T> Select<T>(string query, SqlParameter[] parameters = null) where T : new();
        int InsertModel<T>(T model, string tableName);
        int InsertModels<T>(List<T> models, string tableName);
        List<T> GetAllModels<T>(string tableName) where T : new();
        T GetModelById<T>(string tableName, string idColumn, dynamic id) where T : new();
        int UpdateModel<T>(T model, string tableName, string idColumn);
        int DeleteModel(string tableName, string idColumn, dynamic id);
    }

    public sealed class NoCrudAttribute : Attribute
    {
    }
    public sealed class IdentityAttribute : Attribute
    {
    }
}
