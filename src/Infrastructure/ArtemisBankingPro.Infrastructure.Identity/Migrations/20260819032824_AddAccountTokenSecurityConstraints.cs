using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Identity.Migrations {
    /// <inheritdoc />
    public partial class AddAccountTokenSecurityConstraints : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropIndex(
                name: "IX_AccountTokens_UserId_Type",
                schema: "Identity",
                table: "AccountTokens");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTokens_TokenHash",
                schema: "Identity",
                table: "AccountTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTokens_UserId_Type",
                schema: "Identity",
                table: "AccountTokens",
                columns: new[] { "UserId", "Type" },
                unique: true,
                filter: "[UsedAtUtc] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountTokens_Type",
                schema: "Identity",
                table: "AccountTokens",
                sql: "[Type] IN (1, 2)");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountTokens_Users_UserId",
                schema: "Identity",
                table: "AccountTokens",
                column: "UserId",
                principalSchema: "Identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountTokens_Users_UserId",
                schema: "Identity",
                table: "AccountTokens");

            migrationBuilder.DropIndex(
                name: "IX_AccountTokens_TokenHash",
                schema: "Identity",
                table: "AccountTokens");

            migrationBuilder.DropIndex(
                name: "IX_AccountTokens_UserId_Type",
                schema: "Identity",
                table: "AccountTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountTokens_Type",
                schema: "Identity",
                table: "AccountTokens");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTokens_UserId_Type",
                schema: "Identity",
                table: "AccountTokens",
                columns: new[] { "UserId", "Type" });
        }
    }
}
