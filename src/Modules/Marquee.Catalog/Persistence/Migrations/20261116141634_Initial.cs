using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marquee.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "halls",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    seats_per_row = table.Column<int>(type: "integer", nullable: false),
                    premium_row_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_halls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movies",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tagline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    synopsis = table.Column<string>(type: "text", nullable: false),
                    genre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    director = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    age_rating = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    poster_hue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "showtimes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_showtimes", x => x.id);
                    table.ForeignKey(
                        name: "fk_showtimes_halls_hall_id",
                        column: x => x.hall_id,
                        principalSchema: "catalog",
                        principalTable: "halls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_showtimes_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "catalog",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_showtimes_hall_id_starts_at",
                schema: "catalog",
                table: "showtimes",
                columns: new[] { "hall_id", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_showtimes_movie_id_starts_at",
                schema: "catalog",
                table: "showtimes",
                columns: new[] { "movie_id", "starts_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "showtimes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "halls",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "movies",
                schema: "catalog");
        }
    }
}
