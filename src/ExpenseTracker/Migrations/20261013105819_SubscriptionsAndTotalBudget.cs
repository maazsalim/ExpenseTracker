using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionsAndTotalBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TotalMonthlyBudget = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "TotalMonthlyBudget" },
                values: new object[] { 1, null });

            // Seeded categories use fixed IDs, so make sure the ID counter is past them
            // before adding one without an ID.
            migrationBuilder.Sql("""
                SELECT setval(pg_get_serial_sequence('"Categories"', 'Id'),
                              GREATEST((SELECT MAX("Id") FROM "Categories"), 1));

                INSERT INTO "Categories" ("Name", "Color")
                SELECT 'Subscriptions', 'violet'
                WHERE NOT EXISTS (SELECT 1 FROM "Categories" WHERE lower("Name") = 'subscriptions');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "Categories" c
                WHERE c."Name" = 'Subscriptions'
                  AND NOT EXISTS (SELECT 1 FROM "Expenses" e WHERE e."CategoryId" = c."Id");
                """);

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
