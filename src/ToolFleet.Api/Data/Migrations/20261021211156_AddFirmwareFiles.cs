using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolFleet.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmwareFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "FirmwareReleases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileSha256",
                table: "FirmwareReleases",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "FirmwareReleases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileStorageName",
                table: "FirmwareReleases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileName",
                table: "FirmwareReleases");

            migrationBuilder.DropColumn(
                name: "FileSha256",
                table: "FirmwareReleases");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "FirmwareReleases");

            migrationBuilder.DropColumn(
                name: "FileStorageName",
                table: "FirmwareReleases");
        }
    }
}
