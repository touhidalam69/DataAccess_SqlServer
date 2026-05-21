using Xunit;

namespace TA.DataAccess.SqlServer.Tests;

public class ModelMetadataTests
{
    [Table("Customers", Schema = "sales")]
    private sealed class Customer
    {
        [Identity]
        public int Id { get; set; }
        [Key]
        [Column("customer_code")]
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        [NoCrud]
        public string Internal { get; set; } = string.Empty;
    }

    [Fact]
    public void Metadata_ExposesTableAndSchema()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        Assert.Equal("Customers", metadata.TableName);
        Assert.Equal("sales", metadata.Schema);
    }

    [Fact]
    public void Metadata_HonorsNoCrudAttribute()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        Assert.DoesNotContain(metadata.Columns, c => c.PropertyName == nameof(Customer.Internal));
    }

    [Fact]
    public void Metadata_HonorsColumnAttributeForName()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        var code = metadata.Columns.Single(c => c.PropertyName == nameof(Customer.Code));
        Assert.Equal("customer_code", code.ColumnName);
    }

    [Fact]
    public void Metadata_IdentityExcludedFromInsertable()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        Assert.DoesNotContain(metadata.InsertableColumns, c => c.PropertyName == nameof(Customer.Id));
    }

    [Fact]
    public void Metadata_KeyTakesPrecedenceOverIdentity()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        Assert.Equal(nameof(Customer.Code), metadata.KeyColumn?.PropertyName);
    }

    [Fact]
    public void Metadata_CompiledGetterAndSetterRoundTrip()
    {
        var metadata = ModelMetadataCache.Get<Customer>();
        var nameBinding = metadata.Columns.Single(c => c.PropertyName == nameof(Customer.Name));
        var customer = new Customer();
        nameBinding.Setter(customer, "Acme");
        Assert.Equal("Acme", nameBinding.Getter(customer));
    }
}
