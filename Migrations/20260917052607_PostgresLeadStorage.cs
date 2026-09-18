using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Up2Ai.Migrations
{
    /// <inheritdoc />
    public partial class PostgresLeadStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reach = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    business = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    need = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "new"),
                    last_contacted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    internal_notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leads_created_at",
                table: "leads",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_leads_status",
                table: "leads",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leads");
        }
    }
}
