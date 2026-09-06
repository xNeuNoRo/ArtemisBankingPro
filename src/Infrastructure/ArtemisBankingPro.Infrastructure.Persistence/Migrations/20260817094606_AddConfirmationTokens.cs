using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtemisBankingPro.Infrastructure.Persistence.Migrations {
    /// <inheritdoc />
    public partial class AddConfirmationTokens : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "ConfirmationTokens",
                schema: "dbo",
                columns: table => new {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_ConfirmationTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationTokens_ActorId_OperationType",
                schema: "dbo",
                table: "ConfirmationTokens",
                columns: new[] { "ActorId", "OperationType" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmationTokens_TokenHash",
                schema: "dbo",
                table: "ConfirmationTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "ConfirmationTokens",
                schema: "dbo");
        }
    }
}
