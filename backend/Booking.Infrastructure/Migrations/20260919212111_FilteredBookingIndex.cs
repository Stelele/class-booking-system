using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FilteredBookingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_SlotId_StudentId_Status",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_SlotId_StudentId_Status",
                table: "Bookings",
                columns: new[] { "SlotId", "StudentId", "Status" },
                unique: true,
                filter: "Status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_SlotId_StudentId_Status",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_SlotId_StudentId_Status",
                table: "Bookings",
                columns: new[] { "SlotId", "StudentId", "Status" },
                unique: true);
        }
    }
}
