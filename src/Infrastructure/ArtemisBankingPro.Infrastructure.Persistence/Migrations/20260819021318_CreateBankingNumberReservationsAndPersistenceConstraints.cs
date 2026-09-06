using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class CreateBankingNumberReservationsAndPersistenceConstraints : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "BankingNumberReservations",
                schema: "dbo",
                columns: table => new {
                    Number = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false, defaultValueSql: "RIGHT(REPLICATE('0', 9) + CONVERT(varchar(9), NEXT VALUE FOR dbo.BankingNumberSequence), 9)"),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table => {
                    table.PrimaryKey("PK_BankingNumberReservations", x => x.Number);
                    table.CheckConstraint("CK_BankingNumberReservations_Number_NineDigits", "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_BankingNumberReservations_ResourceType_Valid", "[ResourceType] IN (1, 2)");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavingsAccounts_Status_Valid",
                schema: "dbo",
                table: "SavingsAccounts",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SavingsAccounts_Type_Valid",
                schema: "dbo",
                table: "SavingsAccounts",
                sql: "[Type] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Merchants_Status_Valid",
                schema: "dbo",
                table: "Merchants",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Loans_AnnualInterestRate_NonNegative",
                schema: "dbo",
                table: "Loans",
                sql: "[AnnualInterestRate] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Loans_Status_Valid",
                schema: "dbo",
                table: "Loans",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Installments_Interest_NonNegative",
                schema: "dbo",
                table: "Installments",
                sql: "[InterestAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Installments_Number_Positive",
                schema: "dbo",
                table: "Installments",
                sql: "[Number] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Installments_Principal_NonNegative",
                schema: "dbo",
                table: "Installments",
                sql: "[PrincipalAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Installments_Scheduled_Equals_Breakdown",
                schema: "dbo",
                table: "Installments",
                sql: "[ScheduledAmount] = [InterestAmount] + [PrincipalAmount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IdempotencyRecords_Status_Valid",
                schema: "dbo",
                table: "IdempotencyRecords",
                sql: "[Status] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialOperations_Kind_Valid",
                schema: "dbo",
                table: "FinancialOperations",
                sql: "[Kind] BETWEEN 1 AND 18");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialOperations_Status_Valid",
                schema: "dbo",
                table: "FinancialOperations",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditCards_Status_Valid",
                schema: "dbo",
                table: "CreditCards",
                sql: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CardConsumptions_Type_Valid",
                schema: "dbo",
                table: "CardConsumptions",
                sql: "[Type] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountTransactions_Direction_Valid",
                schema: "dbo",
                table: "AccountTransactions",
                sql: "[Direction] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "BankingNumberReservations",
                schema: "dbo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SavingsAccounts_Status_Valid",
                schema: "dbo",
                table: "SavingsAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SavingsAccounts_Type_Valid",
                schema: "dbo",
                table: "SavingsAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Merchants_Status_Valid",
                schema: "dbo",
                table: "Merchants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Loans_AnnualInterestRate_NonNegative",
                schema: "dbo",
                table: "Loans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Loans_Status_Valid",
                schema: "dbo",
                table: "Loans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Installments_Interest_NonNegative",
                schema: "dbo",
                table: "Installments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Installments_Number_Positive",
                schema: "dbo",
                table: "Installments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Installments_Principal_NonNegative",
                schema: "dbo",
                table: "Installments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Installments_Scheduled_Equals_Breakdown",
                schema: "dbo",
                table: "Installments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IdempotencyRecords_Status_Valid",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialOperations_Kind_Valid",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialOperations_Status_Valid",
                schema: "dbo",
                table: "FinancialOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditCards_Status_Valid",
                schema: "dbo",
                table: "CreditCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CardConsumptions_Type_Valid",
                schema: "dbo",
                table: "CardConsumptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountTransactions_Direction_Valid",
                schema: "dbo",
                table: "AccountTransactions");
        }
    }
}
