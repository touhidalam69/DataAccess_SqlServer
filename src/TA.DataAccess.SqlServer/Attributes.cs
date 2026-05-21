namespace TA.DataAccess.SqlServer
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NoCrudAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IdentityAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class KeyAttribute : Attribute { }

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
