using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dalan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetTrackingAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    From = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ToJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Claim = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingSessions_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackingSessions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrackingSamples",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SampleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IntervalStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: false),
                    ObservedSeconds = table.Column<double>(type: "float", nullable: false),
                    Moving = table.Column<bool>(type: "bit", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingSamples", x => new { x.SessionId, x.SampleId });
                    table.ForeignKey(
                        name: "FK_TrackingSamples_TrackingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TrackingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_AcceptedAtUtc_NeedsReview_NextAttemptAtUtc",
                table: "NotificationDeliveries",
                columns: new[] { "AcceptedAtUtc", "NeedsReview", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingSamples_CapturedAtUtc",
                table: "TrackingSamples",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingSamples_VehicleId_CapturedAtUtc",
                table: "TrackingSamples",
                columns: new[] { "VehicleId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingSessions_DriverId",
                table: "TrackingSessions",
                column: "DriverId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingSessions_VehicleId_StartedAtUtc",
                table: "TrackingSessions",
                columns: new[] { "VehicleId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "TrackingSamples");

            migrationBuilder.DropTable(
                name: "TrackingSessions");
        }
    }
}
