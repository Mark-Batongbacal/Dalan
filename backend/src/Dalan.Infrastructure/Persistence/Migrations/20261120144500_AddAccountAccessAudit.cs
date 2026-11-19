using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dalan.Infrastructure.Persistence.Migrations;

public partial class AddAccountAccessAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccountAccessAudits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                TargetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountAccessAudits", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountAccessAudits_OccurredAtUtc",
            table: "AccountAccessAudits",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AccountAccessAudits_DriverId_OccurredAtUtc",
            table: "AccountAccessAudits",
            columns: new[] { "DriverId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AccountAccessAudits_TargetUserId_OccurredAtUtc",
            table: "AccountAccessAudits",
            columns: new[] { "TargetUserId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "AccountAccessAudits");
}
