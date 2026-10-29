using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPlatform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Events",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Events",
                type: "uuid",
                nullable: true);

            // The previous milestone's events all belong to the single demo organisation.
            migrationBuilder.Sql("""
                INSERT INTO "Organisations" ("Id", "Name")
                VALUES ('00000000-0000-0000-0000-000000000101', 'Gather Studio')
                ON CONFLICT ("Id") DO NOTHING;
                UPDATE "Events" SET "OrganisationId" = '00000000-0000-0000-0000-000000000101'
                WHERE "OrganisationId" IS NULL;
                """);
            migrationBuilder.AlterColumn<Guid>(name: "OrganisationId", table: "Events", type: "uuid",
                nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_OrganisationId",
                table: "Events",
                column: "OrganisationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Organisations_OrganisationId",
                table: "Events",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Organisations_OrganisationId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_OrganisationId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Events");
        }
    }
}
