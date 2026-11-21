using GrayMint.Common.Acme;
using GrayMint.TlsCertificates.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Helper;

// A host of one test, with a database of its own on one of the two servers compose.yaml starts: the module registered as
// a host registers it, with the test's domains, protector, clock and authority. The database has a name nobody else
// uses and is dropped with the test.
public sealed class TestTlsHost : IAsyncDisposable
{
    // by address: "localhost" is tried over IPv6 first, where the containers don't listen
    private const string PostgreSqlServer = "Host=127.0.0.1;Port=5441;Username=postgres;Password=postgres";
    private const string SqlServerServer =
        "Server=127.0.0.1,14341;User ID=sa;Password=GrayMint-TlsCertificates-2026;TrustServerCertificate=true";

    // the fake authority's directory, which nothing is ever sent to
    public static readonly Uri FakeDirectoryUri = new("https://acme.example.com/directory");

    private readonly Action<TlsCertificatesOptions>? _configure;
    private readonly Action<TlsCertificateRenewalOptions>? _configureRenewal;
    private readonly bool _realAuthority;

    public TestDatabaseProvider Provider { get; }
    public string DatabaseName { get; } = $"test_{Guid.CreateVersion7():N}";
    public TestDomains Domains { get; } = new();
    public TestProtector Protector { get; } = new();
    public TestTimeProvider Time { get; } = new();
    public TestAcmeClient AcmeClient { get; }
    public TestCommandCounter Commands { get; } = new();
    public ServiceProvider Services { get; }

    public string ConnectionString => Provider == TestDatabaseProvider.PostgreSql
        ? $"{PostgreSqlServer};Database={DatabaseName}"
        : $"{SqlServerServer};Database={DatabaseName}";

    public TlsCertificateStore Store => Services.GetRequiredService<TlsCertificateStore>();

    private TestTlsHost(TestDatabaseProvider provider, Action<TlsCertificatesOptions>? configure,
        Action<TlsCertificateRenewalOptions>? configureRenewal, bool realAuthority)
    {
        Provider = provider;
        _configure = configure;
        _configureRenewal = configureRenewal;
        _realAuthority = realAuthority;
        AcmeClient = new TestAcmeClient(Time);
        var services = new ServiceCollection();
        Register(services);
        Services = services.BuildServiceProvider();
    }

    public static async Task<TestTlsHost> Create(TestDatabaseProvider provider, CancellationToken cancellationToken,
        Action<TlsCertificatesOptions>? configure = null, Action<TlsCertificateRenewalOptions>? configureRenewal = null,
        bool realAuthority = false)
    {
        var host = new TestTlsHost(provider, configure, configureRenewal, realAuthority);
        try {
            await host.Services.UseGrayMintTlsCertificates(cancellationToken);
            return host;
        }
        catch {
            // no test gets it to drop, so its database goes here
            await host.DisposeAsync();
            throw;
        }
    }

    // only a name: a setup of the test's choice makes the database
    public static TestTlsHost CreateEmpty(TestDatabaseProvider provider)
    {
        return new TestTlsHost(provider, null, null, false);
    }

    // what a host registers: the module on this database, with the test's domains, protector, clock and authority
    public void Register(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ITlsCertificateDomains>(Domains);
        services.AddSingleton<ITlsCertificateProtector>(Protector);
        services.AddGrayMintTlsCertificates(options => _configure?.Invoke(options));
        services.AddGrayMintTlsCertificatesDb(options => UseDatabase(options).AddInterceptors(Commands));
        services.AddGrayMintTlsCertificateRenewal(options => {
            options.DirectoryUri = FakeDirectoryUri;
            options.SelfCheckHandlerFactory = serviceProvider =>
                new TestChallengeHandler(serviceProvider.GetRequiredService<TlsCertificateStore>(), Domains);
            options.NewDomainDelay = TimeSpan.Zero;
            options.PollInterval = TimeSpan.FromMilliseconds(100);
            _configureRenewal?.Invoke(options);
        });
        if (!_realAuthority)
            services.AddSingleton<IAcmeClient>(AcmeClient);
    }

    public DbContextOptionsBuilder UseDatabase(DbContextOptionsBuilder options)
    {
        return Provider == TestDatabaseProvider.PostgreSql
            ? options.UseNpgsql(ConnectionString)
            : options.UseSqlServer(ConnectionString);
    }

    // a run of the renewal, as its job runs it
    public async Task<TlsCertificateRenewalResult> Renew(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TlsCertificateRenewer>().Run(cancellationToken);
    }

    public async Task<TlsCertificateInfo?> GetInfo(string domain, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TlsCertificateService>().Get(domain, cancellationToken);
    }

    // the module's own context, for a test that looks behind its services
    internal TlsCertificatesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TlsCertificatesDbContext>();
        UseDatabase(options);
        return new TlsCertificatesDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }
}
