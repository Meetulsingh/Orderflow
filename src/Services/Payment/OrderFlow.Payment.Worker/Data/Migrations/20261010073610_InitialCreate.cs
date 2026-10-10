using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Payment.Worker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    card_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                schema: "payment",
                table: "payments",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments",
                schema: "payment");
        }
    }
}
