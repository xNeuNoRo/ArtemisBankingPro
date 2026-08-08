using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class InitialCreate : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence(
                name: "BankingNumberSequence",
                schema: "dbo",
                minValue: 1L,
                maxValue: 999999999L);

            migrationBuilder.CreateTable(
                name: "CreditCards",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    PanFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CvcDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentDebt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpirationMonth = table.Column<int>(type: "int", nullable: false),
                    ExpirationYear = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_CreditCards", x => x.Id);
                    table.CheckConstraint("CK_CreditCards_Debt_NonNegative", "[CurrentDebt] >= 0");
                    table.CheckConstraint("CK_CreditCards_Debt_Within_Limit", "[CurrentDebt] <= [CreditLimit]");
                    table.CheckConstraint("CK_CreditCards_Fingerprint_Hex", "LEN([PanFingerprint]) = 64 AND [PanFingerprint] NOT LIKE '%[^0-9A-Fa-f]%'");
                    table.CheckConstraint("CK_CreditCards_LastFour_Digits", "[LastFour] LIKE '[0-9][0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_CreditCards_Limit_Positive", "[CreditLimit] > 0");
                });

            migrationBuilder.CreateTable(
                name: "FinancialOperations",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InitiatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RejectionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreditCardId = table.Column<int>(type: "int", nullable: true),
                    LoanNumber = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    MerchantId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table => {
                    table.PrimaryKey("PK_FinancialOperations", x => x.Id);
                    table.CheckConstraint("CK_FinancialOperations_Applied_NonNegative", "[AppliedAmount] >= 0");
                    table.CheckConstraint("CK_FinancialOperations_Interest_NonNegative", "[InterestAmount] >= 0");
                    table.CheckConstraint("CK_FinancialOperations_Requested_Positive", "[RequestedAmount] > 0");
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    ApprovedPrincipal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TermMonths = table.Column<int>(type: "int", nullable: false),
                    AnnualInterestRate = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.CheckConstraint("CK_Loans_Number_NineDigits", "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_Loans_Principal_Positive", "[ApprovedPrincipal] > 0");
                    table.CheckConstraint("CK_Loans_Term_Allowed", "[TermMonths] IN (6, 12, 18, 24, 30, 36, 42, 48, 54, 60)");
                });

            migrationBuilder.CreateTable(
                name: "Merchants",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rnc = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssociatedUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavingsAccounts",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_SavingsAccounts", x => x.Id);
                    table.CheckConstraint("CK_SavingsAccounts_Balance_NonNegative", "[Balance] >= 0");
                    table.CheckConstraint("CK_SavingsAccounts_Number_NineDigits", "LEN([Number]) = 9 AND [Number] NOT LIKE '%[^0-9]%'");
                });

            migrationBuilder.CreateTable(
                name: "AccountTransactions",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BeneficiaryReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table => {
                    table.PrimaryKey("PK_AccountTransactions", x => x.Id);
                    table.CheckConstraint("CK_AccountTransactions_Amount_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_AccountTransactions_FinancialOperations_FinancialOperationId",
                        column: x => x.FinancialOperationId,
                        principalSchema: "dbo",
                        principalTable: "FinancialOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Installments",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsOverdue = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_Installments", x => x.Id);
                    table.CheckConstraint("CK_Installments_Paid_NonNegative", "[PaidAmount] >= 0");
                    table.CheckConstraint("CK_Installments_Paid_Within_Scheduled", "[PaidAmount] <= [ScheduledAmount]");
                    table.CheckConstraint("CK_Installments_Scheduled_NonNegative", "[ScheduledAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_Installments_Loans_LoanId",
                        column: x => x.LoanId,
                        principalSchema: "dbo",
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardConsumptions",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditCardId = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<int>(type: "int", nullable: true),
                    MerchantDisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table => {
                    table.PrimaryKey("PK_CardConsumptions", x => x.Id);
                    table.CheckConstraint("CK_CardConsumptions_Amount_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CardConsumptions_CreditCards_CreditCardId",
                        column: x => x.CreditCardId,
                        principalSchema: "dbo",
                        principalTable: "CreditCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardConsumptions_FinancialOperations_FinancialOperationId",
                        column: x => x.FinancialOperationId,
                        principalSchema: "dbo",
                        principalTable: "FinancialOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardConsumptions_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalSchema: "dbo",
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Beneficiaries",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DestinationAccountId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_Beneficiaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Beneficiaries_SavingsAccounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalSchema: "dbo",
                        principalTable: "SavingsAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransactions_AccountNumber",
                schema: "dbo",
                table: "AccountTransactions",
                column: "AccountNumber");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransactions_FinancialOperationId",
                schema: "dbo",
                table: "AccountTransactions",
                column: "FinancialOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_Beneficiaries_DestinationAccountId",
                schema: "dbo",
                table: "Beneficiaries",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Beneficiaries_OwnerUserId",
                schema: "dbo",
                table: "Beneficiaries",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Beneficiaries_OwnerUserId_DestinationAccountId",
                schema: "dbo",
                table: "Beneficiaries",
                columns: new[] { "OwnerUserId", "DestinationAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardConsumptions_CreditCardId",
                schema: "dbo",
                table: "CardConsumptions",
                column: "CreditCardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardConsumptions_FinancialOperationId",
                schema: "dbo",
                table: "CardConsumptions",
                column: "FinancialOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardConsumptions_MerchantId",
                schema: "dbo",
                table: "CardConsumptions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditCards_CustomerUserId",
                schema: "dbo",
                table: "CreditCards",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditCards_PanFingerprint",
                schema: "dbo",
                table: "CreditCards",
                column: "PanFingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_InitiatedByUserId",
                schema: "dbo",
                table: "FinancialOperations",
                column: "InitiatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_Kind_Status",
                schema: "dbo",
                table: "FinancialOperations",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialOperations_OccurredAt",
                schema: "dbo",
                table: "FinancialOperations",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_IdempotencyKey_ActorId",
                schema: "dbo",
                table: "IdempotencyRecords",
                columns: new[] { "IdempotencyKey", "ActorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_OperationType",
                schema: "dbo",
                table: "IdempotencyRecords",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_LoanId_DueDate",
                schema: "dbo",
                table: "Installments",
                columns: new[] { "LoanId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Installments_LoanId_Number",
                schema: "dbo",
                table: "Installments",
                columns: new[] { "LoanId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_CustomerUserId",
                schema: "dbo",
                table: "Loans",
                column: "CustomerUserId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_Number",
                schema: "dbo",
                table: "Loans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_AssociatedUserId",
                schema: "dbo",
                table: "Merchants",
                column: "AssociatedUserId",
                unique: true,
                filter: "[AssociatedUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_Email",
                schema: "dbo",
                table: "Merchants",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_Rnc",
                schema: "dbo",
                table: "Merchants",
                column: "Rnc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsAccounts_Number",
                schema: "dbo",
                table: "SavingsAccounts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsAccounts_OwnerUserId",
                schema: "dbo",
                table: "SavingsAccounts",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsAccounts_OwnerUserId_Type",
                schema: "dbo",
                table: "SavingsAccounts",
                columns: new[] { "OwnerUserId", "Type" },
                unique: true,
                filter: "[Type] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "AccountTransactions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Beneficiaries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CardConsumptions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Installments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SavingsAccounts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditCards",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FinancialOperations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Merchants",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Loans",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "BankingNumberSequence",
                schema: "dbo");
        }
    }
}
