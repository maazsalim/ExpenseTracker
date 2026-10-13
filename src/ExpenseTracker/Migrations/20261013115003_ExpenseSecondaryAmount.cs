using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseSecondaryAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountInSecondary",
                table: "Expenses",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SecondaryFxRate",
                table: "Expenses",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            // AED expenses: the secondary amount is the original amount.
            // CAD expenses: CAD→AED = 3.6725 / (CAD per USD), using the closest cached rate on or
            // before the expense date. (There are no CAD expenses yet when this was written.)
            migrationBuilder.Sql("""
                UPDATE "Expenses" SET "SecondaryFxRate" = 1, "AmountInSecondary" = "Amount"
                WHERE "Currency" = 'AED';

                UPDATE "Expenses" e
                SET "SecondaryFxRate" = x.rate, "AmountInSecondary" = round(e."Amount" * x.rate, 2)
                FROM "Expenses" e2
                CROSS JOIN LATERAL (
                    SELECT round(3.6725 / r."Rate", 8) AS rate
                    FROM "ExchangeRates" r
                    WHERE r."Base" = 'USD' AND r."Quote" = 'CAD' AND r."Date" <= e2."Date"
                    ORDER BY r."Date" DESC
                    LIMIT 1
                ) x
                WHERE e2."Id" = e."Id" AND e."Currency" = 'CAD';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountInSecondary",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "SecondaryFxRate",
                table: "Expenses");
        }
    }
}
