using System.Data.Common;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.SqlServer.Hosting;
using Microsoft.Data.SqlClient;

namespace Akka.Reminders.Tests.Storage;

public sealed class SqlServerReminderStorageSettingsSpecs
{
    private const string ConnectionString = "Server=localhost;Database=reminders;User ID=sa;Password=secret";

    private static ValueTask<DbConnection> Factory(CancellationToken _) =>
        new(new SqlConnection(ConnectionString));

    private static Task<string> GetTokenAsync(CancellationToken _) => Task.FromResult("token");

    [Fact]
    public void Validate_ShouldAcceptConnectionStringOnly()
    {
        var settings = SqlServerReminderStorageSettings.Create(ConnectionString);

        settings.Validate();
        Assert.Null(settings.ConnectionFactory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldAcceptConnectionFactoryOnly(string? connectionString)
    {
        var settings = new SqlServerReminderStorageSettings
        {
            ConnectionString = connectionString!,
            ConnectionFactory = Factory
        };

        settings.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldReject_WhenNeitherConnectionStringNorFactoryIsSet(string? connectionString)
    {
        var settings = new SqlServerReminderStorageSettings { ConnectionString = connectionString! };

        var ex = Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Equal(nameof(SqlServerReminderStorageSettings.ConnectionString), ex.ParamName);
        Assert.Contains(nameof(SqlServerReminderStorageSettings.ConnectionFactory), ex.Message);
    }

    [Fact]
    public void Validate_ShouldReject_WhenBothConnectionStringAndFactoryAreSet()
    {
        var settings = SqlServerReminderStorageSettings.Create(ConnectionString) with { ConnectionFactory = Factory };

        var ex = Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Equal(nameof(SqlServerReminderStorageSettings.ConnectionFactory), ex.ParamName);
    }

    [Fact]
    public void WithSqlServerStorage_ShouldRejectNullConnectionFactory()
    {
        var builder = new ReminderConfigurationBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithSqlServerStorage((Func<CancellationToken, ValueTask<DbConnection>>)null!));
    }

    [Fact]
    public async Task WithSqlServerStorage_ConnectionFactory_ShouldBuildSqlServerStorage()
    {
        // Same shape as the README example: an async lambda that sets an access token.
        var setup = new ReminderConfigurationBuilder()
            .WithSqlServerStorage(
                connectionFactory: async ct => new SqlConnection("Server=localhost;Database=reminders")
                {
                    AccessToken = await GetTokenAsync(ct)
                },
                schemaName: "custom",
                autoInitialize: false)
            .Build();

        var system = Akka.Actor.ActorSystem.Create("factory-hosting-settings");
        try
        {
            Assert.IsType<SqlServerReminderStorage>(setup.StorageFactory(system));
        }
        finally
        {
            await system.Terminate();
        }
    }
}
