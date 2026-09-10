using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BitCode.Framework.Tests.Migrations.TestModel.Migrations;

/// <inheritdoc />
public partial class ExpandMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Descripcion",
            table: "SampleEntities",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Activo",
            table: "SampleEntities",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Activo",
            table: "SampleEntities");

        migrationBuilder.DropColumn(
            name: "Descripcion",
            table: "SampleEntities");
    }
}
