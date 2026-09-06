using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class AllowCardAssignedOperationAmounts : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialOperations_Requested_Positive",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialOperations_Requested_Positive",
                schema: "dbo",
                table: "FinancialOperations",
                sql: "([RequestedAmount] > 0 AND [Kind] NOT IN (15, 16, 17)) OR ([Kind] IN (15, 16, 17) AND [RequestedAmount] = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialOperations_Requested_Positive",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialOperations_Requested_Positive",
                schema: "dbo",
                table: "FinancialOperations",
                sql: "([RequestedAmount] > 0 AND [Kind] NOT IN (15, 16)) OR ([Kind] IN (15, 16) AND [RequestedAmount] = 0)");
        }
    }
}
