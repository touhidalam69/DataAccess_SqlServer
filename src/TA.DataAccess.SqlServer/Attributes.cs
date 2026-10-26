namespace TA.DataAccess.SqlServer
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NoCrudAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IdentityAttribute : Attribute { }

    /// <summary>
    /// Marks a key column. Put it on several properties for a composite key; the key columns are
    /// ordered by <see cref="Order"/>, then by declaration order.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class KeyAttribute : Attribute
    {
        /// <summary>Position of this column within a composite key.</summary>
        public int Order { get; init; }
    }

    /// <summary>
    /// A column the database fills: a computed column, or a column whose default you never set
    /// from code. It is read into the model but never written by inserts or updates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ComputedAttribute : Attribute { }

    /// <summary>
    /// A <c>rowversion</c> column used for optimistic concurrency. It is read into the model, never
    /// written, written back after insert and update, and checked by <c>UpdateModel</c>. The
    /// property must be a <c>byte[]</c>; a model can have at most one.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class RowVersionAttribute : Attribute { }

    /// <summary>
    /// A column written by inserts but never by updates (for example <c>CreatedAt</c>/<c>CreatedBy</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class InsertOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ColumnAttribute : Attribute
    {
        public ColumnAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class TableAttribute : Attribute
    {
        public TableAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
        public string? Schema { get; init; }
    }
}
