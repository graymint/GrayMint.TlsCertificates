using GrayMint.TlsCertificates.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrayMint.TlsCertificates.Persistence;

// Makes the tables when they are not there, any number of times; and refuses a start on a database without them, or
// with tables of another version.
internal static class TlsCertificatesSetup
{
    private const int TableCount = 3;

    // INFORMATION_SCHEMA is on both databases, and lists only the tables the login has a right on. Its names are in
    // upper case, as SQL Server has them: a database whose collation tells case apart wouldn't find them in lower case,
    // and PostgreSQL folds them to lower case either way.
    private const string CountTablesSql =
        $"""
         SELECT CAST(COUNT(*) AS int) AS "Value" FROM INFORMATION_SCHEMA.TABLES
         WHERE TABLE_SCHEMA = '{TlsCertificatesSchema.Name}' AND TABLE_TYPE = 'BASE TABLE'
           AND TABLE_NAME IN ('{TlsCertificatesSchema.CertificatesTable}', '{TlsCertificatesSchema.AccountsTable}',
                              '{TlsCertificatesSchema.MetadataTable}')
         """;

    public static async Task Run(TlsCertificatesDbContext dbContext, CancellationToken cancellationToken)
    {
        // a database of the module's own is made here; a host's database is there already, and its tables are left be
        var databaseCreator = dbContext.GetService<IRelationalDatabaseCreator>();
        if (!await databaseCreator.ExistsAsync(cancellationToken))
            await databaseCreator.CreateAsync(cancellationToken);

        // the tables and their version are made together, or neither
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)) {
            var tableCount = await CountTables(dbContext, cancellationToken);
            if (tableCount == 0) {
                await databaseCreator.CreateTablesAsync(cancellationToken);
                dbContext.Metadata.Add(new MetadataModel {
                    MetadataId = TlsCertificatesDbContext.MetadataRowId,
                    SchemaVersion = TlsCertificatesSchema.Version
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else if (tableCount != TableCount)
                throw new InvalidOperationException(
                    $"Only some of the certificates' tables are in the schema {TlsCertificatesSchema.Name}, and the " +
                    "setup doesn't know how they came to be so. It changed nothing.");

            await transaction.CommitAsync(cancellationToken);
        }

        await ThrowIfNotReady(dbContext, cancellationToken);
    }

    // So a host that never ran the setup stops at its start, not at its first connection; and so does a build of the
    // module on tables of another shape, which its reads would read wrong.
    public static async Task ThrowIfNotReady(TlsCertificatesDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CountTables(dbContext, cancellationToken) != TableCount)
            throw new InvalidOperationException(
                $"The certificates' tables are not in the schema {TlsCertificatesSchema.Name}, or this login has no " +
                "right on them. The setup (UseGrayMintTlsCertificates) makes them.");

        var versions = await dbContext.Metadata
            .AsNoTracking()
            .Select(x => x.SchemaVersion)
            .ToArrayAsync(cancellationToken);
        if (versions is not [TlsCertificatesSchema.Version])
            throw new InvalidOperationException(
                $"The certificates' tables in the schema {TlsCertificatesSchema.Name} are of version " +
                $"{(versions.Length == 0 ? "none" : string.Join(" and ", versions))}, and this build of the module " +
                $"works on version {TlsCertificatesSchema.Version}.");
    }

    private static Task<int> CountTables(TlsCertificatesDbContext dbContext, CancellationToken cancellationToken)
    {
        return dbContext.Database.SqlQueryRaw<int>(CountTablesSql).SingleAsync(cancellationToken);
    }
}
