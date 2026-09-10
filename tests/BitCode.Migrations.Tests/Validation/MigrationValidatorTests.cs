using BitCode.Framework.Tests.Migrations.TestModel.Migrations;
using BitCode.Framework.Tools.Migrations.Core;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace BitCode.Framework.Tests.Migrations.Validation;

public class MigrationValidatorTests
{
    [Fact]
    public void ValidateOperations_WhenSafeExpandOperations_ReturnsValidWithoutErrors()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new CreateTableOperation { Name = "NewOrders" },
            new AddColumnOperation
            {
                Table = "Orders",
                Name = "Notes",
                ClrType = typeof(string),
                IsNullable = true
            },
            new AddColumnOperation
            {
                Table = "Orders",
                Name = "IsActive",
                ClrType = typeof(bool),
                IsNullable = false,
                DefaultValue = true
            }
        };

        var result = validator.ValidateOperations("20260910_SafeMigration", operations);

        result.IsValid.Should().BeTrue();
        result.ErrorCount.Should().Be(0);
        result.WarningCount.Should().Be(1); // AddColumn NOT NULL con default genera Warning preventivo
    }

    [Fact]
    public void ValidateOperations_WhenDropTable_ReturnsError()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new DropTableOperation { Name = "LegacyCustomers" }
        };

        var result = validator.ValidateOperations("20260910_DropTableMigration", operations);

        result.IsValid.Should().BeFalse();
        result.ErrorCount.Should().Be(1);
        result.Violations.Should().ContainSingle(v =>
            v.Severity == RuleSeverity.Error &&
            v.OperationType == nameof(DropTableOperation) &&
            v.Target == "LegacyCustomers");
    }

    [Fact]
    public void ValidateOperations_WhenDropColumn_ReturnsError()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new DropColumnOperation { Table = "Users", Name = "LegacyAddress" }
        };

        var result = validator.ValidateOperations("20260910_DropColumnMigration", operations);

        result.IsValid.Should().BeFalse();
        result.ErrorCount.Should().Be(1);
        result.Violations.Should().ContainSingle(v =>
            v.Severity == RuleSeverity.Error &&
            v.OperationType == nameof(DropColumnOperation) &&
            v.Target == "Users.LegacyAddress");
    }

    [Fact]
    public void ValidateOperations_WhenRenameTable_ReturnsError()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new RenameTableOperation { Name = "OldName", NewName = "NewName" }
        };

        var result = validator.ValidateOperations("20260910_RenameTableMigration", operations);

        result.IsValid.Should().BeFalse();
        result.ErrorCount.Should().Be(1);
        result.Violations.Should().ContainSingle(v =>
            v.Severity == RuleSeverity.Error &&
            v.OperationType == nameof(RenameTableOperation));
    }

    [Fact]
    public void ValidateOperations_WhenRenameColumn_ReturnsError()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new RenameColumnOperation { Table = "Products", Name = "OldSku", NewName = "Sku" }
        };

        var result = validator.ValidateOperations("20260910_RenameColumnMigration", operations);

        result.IsValid.Should().BeFalse();
        result.ErrorCount.Should().Be(1);
        result.Violations.Should().ContainSingle(v =>
            v.Severity == RuleSeverity.Error &&
            v.OperationType == nameof(RenameColumnOperation));
    }

    [Fact]
    public void ValidateOperations_WhenAddNonNullableColumnWithoutDefault_ReturnsError()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var operations = new MigrationOperation[]
        {
            new AddColumnOperation
            {
                Table = "Invoices",
                Name = "TotalAmount",
                ClrType = typeof(decimal),
                IsNullable = false,
                DefaultValue = null,
                DefaultValueSql = null
            }
        };

        var result = validator.ValidateOperations("20260910_AddRequiredWithoutDefault", operations);

        result.IsValid.Should().BeFalse();
        result.ErrorCount.Should().Be(1);
        result.Violations.Should().ContainSingle(v =>
            v.Severity == RuleSeverity.Error &&
            v.OperationType == nameof(AddColumnOperation));
    }

    [Fact]
    public void ValidateOperations_WhenAllowDestructiveTrue_DowngradesErrorsToWarnings()
    {
        var validator = new MigrationValidator(allowDestructive: true);
        var operations = new MigrationOperation[]
        {
            new DropTableOperation { Name = "ObsoleteTable" },
            new DropColumnOperation { Table = "Orders", Name = "ObsoleteColumn" },
            new RenameTableOperation { Name = "T1", NewName = "T2" },
            new RenameColumnOperation { Table = "Orders", Name = "C1", NewName = "C2" }
        };

        var result = validator.ValidateOperations("20260910_ContractPhaseMigration", operations);

        result.IsValid.Should().BeTrue();
        result.ErrorCount.Should().Be(0);
        result.WarningCount.Should().Be(4);
    }

    [Fact]
    public void ValidateMigration_WithRealExpandMigration_ReturnsValid()
    {
        var validator = new MigrationValidator(allowDestructive: false);
        var expandMigration = new ExpandMigration();

        var result = validator.ValidateMigration(expandMigration);

        result.IsValid.Should().BeTrue();
        result.ErrorCount.Should().Be(0);
    }
}
