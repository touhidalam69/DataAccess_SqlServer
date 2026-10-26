using System.Data;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// Creates <see cref="SqlParameter"/>s with explicit, plan-cache-friendly types for the
    /// high-value cases. String sizes are bucketed (≤4000 → 4000, else max) so variable-length
    /// values do not produce a distinct cached plan per length. <c>decimal</c> is left to ADO.NET
    /// inference. <c>DateTime</c> is left to inference (<c>datetime</c>) unless
    /// <paramref name="dateTime2"/> is set, in which case it is sent as <c>datetime2</c>
    /// (<see cref="SqlServerHelperOptions.UseDateTime2"/>).
    /// </summary>
    internal static class ParameterFactory
    {
        public static SqlParameter Create(string name, object? rawValue, bool dateTime2 = false)
        {
            var value = ValueCoercion.ToDbValue(rawValue);
            var parameter = new SqlParameter(name, value);

            switch (value)
            {
                case string s:
                    parameter.SqlDbType = SqlDbType.NVarChar;
                    parameter.Size = s.Length <= 4000 ? 4000 : -1;
                    break;
                case bool:
                    parameter.SqlDbType = SqlDbType.Bit;
                    break;
                case Guid:
                    parameter.SqlDbType = SqlDbType.UniqueIdentifier;
                    break;
                case byte[] b:
                    parameter.SqlDbType = SqlDbType.VarBinary;
                    parameter.Size = b.Length <= 8000 ? 8000 : -1;
                    break;
                case DateTime when dateTime2:
                    parameter.SqlDbType = SqlDbType.DateTime2;
                    break;
            }

            return parameter;
        }
    }
}
