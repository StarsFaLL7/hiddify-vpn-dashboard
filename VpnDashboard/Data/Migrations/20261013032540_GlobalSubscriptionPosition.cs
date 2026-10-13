using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VpnDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSubscriptionPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "GlobalSubscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Стартовый порядок — прежний алфавитный, чтобы после обновления ничего не переехало.
            migrationBuilder.Sql(
                """
                UPDATE GlobalSubscriptions
                SET Position = (
                    SELECT COUNT(*)
                    FROM GlobalSubscriptions AS other
                    WHERE other.Label < GlobalSubscriptions.Label
                       OR (other.Label = GlobalSubscriptions.Label AND other.Id < GlobalSubscriptions.Id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "GlobalSubscriptions");
        }
    }
}
