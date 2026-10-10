using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OrderFlow.Ordering.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ordering");

            migrationBuilder.CreateTable(
                name: "inbox_state",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    receive_count = table.Column<int>(type: "integer", nullable: false),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_state", x => x.id);
                    table.UniqueConstraint("ak_inbox_state_message_id_consumer_id", x => new { x.message_id, x.consumer_id });
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_scenario = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    card_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    shipping_address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_address_country = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_address_full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    shipping_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shipping_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shipping_address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_state",
                schema: "ordering",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_state", x => x.outbox_id);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_status_history_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "ordering",
                columns: table => new
                {
                    sequence_number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enqueue_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    headers = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbox_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    destination_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    response_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fault_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.sequence_number);
                    table.ForeignKey(
                        name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_",
                        columns: x => new { x.inbox_message_id, x.inbox_consumer_id },
                        principalSchema: "ordering",
                        principalTable: "inbox_state",
                        principalColumns: new[] { "message_id", "consumer_id" });
                    table.ForeignKey(
                        name: "fk_outbox_message_outbox_state_outbox_id",
                        column: x => x.outbox_id,
                        principalSchema: "ordering",
                        principalTable: "outbox_state",
                        principalColumn: "outbox_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_state_delivered",
                schema: "ordering",
                table: "inbox_state",
                column: "delivered");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                schema: "ordering",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_history_order_id",
                schema: "ordering",
                table: "order_status_history",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_number",
                schema: "ordering",
                table: "orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_status",
                schema: "ordering",
                table: "orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_created_at_utc",
                schema: "ordering",
                table: "orders",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_idempotency_key",
                schema: "ordering",
                table: "orders",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_enqueue_time",
                schema: "ordering",
                table: "outbox_message",
                column: "enqueue_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_expiration_time",
                schema: "ordering",
                table: "outbox_message",
                column: "expiration_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_",
                schema: "ordering",
                table: "outbox_message",
                columns: new[] { "inbox_message_id", "inbox_consumer_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_outbox_id_sequence_number",
                schema: "ordering",
                table: "outbox_message",
                columns: new[] { "outbox_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_state_created",
                schema: "ordering",
                table: "outbox_state",
                column: "created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_status_history",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "inbox_state",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "outbox_state",
                schema: "ordering");
        }
    }
}
