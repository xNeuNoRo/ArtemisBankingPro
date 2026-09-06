using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class AllowCardCancelledOperationAmounts : Migration {
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
                sql: "([RequestedAmount] > 0 AND [Kind] <> 15) OR ([Kind] = 15 AND [RequestedAmount] = 0)");
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
                sql: "[RequestedAmount] > 0");
        }
    }
}
