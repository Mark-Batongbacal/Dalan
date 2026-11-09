using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dalan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DispatchRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartLatitude = table.Column<double>(type: "float", nullable: false),
                    StartLongitude = table.Column<double>(type: "float", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchRoutes_DispatchPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "DispatchPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DispatchRoutes_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DispatchRoutes_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DispatchStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    RouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "Deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DispatchStops_DispatchRoutes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "DispatchRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutes_DriverId",
                table: "DispatchRoutes",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutes_PlanId_VehicleId",
                table: "DispatchRoutes",
                columns: new[] { "PlanId", "VehicleId" },
                unique: true,
                filter: "[PlanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutes_VehicleId",
                table: "DispatchRoutes",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_DeliveryId_Kind",
                table: "DispatchStops",
                columns: new[] { "DeliveryId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_RouteId_Position",
                table: "DispatchStops",
                columns: new[] { "RouteId", "Position" },
                unique: true,
                filter: "[RouteId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchStops");

            migrationBuilder.DropTable(
                name: "DispatchRoutes");

            migrationBuilder.DropTable(
                name: "DispatchPlans");
        }
    }
}
