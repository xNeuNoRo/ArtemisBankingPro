using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Identity.Migrations;

[DbContext(typeof(IdentityContext))]
[Migration("20260818120000_CommerceEmailUniqueness")]
public partial class CommerceEmailUniqueness : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "EmailIndex",
            schema: "Identity",
            table: "Users");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "Identity",
            table: "Users",
            column: "NormalizedEmail",
            unique: true,
            filter: "[NormalizedEmail] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "EmailIndex",
            schema: "Identity",
            table: "Users");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "Identity",
            table: "Users",
            column: "NormalizedEmail");
    }
}
