using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop rather than rename: TwilioSid held a provider message id
            // (or the "SM-LOG-ONLY" sentinel), never a user id. EF's default
            // rename-into-UserId would have reinterpreted that text as a Guid.
            migrationBuilder.DropColumn(
                name: "TwilioSid",
                table: "ReminderLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ReminderLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "ReminderLogs",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "log");

            migrationBuilder.AddColumn<string>(
                name: "ProviderRef",
                table: "ReminderLogs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            // ReminderLog rows are the reminder scheduler's idempotency keys.
            // Rows written while Twilio was unconfigured still recorded a To
            // (phone number), so recover the user id from it. Without this
            // backfill every student re-receives today's reminders once.
            // Rows with no matching phone (users who never had one) stay null
            // and are ignored by the scheduler.
            //
            // "To" must stay quoted: SQLite treats TO as a reserved keyword and
            // the unqualified ReminderLogs.To form is a syntax error.
            migrationBuilder.Sql(
                """
                UPDATE "ReminderLogs"
                SET UserId = (
                    SELECT u.Id FROM Users u
                    WHERE u.PhoneE164 IS NOT NULL
                      AND u.PhoneE164 <> ''
                      AND u.PhoneE164 = "ReminderLogs"."To"
                )
                WHERE UserId IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    P256Dh = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_UserId_Date_Template",
                table: "ReminderLogs",
                columns: new[] { "UserId", "Date", "Template" });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_ReminderLogs_UserId_Date_Template",
                table: "ReminderLogs");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "ReminderLogs");

            migrationBuilder.DropColumn(
                name: "ProviderRef",
                table: "ReminderLogs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ReminderLogs");

            migrationBuilder.AddColumn<string>(
                name: "TwilioSid",
                table: "ReminderLogs",
                type: "TEXT",
                nullable: true);
        }
    }
}
