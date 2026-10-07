using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaContentType",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaFileName",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaPath",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MediaSize",
                table: "Messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhotoMode",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ViewedAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaContentType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaFileName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaPath",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaSize",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "PhotoMode",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ViewedAt",
                table: "Messages");
        }
    }
}
