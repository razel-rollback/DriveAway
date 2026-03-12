using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMechanicAssignmentToMaintenanceSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "MaintenanceSchedules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedMechanicEmail",
                table: "MaintenanceSchedules",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedMechanicId",
                table: "MaintenanceSchedules",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "MaintenanceSchedules");

            migrationBuilder.DropColumn(
                name: "AssignedMechanicEmail",
                table: "MaintenanceSchedules");

            migrationBuilder.DropColumn(
                name: "AssignedMechanicId",
                table: "MaintenanceSchedules");
        }
    }
}
