using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Industrial.Persistence.Migrations.Target
{
    /// <inheritdoc />
    public partial class InitialTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    RecordId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    RecordTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.RecordId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Records_RecordTime",
                table: "Records",
                column: "RecordTime");

            migrationBuilder.CreateIndex(
                name: "IX_Records_SourceIdentityKey",
                table: "Records",
                column: "SourceIdentityKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Records");
        }
    }
}
