using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class ConnectionStringProviderTests
{
    [Theory]
    [InlineData("Data Source=prototype-manager.db", true)]
    [InlineData("Data Source=/tmp/app.db", true)]
    [InlineData(
        "Server=tcp:example.database.windows.net,1433;Initial Catalog=prototype-manager-dev;User ID=user;Password=secret;Encrypt=True;",
        false)]
    [InlineData(
        "Server=(localdb)\\mssqllocaldb;Database=PrototypeManager;Trusted_Connection=True;",
        false)]
    public void IsSqliteConnectionString_DetectsProvider(string connectionString, bool expected)
    {
        Assert.Equal(expected, InfrastructureServiceCollectionExtensions.IsSqliteConnectionString(connectionString));
    }
}
