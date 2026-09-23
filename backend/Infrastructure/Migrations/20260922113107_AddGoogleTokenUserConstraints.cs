using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleTokenUserConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GoogleTokens_UserId",
                table: "GoogleTokens",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GoogleTokens_Users_UserId",
                table: "GoogleTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoogleTokens_Users_UserId",
                table: "GoogleTokens");

            migrationBuilder.DropIndex(
                name: "IX_GoogleTokens_UserId",
                table: "GoogleTokens");
        }
    }
}
