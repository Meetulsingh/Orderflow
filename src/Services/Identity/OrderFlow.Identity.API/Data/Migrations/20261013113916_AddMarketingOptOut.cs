using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Identity.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingOptOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "marketing_opt_out",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "marketing_opt_out",
                schema: "identity",
                table: "users");
        }
    }
}
