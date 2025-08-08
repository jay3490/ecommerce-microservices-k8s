using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaTimeouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Timeouts",
                table: "order_sagas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_order_sagas_State_UpdatedAt",
                table: "order_sagas",
                columns: new[] { "State", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_sagas_State_UpdatedAt",
                table: "order_sagas");

            migrationBuilder.DropColumn(
                name: "Timeouts",
                table: "order_sagas");
        }
    }
}
