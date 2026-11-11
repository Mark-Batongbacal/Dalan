using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dalan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaxFleetTravelSeconds",
                table: "DispatchPlans",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RotationFirstDriverId",
                table: "DispatchPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RotationVersion",
                table: "DispatchPlans",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ServiceSecondsPerStop",
                table: "DispatchPlans",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Strategy",
                table: "DispatchPlans",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "MinimumTotalTravelTime");

            migrationBuilder.AddColumn<string>(
                name: "WarehouseKey",
                table: "DispatchPlans",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DispatchRotations",
                columns: table => new
                {
                    WarehouseKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastFirstDriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchRotations", x => x.WarehouseKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchRotations");

            migrationBuilder.DropColumn(
                name: "MaxFleetTravelSeconds",
                table: "DispatchPlans");

            migrationBuilder.DropColumn(
                name: "RotationFirstDriverId",
                table: "DispatchPlans");

            migrationBuilder.DropColumn(
                name: "RotationVersion",
                table: "DispatchPlans");

            migrationBuilder.DropColumn(
                name: "ServiceSecondsPerStop",
                table: "DispatchPlans");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "DispatchPlans");

            migrationBuilder.DropColumn(
                name: "WarehouseKey",
                table: "DispatchPlans");
        }
    }
}
