using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Throne.Infrastructure.EfCore;

#nullable disable

namespace Throne.Infrastructure.Migrations;

[DbContext(typeof(ThroneDbContext))]
[Migration("20260924000000_AddTerminalLastLaunchPreference")]
public partial class AddTerminalLastLaunchPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "last_model",
            table: "terminal_settings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_vendor",
            table: "terminal_settings",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "last_model", table: "terminal_settings");
        migrationBuilder.DropColumn(name: "last_vendor", table: "terminal_settings");
    }
}
