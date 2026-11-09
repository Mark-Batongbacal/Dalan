using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dalan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireDispatchParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DispatchStops_RouteId_Position",
                table: "DispatchStops");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRoutes_PlanId_VehicleId",
                table: "DispatchRoutes");

            migrationBuilder.AlterColumn<Guid>(
                name: "RouteId",
                table: "DispatchStops",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PlanId",
                table: "DispatchRoutes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_RouteId_Position",
                table: "DispatchStops",
                columns: new[] { "RouteId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutes_PlanId_VehicleId",
                table: "DispatchRoutes",
                columns: new[] { "PlanId", "VehicleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DispatchStops_RouteId_Position",
                table: "DispatchStops");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRoutes_PlanId_VehicleId",
                table: "DispatchRoutes");

            migrationBuilder.AlterColumn<Guid>(
                name: "RouteId",
                table: "DispatchStops",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "PlanId",
                table: "DispatchRoutes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_RouteId_Position",
                table: "DispatchStops",
                columns: new[] { "RouteId", "Position" },
                unique: true,
                filter: "[RouteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutes_PlanId_VehicleId",
                table: "DispatchRoutes",
                columns: new[] { "PlanId", "VehicleId" },
                unique: true,
                filter: "[PlanId] IS NOT NULL");
        }
    }
}
