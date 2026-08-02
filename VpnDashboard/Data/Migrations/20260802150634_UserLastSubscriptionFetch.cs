using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VpnDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserLastSubscriptionFetch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSubscriptionFetchAt",
                table: "LocalUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSubscriptionFetchAt",
                table: "LocalUsers");
        }
    }
}
