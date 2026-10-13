using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Ordering.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponsAndDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "ordering",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                schema: "ordering",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal",
                schema: "ordering",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Orders placed before discounts existed: subtotal equals the total.
            migrationBuilder.Sql("UPDATE ordering.orders SET subtotal = total;");

            migrationBuilder.CreateTable(
                name: "coupons",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    campaign_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    percent_off = table.Column<int>(type: "integer", nullable: false),
                    product_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coupons_campaign_key",
                schema: "ordering",
                table: "coupons",
                column: "campaign_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupons_code",
                schema: "ordering",
                table: "coupons",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupons_expires_at_utc",
                schema: "ordering",
                table: "coupons",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupons",
                schema: "ordering");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discount",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "subtotal",
                schema: "ordering",
                table: "orders");
        }
    }
}
