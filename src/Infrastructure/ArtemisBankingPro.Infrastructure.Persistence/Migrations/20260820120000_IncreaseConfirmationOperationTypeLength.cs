using Microsoft.EntityFrameworkCore.Migrations;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BankingDbContext))]
[Migration("20260820120000_IncreaseConfirmationOperationTypeLength")]
public partial class IncreaseConfirmationOperationTypeLength : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AlterColumn<string>(
            name: "OperationType",
            schema: "dbo",
            table: "ConfirmationTokens",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.AlterColumn<string>(
            name: "OperationType",
            schema: "dbo",
            table: "ConfirmationTokens",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128
        );
    }
}
