using GrayMint.Common.Acme;
using GrayMint.TlsCertificates.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

/// <summary>Registers the module in a host, and makes its tables.</summary>
public static class TlsCertificatesExtension
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers what every host of the module reads: <see cref="TlsCertificateStore"/>, for the domains'
        /// certificates and challenges; <see cref="TlsCertificateService"/>, for how they stand; and the certificate of
        /// each TLS connection, for Kestrel (<c>UseTlsCertificates</c>). Their context comes from
        /// <c>AddGrayMintTlsCertificatesDb</c>; the host registers its <see cref="ITlsCertificateDomains"/>, as a
        /// singleton, and its <see cref="ITlsCertificateProtector"/>. The host doesn't start without them, on a
        /// database without the tables, or with tables of another version.
        /// </summary>
        /// <param name="configure">Sets the options; they are checked here, and a wrong one stops the registration.</param>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintTlsCertificates(Action<TlsCertificatesOptions>? configure = null)
        {
            var options = new TlsCertificatesOptions();
            configure?.Invoke(options);
            options.Validate();

            services.AddSingleton(Options.Create(options));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(serviceProvider => new TlsCertificateStore(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<ITlsCertificateDomains>(),
                serviceProvider.GetRequiredService<IOptions<TlsCertificatesOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
            services.AddScoped(serviceProvider =>
                new TlsCertificateService(serviceProvider.GetRequiredService<TlsCertificatesDbContext>()));
            services.AddSingleton(serviceProvider => new TlsCertificateSelector(
                serviceProvider.GetRequiredService<TlsCertificateStore>().GetPfx,
                new TlsCertificateSelectorOptions {
                    CacheTimeout = options.CertificateCacheTimeout,
                    IdleTimeout = options.CertificateIdleTimeout,
                    DefaultDomain = options.DefaultDomain
                },
                serviceProvider.GetRequiredService<ILogger<TlsCertificateSelector>>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
            services.AddHostedService<TlsCertificatesStartup>();
            return services;
        }

        /// <summary>
        /// The tables on a connection of their own: in a database of their own, which the setup makes, or in the host's
        /// database, beside its tables, in a schema of their own (<see cref="TlsCertificatesSchema.Name"/>).
        /// </summary>
        /// <param name="dbOptionsAction">
        /// Names the database, as in <c>options =&gt; options.UseNpgsql(connectionString)</c>.
        /// </param>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintTlsCertificatesDb(Action<DbContextOptionsBuilder> dbOptionsAction)
        {
            // EF maps the non-generic DbContextOptions to the last context it registers, and a host's context that takes
            // them would get the module's: they stay the host's
            var hostOptions = services.Where(x => x.ServiceType == typeof(DbContextOptions)).ToArray();
            services.AddDbContext<TlsCertificatesDbContext>(dbOptionsAction);
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(DbContextOptions)).Except(hostOptions).ToArray())
                services.Remove(descriptor);
            return services;
        }

        /// <summary>
        /// Registers the renewal, in the one host that runs it, beside <c>AddGrayMintTlsCertificates</c>: a job that
        /// orders the certificates the host's domains need every
        /// <see cref="TlsCertificateRenewalOptions.Interval"/>, and <see cref="TlsCertificateRenewer"/>, which runs it.
        /// A second host with it, or a second start of this one, waits on the lease of the one running.
        /// </summary>
        /// <param name="configure">Sets the options; they are checked here, and a wrong one stops the registration.</param>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintTlsCertificateRenewal(Action<TlsCertificateRenewalOptions> configure)
        {
            var options = new TlsCertificateRenewalOptions();
            configure(options);
            options.Validate();

            services.AddSingleton(Options.Create(options));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IAcmeClient>(new AcmeClient(new AcmeClientOptions {
                DirectoryUri = options.DirectoryUri,
                HttpClient = options.AuthorityHttpClient
            }));
            services.AddSingleton(serviceProvider => new AcmeCertificateIssuer(
                serviceProvider.GetRequiredService<IAcmeClient>(),
                serviceProvider.GetRequiredService<ILogger<AcmeCertificateIssuer>>()));
            services.AddScoped(serviceProvider => new TlsCertificateRenewer(
                serviceProvider.GetRequiredService<TlsCertificatesDbContext>(),
                serviceProvider.GetRequiredService<ITlsCertificateDomains>(),
                serviceProvider.GetRequiredService<ITlsCertificateProtector>(),
                serviceProvider.GetRequiredService<AcmeCertificateIssuer>(),
                serviceProvider,
                serviceProvider.GetRequiredService<IOptions<TlsCertificateRenewalOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<TlsCertificateRenewer>>()));
            services.AddHostedService<TlsCertificateRenewalJob>();
            return services;
        }

        /// <summary>
        /// Registers <see cref="TlsDomainChecker"/>, the checks of a user's domain before the host serves it: that it points
        /// to the host, and that it is the user's. It needs no tables, nor the module's other registrations. The DNS it
        /// reads is the host's <see cref="ITlsDomainDns"/> when one is registered, and the module's own otherwise.
        /// </summary>
        /// <param name="configure">Sets the options; they are checked here, and a wrong one stops the registration.</param>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintTlsDomainChecks(Action<TlsDomainCheckOptions> configure)
        {
            var options = new TlsDomainCheckOptions();
            configure(options);
            options.Validate();

            services.AddSingleton(Options.Create(options));

            // the checks need nothing of a host, logging neither: a host without it logs nothing of the lookups
            services.TryAddSingleton<ITlsDomainDns>(serviceProvider => new TlsDomainDns(
                serviceProvider.GetRequiredService<IOptions<TlsDomainCheckOptions>>(),
                serviceProvider.GetService<ILogger<TlsDomainDns>>() ?? NullLogger<TlsDomainDns>.Instance));
            services.AddSingleton(serviceProvider => new TlsDomainChecker(
                serviceProvider.GetRequiredService<ITlsDomainDns>(),
                serviceProvider.GetRequiredService<IOptions<TlsDomainCheckOptions>>()));
            return services;
        }
    }

    /// <summary>
    /// Makes the tables when they are not there, and in a database of their own the database too, before the host
    /// starts. It needs a login that may make them, and can run any number of times; one host runs it, and the others
    /// check the tables at their start. It refuses a database with only some of the tables, or with tables of another
    /// version, and changes nothing in it.
    /// </summary>
    /// <param name="serviceProvider">The host's services, with the module registered.</param>
    /// <param name="cancellationToken">The token that cancels the setup.</param>
    /// <returns>The host's services.</returns>
    public static async Task<IServiceProvider> UseGrayMintTlsCertificates(this IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await TlsCertificatesSetup.Run(scope.ServiceProvider.GetRequiredService<TlsCertificatesDbContext>(),
            cancellationToken);
        return serviceProvider;
    }
}
