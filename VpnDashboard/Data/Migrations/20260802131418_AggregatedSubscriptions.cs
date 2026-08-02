using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VpnDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AggregatedSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Prefix",
                table: "ManualSubscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpstreamUserAgent",
                table: "ManualSubscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prefix",
                table: "HiddifyServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpstreamUserAgent",
                table: "HiddifyServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prefix",
                table: "GlobalSubscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpstreamUserAgent",
                table: "GlobalSubscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ContentGzip = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ConfigCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    LastFormat = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshots_SourceKind_SourceId",
                table: "SubscriptionSnapshots",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionSnapshots");

            migrationBuilder.DropColumn(
                name: "Prefix",
                table: "ManualSubscriptions");

            migrationBuilder.DropColumn(
                name: "UpstreamUserAgent",
                table: "ManualSubscriptions");

            migrationBuilder.DropColumn(
                name: "Prefix",
                table: "HiddifyServers");

            migrationBuilder.DropColumn(
                name: "UpstreamUserAgent",
                table: "HiddifyServers");

            migrationBuilder.DropColumn(
                name: "Prefix",
                table: "GlobalSubscriptions");

            migrationBuilder.DropColumn(
                name: "UpstreamUserAgent",
                table: "GlobalSubscriptions");
        }
    }
}
