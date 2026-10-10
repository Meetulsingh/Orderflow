using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Notification.Worker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notification");

            migrationBuilder.CreateTable(
                name: "dead_letters",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    exception_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    exception_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    faulted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_logs",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dead_letters_faulted_at_utc",
                schema: "notification",
                table: "dead_letters",
                column: "faulted_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_event_logs_event_id",
                schema: "notification",
                table: "event_logs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_logs_occurred_at_utc",
                schema: "notification",
                table: "event_logs",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_sent_at_utc",
                schema: "notification",
                table: "notifications",
                columns: new[] { "user_id", "sent_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letters",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "event_logs",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notification");
        }
    }
}
