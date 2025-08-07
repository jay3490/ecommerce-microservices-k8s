using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Mapping that differs between PostgreSQL, which the services run on, and the in-memory
/// SQLite database their handler tests use. The PostgreSQL model is unaffected.
/// </summary>
public static class ProviderConventions
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// SQLite cannot compare or sort DateTimeOffset columns, so they are stored as numbers there.
    /// </summary>
    public static void ApplyProviderConventions(this ModelConfigurationBuilder configurationBuilder, DatabaseFacade database)
    {
        if (database.ProviderName == SqliteProvider)
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    /// <summary>
    /// Adds a <c>Version</c> concurrency token. On PostgreSQL it maps to the <c>xmin</c> system
    /// column, so two transactions updating the same row cannot both commit. SQLite has no
    /// equivalent; there it is a plain column that keeps its initial value.
    /// </summary>
    public static void HasRowVersion<TEntity>(this EntityTypeBuilder<TEntity> entity, DatabaseFacade database)
        where TEntity : class
    {
        var version = entity.Property<uint>("Version");
        if (database.ProviderName == SqliteProvider)
        {
            version.IsConcurrencyToken().HasDefaultValue(0u);
        }
        else
        {
            version.IsRowVersion();
        }
    }
}
