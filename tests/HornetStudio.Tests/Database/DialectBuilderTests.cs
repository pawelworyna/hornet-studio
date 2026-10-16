using FluentAssertions;
using HornetStudio.Core.Database.Dialects;
using HornetStudio.Core.Domain.Models;
using Xunit;

namespace HornetStudio.Tests.Database;

public class DialectBuilderTests
{
    [Fact]
    public void SqliteChangeBuilder_GeneratesValidStatements()
    {
        var builder = new SqliteChangeBuilder("users");

        var col = new ExtendedTableColumn
        {
            ColumnName = "phone",
            DataType = "TEXT",
            Nullable = true,
            DefaultValue = "'N/A'"
        };

        var addCol = builder.AddColumn(col);
        addCol.Should().Be("ALTER TABLE \"users\" ADD COLUMN \"phone\" TEXT NULL DEFAULT 'N/A'");

        var dropCol = builder.DropColumn("phone");
        dropCol.Should().Be("ALTER TABLE \"users\" DROP COLUMN \"phone\"");

        var renameCol = builder.RenameColumn("phone", "mobile");
        renameCol.Should().Be("ALTER TABLE \"users\" RENAME COLUMN \"phone\" TO \"mobile\"");

        var createIdx = builder.CreateIndex("idx_users_mobile", ["mobile"], isUnique: true);
        createIdx.Should().Be("CREATE UNIQUE INDEX \"idx_users_mobile\" ON \"users\" (\"mobile\")");
    }

    [Fact]
    public void MySqlChangeBuilder_UsesBackticks()
    {
        var builder = new MySqlChangeBuilder("orders");

        var col = new ExtendedTableColumn
        {
            ColumnName = "total",
            DataType = "DECIMAL(10,2)",
            Nullable = false,
            DefaultValue = "0.00"
        };

        var addCol = builder.AddColumn(col);
        addCol.Should().Be("ALTER TABLE `orders` ADD COLUMN `total` DECIMAL(10,2) NOT NULL DEFAULT 0.00");

        var dropIdx = builder.DropIndex("idx_orders_date");
        dropIdx.Should().Be("DROP INDEX `idx_orders_date` ON `orders`");
    }

    [Fact]
    public void SqlServerChangeBuilder_UsesSquareBrackets()
    {
        var builder = new SqlServerChangeBuilder("invoices", "billing");

        builder.TableName.Should().Be("[billing].[invoices]");

        var col = new ExtendedTableColumn
        {
            ColumnName = "amount",
            DataType = "MONEY",
            Nullable = false
        };

        var addCol = builder.AddColumn(col);
        addCol.Should().Be("ALTER TABLE [billing].[invoices] ADD COLUMN [amount] MONEY NOT NULL");

        var dropIdx = builder.DropIndex("idx_amount");
        dropIdx.Should().Be("DROP INDEX [idx_amount] ON [billing].[invoices]");
    }

    [Fact]
    public void FirebirdChangeBuilder_GeneratesFirebirdStatements()
    {
        var builder = new FirebirdChangeBuilder("departments");

        builder.TableName.Should().Be("\"departments\"");

        var col = new ExtendedTableColumn
        {
            ColumnName = "budget",
            DataType = "DECIMAL(12,2)",
            Nullable = false
        };

        var addCol = builder.AddColumn(col);
        addCol.Should().Be("ALTER TABLE \"departments\" ADD \"budget\" DECIMAL(12,2) NOT NULL");

        var dropCol = builder.DropColumn("budget");
        dropCol.Should().Be("ALTER TABLE \"departments\" DROP \"budget\"");

        var renameCol = builder.RenameColumn("budget", "allocated_budget");
        renameCol.Should().Be("ALTER TABLE \"departments\" ALTER COLUMN \"budget\" TO \"allocated_budget\"");

        var dropIdx = builder.DropIndex("idx_dept_budget");
        dropIdx.Should().Be("DROP INDEX \"idx_dept_budget\"");
    }
}
