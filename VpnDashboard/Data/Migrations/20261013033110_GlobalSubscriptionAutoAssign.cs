using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VpnDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSubscriptionAutoAssign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AssignToNewUsers",
                table: "GlobalSubscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignToNewUsers",
                table: "GlobalSubscriptions");
        }
    }
}
