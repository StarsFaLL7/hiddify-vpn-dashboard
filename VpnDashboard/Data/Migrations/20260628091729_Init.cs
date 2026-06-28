using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VpnDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HiddifyServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false),
                    AdminProxyPath = table.Column<string>(type: "TEXT", nullable: false),
                    ClientProxyPath = table.Column<string>(type: "TEXT", nullable: false),
                    AdminUuid = table.Column<string>(type: "TEXT", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastPingAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiddifyServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ShowcaseToken = table.Column<string>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualSubscriptions_LocalUsers_LocalUserId",
                        column: x => x.LocalUserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGlobalSubscriptions",
                columns: table => new
                {
                    LocalUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlobalSubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGlobalSubscriptions", x => new { x.LocalUserId, x.GlobalSubscriptionId });
                    table.ForeignKey(
                        name: "FK_UserGlobalSubscriptions_GlobalSubscriptions_GlobalSubscriptionId",
                        column: x => x.GlobalSubscriptionId,
                        principalTable: "GlobalSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGlobalSubscriptions_LocalUsers_LocalUserId",
                        column: x => x.LocalUserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserServerBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HiddifyServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HiddifyUuid = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBroken = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserServerBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserServerBindings_HiddifyServers_HiddifyServerId",
                        column: x => x.HiddifyServerId,
                        principalTable: "HiddifyServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserServerBindings_LocalUsers_LocalUserId",
                        column: x => x.LocalUserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalUsers_ShowcaseToken",
                table: "LocalUsers",
                column: "ShowcaseToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualSubscriptions_LocalUserId",
                table: "ManualSubscriptions",
                column: "LocalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGlobalSubscriptions_GlobalSubscriptionId",
                table: "UserGlobalSubscriptions",
                column: "GlobalSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserServerBindings_HiddifyServerId",
                table: "UserServerBindings",
                column: "HiddifyServerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserServerBindings_LocalUserId_HiddifyServerId",
                table: "UserServerBindings",
                columns: new[] { "LocalUserId", "HiddifyServerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualSubscriptions");

            migrationBuilder.DropTable(
                name: "UserGlobalSubscriptions");

            migrationBuilder.DropTable(
                name: "UserServerBindings");

            migrationBuilder.DropTable(
                name: "GlobalSubscriptions");

            migrationBuilder.DropTable(
                name: "HiddifyServers");

            migrationBuilder.DropTable(
                name: "LocalUsers");
        }
    }
}
