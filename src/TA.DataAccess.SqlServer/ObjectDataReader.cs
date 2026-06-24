using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace TA.DataAccess.SqlServer
{
    /// <summary>
    /// A forward-only <see cref="DbDataReader"/> over an <see cref="IEnumerable{T}"/>, driven by the
    /// model's compiled getters. Lets <c>SqlBulkCopy</c> stream rows without materializing a
    /// <c>DataTable</c>. Only the members <c>SqlBulkCopy</c> uses are meaningfully implemented; the
    /// typed accessors cast the boxed value, and unsupported members throw.
    /// </summary>
    [RequiresUnreferencedCode("Reads model properties via compiled reflection getters.")]
    internal sealed class ObjectDataReader<T> : DbDataReader
    {
        private readonly IEnumerator<T> _enumerator;
        private readonly ColumnBinding[] _columns;
        private int _rowsRead;

        public ObjectDataReader(IEnumerable<T> source, ColumnBinding[] columns)
        {
            _enumerator = source.GetEnumerator();
            _columns = columns;
        }

        /// <summary>Number of rows yielded so far (the inserted row count after a full read).</summary>
        public int RowsRead => _rowsRead;

        public override int FieldCount => _columns.Length;
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;

        public override bool Read()
        {
            var moved = _enumerator.MoveNext();
            if (moved) _rowsRead++;
            return moved;
        }

        public override bool NextResult() => false;

        public override object GetValue(int ordinal)
            => ValueCoercion.ToDbValue(_columns[ordinal].Getter(_enumerator.Current!));

        public override int GetValues(object[] values)
        {
            int count = Math.Min(values.Length, _columns.Length);
            for (int i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }

        public override string GetName(int ordinal) => _columns[ordinal].ColumnName;

        public override int GetOrdinal(string name)
        {
            for (int i = 0; i < _columns.Length; i++)
                if (string.Equals(_columns[i].ColumnName, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            throw new IndexOutOfRangeException(name);
        }

        public override Type GetFieldType(int ordinal) => _columns[ordinal].UnderlyingType;
        public override string GetDataTypeName(int ordinal) => _columns[ordinal].UnderlyingType.Name;
        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        {
            var data = (byte[])GetValue(ordinal);
            if (buffer is null) return data.Length;
            long available = Math.Min(length, data.Length - dataOffset);
            if (available <= 0) return 0;
            Array.Copy(data, dataOffset, buffer, bufferOffset, available);
            return available;
        }

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        {
            var data = ((string)GetValue(ordinal)).ToCharArray();
            if (buffer is null) return data.Length;
            long available = Math.Min(length, data.Length - dataOffset);
            if (available <= 0) return 0;
            Array.Copy(data, dataOffset, buffer, bufferOffset, available);
            return available;
        }

        public override IEnumerator GetEnumerator() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _enumerator.Dispose();
            base.Dispose(disposing);
        }
    }
}
