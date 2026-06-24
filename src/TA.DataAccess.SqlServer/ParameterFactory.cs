using System.Data;
using Microsoft.Data.SqlClient;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// Creates <see cref="SqlParameter"/>s with explicit, plan-cache-friendly types for the
    /// high-value cases. String sizes are bucketed (≤4000 → 4000, else max) so variable-length
    /// values do not produce a distinct cached plan per length. <c>DateTime</c> and <c>decimal</c>
    /// are intentionally left to ADO.NET inference to avoid <c>datetime</c>/precision behavior changes.
    /// </summary>
    internal static class ParameterFactory
    {
        public static SqlParameter Create(string name, object? rawValue)
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
            }

            return parameter;
        }
    }
}
