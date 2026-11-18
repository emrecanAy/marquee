using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marquee.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                schema: "booking",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_account_id",
                schema: "booking",
                table: "reservations",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_account_id",
                schema: "booking",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "account_id",
                schema: "booking",
                table: "reservations");
        }
    }
}
