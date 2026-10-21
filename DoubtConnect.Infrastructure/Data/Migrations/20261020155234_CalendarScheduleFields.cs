using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoubtConnect.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CalendarScheduleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityType",
                table: "WeeklyAvailabilityTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "WeeklyAvailabilityTemplates",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivityType",
                table: "WeeklyAvailabilityTemplates");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "WeeklyAvailabilityTemplates");
        }
    }
}
