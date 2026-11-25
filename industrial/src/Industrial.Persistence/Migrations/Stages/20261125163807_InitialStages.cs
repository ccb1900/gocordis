using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Industrial.Persistence.Migrations.Stages
{
    /// <inheritdoc />
    public partial class InitialStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageOutputs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageOutputs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageOutputs_InstanceId_NodeId",
                table: "StageOutputs",
                columns: new[] { "InstanceId", "NodeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageOutputs");
        }
    }
}
