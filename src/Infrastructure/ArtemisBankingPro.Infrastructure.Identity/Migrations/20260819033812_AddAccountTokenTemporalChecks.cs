using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Identity.Migrations {
    /// <inheritdoc />
    public partial class AddAccountTokenTemporalChecks : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountTokens_Dates",
                schema: "Identity",
                table: "AccountTokens",
                sql: "[ExpiresAtUtc] > [CreatedAtUtc] AND ([UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountTokens_Dates",
                schema: "Identity",
                table: "AccountTokens");
        }
    }
}
