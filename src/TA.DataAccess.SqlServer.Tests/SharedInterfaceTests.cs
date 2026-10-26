using System.Reflection;
using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class SharedInterfaceTests
{
    private static readonly string[] TransactionControl = { "BeginTransaction", "BeginTransactionAsync", "Commit", "CommitAsync", "Rollback", "RollbackAsync" };

    [Fact]
    public void HelperAndUnitOfWork_ShareOneInterface()
    {
        Assert.True(typeof(ISqlServerOperations).IsAssignableFrom(typeof(ISqlServerHelper)));
        Assert.True(typeof(ISqlServerOperations).IsAssignableFrom(typeof(ISqlServerUnitOfWork)));
        Assert.True(typeof(ISqlServerOperations).IsAssignableFrom(typeof(SqlServerOperations)));
    }

    // Both interfaces repeat the operations they declared before 2.4, so assemblies compiled against older
    // versions keep binding to them. The repeats must be the same on both and match the shared interface.
    [Fact]
    public void RepeatedOperations_MatchTheSharedInterface()
    {
        var helper = Operations(typeof(ISqlServerHelper));
        var unitOfWork = Operations(typeof(ISqlServerUnitOfWork));

        Assert.NotEmpty(helper);
        Assert.Equal(helper, unitOfWork);
        Assert.Subset(Operations(typeof(ISqlServerOperations)).ToHashSet(), helper.ToHashSet());
    }

    // Operations added from 2.4 on are declared on the shared interface only.
    [Fact]
    public void SharedInterface_AddsOnlyTheModelDelete()
    {
        var repeated = Operations(typeof(ISqlServerHelper));
        var added = DeclaredOperations(typeof(ISqlServerOperations))
            .Where(m => !repeated.Contains(m.ToString()))
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(new[] { "DeleteModel", "DeleteModelAsync" }, added);
    }

    /// <summary>Signatures of the methods the interface itself declares, other than transaction control.</summary>
    private static string[] Operations(Type type)
        => DeclaredOperations(type).Select(m => m.ToString()!).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<MethodInfo> DeclaredOperations(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !TransactionControl.Contains(m.Name));
}
