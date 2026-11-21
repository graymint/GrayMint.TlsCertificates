using GrayMint.TlsCertificates.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

[TestClass]
public class RegistrationTest
{
    // a host's context of the kind EF made first, which takes the options of no type in particular
    private sealed class HostDbContext(DbContextOptions options) : DbContext(options);

    [TestMethod]
    public void Host_context_that_takes_options_of_no_type_keeps_its_own()
    {
        var services = new ServiceCollection();
        services.AddDbContext<HostDbContext>(options => options.UseSqlServer("Server=host;Database=host"));
        services.AddGrayMintTlsCertificatesDb(options => options.UseSqlServer("Server=module;Database=module"));
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var hostDbContext = scope.ServiceProvider.GetRequiredService<HostDbContext>();
        StringAssert.Contains(hostDbContext.Database.GetConnectionString(), "Initial Catalog=host");
        var moduleDbContext = scope.ServiceProvider.GetRequiredService<TlsCertificatesDbContext>();
        StringAssert.Contains(moduleDbContext.Database.GetConnectionString(), "Initial Catalog=module");
    }
}
