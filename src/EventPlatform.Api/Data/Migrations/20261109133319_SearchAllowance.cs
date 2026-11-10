using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPlatform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SearchAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CostMicroUsd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchAttempts_CreatedAt",
                table: "SearchAttempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchAttempts_UserId_CreatedAt",
                table: "SearchAttempts",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchAttempts");
        }
    }
}
