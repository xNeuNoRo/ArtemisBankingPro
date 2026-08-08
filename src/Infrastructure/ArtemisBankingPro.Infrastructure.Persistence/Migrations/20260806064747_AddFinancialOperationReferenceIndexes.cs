using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class AddFinancialOperationReferenceIndexes : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_CreditCardId",
                schema: "dbo",
                table: "FinancialOperations",
                column: "CreditCardId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_LoanNumber",
                schema: "dbo",
                table: "FinancialOperations",
                column: "LoanNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_MerchantId",
                schema: "dbo",
                table: "FinancialOperations",
                column: "MerchantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropIndex(
                name: "IX_FinancialOperations_CreditCardId",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.DropIndex(
                name: "IX_FinancialOperations_LoanNumber",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.DropIndex(
                name: "IX_FinancialOperations_MerchantId",
                schema: "dbo",
                table: "FinancialOperations");
        }
    }
}
