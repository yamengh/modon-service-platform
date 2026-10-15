using FormCMS.Infrastructure.RelationDbDao;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Modon.Platform.Data;

public static class ModonDatabase
{
    public static void AddModonDbContext(this IServiceCollection services, DatabaseProvider provider, string connStr)
        => services.AddDbContext<ModonDbContext>(options => _ = provider switch
        {
            DatabaseProvider.Postgres => options.UseNpgsql(connStr),
            DatabaseProvider.Sqlite => options.UseSqlite(connStr),
            _ => throw new NotSupportedException($"Database provider {provider} is not supported by Modon.Platform.")
        });

    /// <summary>
    /// PostgreSQL: applies EF Core migrations (server/Modon.Platform/Data/Migrations).
    /// SQLite (tests and no-Docker development): creates the Modon tables once if they are missing.
    /// The SQLite path does not evolve existing tables — delete modon-dev.db after a schema change.
    /// </summary>
    public static async Task EnsureModonSchemaAsync(this WebApplication app, DatabaseProvider provider)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();

        if (provider == DatabaseProvider.Postgres)
        {
            await db.Database.MigrateAsync();
            return;
        }

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'modon_service_requests'";
        var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        if (!exists) await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }
}

/// <summary>Used only by "dotnet ef" to generate PostgreSQL migrations; no database connection is made.</summary>
public sealed class ModonDbContextDesignFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ModonDbContext>
{
    public ModonDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ModonDbContext>()
            .UseNpgsql("Host=localhost;Database=modon_design_time")
            .Options);
}
