using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayMongoPaymentResourceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayMongoPaymentResourceId",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayMongoPaymentResourceId",
                table: "Payments");
        }
    }
}
