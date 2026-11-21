using GrayMint.TlsCertificates.Persistence;
using GrayMint.TlsCertificates.Test.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.TlsCertificates.Test.Tests;

[TestClass]
public class SetupTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    // a host's start, which checks the tables
    private static async Task Start(TestTlsHost host, CancellationToken cancellationToken)
    {
        foreach (var hostedService in host.Services.GetServices<IHostedService>().OfType<TlsCertificatesStartup>())
            await hostedService.StartAsync(cancellationToken);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Setup_makes_the_database_its_tables_and_their_version_any_number_of_times(
        TestDatabaseProvider provider)
    {
        await using var host = TestTlsHost.CreateEmpty(provider);

        await host.Services.UseGrayMintTlsCertificates(CancellationToken);
        await host.Services.UseGrayMintTlsCertificates(CancellationToken);
        await Start(host, CancellationToken);

        await using var dbContext = host.CreateDbContext();
        var metadata = await dbContext.Metadata.AsNoTracking().SingleAsync(CancellationToken);
        Assert.AreEqual(TlsCertificatesSchema.Version, metadata.SchemaVersion);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Setup_puts_the_tables_beside_the_hosts_own(TestDatabaseProvider provider)
    {
        await using var host = TestTlsHost.CreateEmpty(provider);
        await using (var dbContext = host.CreateDbContext()) {
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync(CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE HostOrders (OrderId int PRIMARY KEY)", CancellationToken);
        }

        await host.Services.UseGrayMintTlsCertificates(CancellationToken);
        await Start(host, CancellationToken);

        await using (var dbContext = host.CreateDbContext()) {
            var hostRows = await dbContext.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM HostOrders")
                .SingleAsync(CancellationToken);
            Assert.AreEqual(0, hostRows, "the host's table is left as it was");
        }
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Start_refuses_a_database_without_the_tables(TestDatabaseProvider provider)
    {
        await using var host = TestTlsHost.CreateEmpty(provider);
        await using (var dbContext = host.CreateDbContext())
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync(CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Start(host, CancellationToken));
        StringAssert.Contains(ex.Message, "UseGrayMintTlsCertificates");
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Start_refuses_tables_of_another_version(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        await using (var dbContext = host.CreateDbContext())
            await dbContext.Metadata.ExecuteUpdateAsync(x => x.SetProperty(y => y.SchemaVersion, 2), CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Start(host, CancellationToken));
        StringAssert.Contains(ex.Message, "of version 2");
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Setup_refuses_some_of_the_tables_and_changes_nothing(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        await using (var dbContext = host.CreateDbContext())
            await dbContext.Database.ExecuteSqlRawAsync(provider == TestDatabaseProvider.PostgreSql
                ? $"DROP TABLE {TlsCertificatesSchema.Name}.\"{TlsCertificatesSchema.AccountsTable}\""
                : $"DROP TABLE {TlsCertificatesSchema.Name}.[{TlsCertificatesSchema.AccountsTable}]", CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            host.Services.UseGrayMintTlsCertificates(CancellationToken));
        StringAssert.Contains(ex.Message, "Only some");

        await using (var dbContext = host.CreateDbContext())
            Assert.AreEqual(0, await dbContext.Certificates.CountAsync(CancellationToken), "the other tables are there still");
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Start_refuses_a_host_that_registered_no_domains(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITlsCertificateProtector>(host.Protector);
        services.AddGrayMintTlsCertificates();
        services.AddGrayMintTlsCertificatesDb(options => host.UseDatabase(options));
        await using var serviceProvider = services.BuildServiceProvider();

        var startup = serviceProvider.GetServices<IHostedService>().OfType<TlsCertificatesStartup>().Single();
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => startup.StartAsync(CancellationToken));
        StringAssert.Contains(ex.Message, nameof(ITlsCertificateDomains));
    }
}
