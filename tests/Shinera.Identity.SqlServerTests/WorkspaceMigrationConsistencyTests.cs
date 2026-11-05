using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>Protects all SQL Server suites from drift between the runtime EF model and the committed snapshot.</summary>
public sealed class WorkspaceMigrationConsistencyTests
{
    [Fact]
    public void Migration_snapshot_matches_runtime_identity_model()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=(local);Database=SnapshotCheck;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new IdentityDbContext(options, new TenantContext());
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var runtime = db.GetService<IDesignTimeModel>().Model;
        var changes = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), runtime.GetRelationalModel());

        static string Describe(MigrationOperation op) => op switch
        {
            AlterColumnOperation x => $"AlterColumn: {x.Table}.{x.Name}: {x.OldColumn.ColumnType}/{x.OldColumn.IsNullable} => {x.ColumnType}/{x.IsNullable}",
            AddColumnOperation x => $"AddColumn: {x.Table}.{x.Name} {x.ColumnType} nullable={x.IsNullable}",
            DropColumnOperation x => $"DropColumn: {x.Table}.{x.Name}",
            CreateIndexOperation x => $"CreateIndex: {x.Table}.{x.Name} {string.Join(",",x.Columns)}",
            DropIndexOperation x => $"DropIndex: {x.Table}.{x.Name}",
            AddForeignKeyOperation x => $"AddFK: {x.Table}.{x.Name}",
            DropForeignKeyOperation x => $"DropFK: {x.Table}.{x.Name}",
            CreateTableOperation x => $"CreateTable: {x.Name}",
            DropTableOperation x => $"DropTable: {x.Name}",
            _ => op.GetType().Name
        };
        Assert.True(changes.Count == 0, "EF migration snapshot drift:\n" +
            string.Join("\n", changes.Select(Describe)));
    }
}
