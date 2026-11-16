using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Marquee.Audit.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "change_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_events_position",
                schema: "audit",
                table: "change_events",
                column: "position",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_events_table_name_committed_at",
                schema: "audit",
                table: "change_events",
                columns: new[] { "table_name", "committed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_events",
                schema: "audit");
        }
    }
}
