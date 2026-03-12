using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImageRatesPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Vehicles",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "RentalContracts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DamageFee",
                table: "RentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FuelFee",
                table: "RentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFee",
                table: "RentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoPaymentId",
                table: "RentalContracts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayMongoPaymentUrl",
                table: "RentalContracts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SecurityDeposit",
                table: "RentalContracts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CategoryRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRates_Category",
                table: "CategoryRates",
                column: "Category",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryRates");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "DamageFee",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "FuelFee",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "LateFee",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "PayMongoPaymentId",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "PayMongoPaymentUrl",
                table: "RentalContracts");

            migrationBuilder.DropColumn(
                name: "SecurityDeposit",
                table: "RentalContracts");
        }
    }
}
