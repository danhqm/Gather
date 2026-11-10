using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPlatform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Admission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AdmittedAt",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdmittedBy",
                table: "Tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AdmittedBy",
                table: "Tickets",
                column: "AdmittedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_AdmittedBy",
                table: "Tickets",
                column: "AdmittedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_AdmittedBy",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AdmittedBy",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "AdmittedAt",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "AdmittedBy",
                table: "Tickets");
        }
    }
}
