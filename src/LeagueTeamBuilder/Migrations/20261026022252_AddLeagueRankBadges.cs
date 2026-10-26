using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeagueTeamBuilder.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueRankBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeagueRankBadges",
                columns: table => new
                {
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueRankBadges", x => x.Tier);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueRankBadges");
        }
    }
}
