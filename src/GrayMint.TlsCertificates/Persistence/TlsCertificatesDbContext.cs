using GrayMint.TlsCertificates.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GrayMint.TlsCertificates.Persistence;

// The module's own tables, in a schema of their own beside the host's. It has no migrations: the setup makes the tables
// from this model (TlsCertificatesSetup), and TlsCertificatesSchema.Version says which shape they have.
internal sealed class TlsCertificatesDbContext(DbContextOptions<TlsCertificatesDbContext> options) : DbContext(options)
{
    // the key of the metadata's one row
    public const int MetadataRowId = 1;

    private const int DomainLength = 253;
    private const int DirectoryUriLength = 200;

    // SQL Server keeps no kind with a time, so every time is said to be UTC as it is read, which is how it was written
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);

    public DbSet<CertificateModel> Certificates => Set<CertificateModel>();
    public DbSet<AccountModel> Accounts => Set<AccountModel>();
    public DbSet<MetadataModel> Metadata => Set<MetadataModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TlsCertificatesSchema.Name);

        // Domains, URLs, tokens and base64 are ASCII on both databases. An error's message is the authority's words, in
        // any script.
        modelBuilder.Entity<CertificateModel>(entity => {
            entity.ToTable(TlsCertificatesSchema.CertificatesTable);
            entity.HasKey(x => x.Domain);
            entity.Property(x => x.Domain).HasMaxLength(DomainLength).IsUnicode(false);
            entity.Property(x => x.IssuerDirectoryUri).HasMaxLength(DirectoryUriLength).IsUnicode(false);
            entity.Property(x => x.Pfx).IsUnicode(false);
            entity.Property(x => x.Thumbprint).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.ChallengeToken).HasMaxLength(256).IsUnicode(false);
            entity.Property(x => x.ChallengeKeyAuthorization).HasMaxLength(512).IsUnicode(false);
            entity.Property(x => x.ErrorCode).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ErrorMessage).HasMaxLength(TlsCertificateRenewer.MaxErrorMessageLength);
        });

        modelBuilder.Entity<AccountModel>(entity => {
            entity.ToTable(TlsCertificatesSchema.AccountsTable);
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).ValueGeneratedOnAdd();
            entity.Property(x => x.DirectoryUri).HasMaxLength(DirectoryUriLength).IsUnicode(false);
            entity.Property(x => x.AccountKey).IsUnicode(false);

            // one account for each authority, which two runs that met couldn't both make
            entity.HasIndex(x => x.DirectoryUri).IsUnique();
        });

        modelBuilder.Entity<MetadataModel>(entity => {
            entity.ToTable(TlsCertificatesSchema.MetadataTable);
            entity.HasKey(x => x.MetadataId);
            entity.Property(x => x.MetadataId).ValueGeneratedNever();
            entity.Property(x => x.LeaseOwner).HasMaxLength(64).IsUnicode(false);
        });

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties())) {
            if (property.ClrType == typeof(DateTime))
                property.SetValueConverter(UtcConverter);
            else if (property.ClrType == typeof(DateTime?))
                property.SetValueConverter(NullableUtcConverter);
        }
    }
}
