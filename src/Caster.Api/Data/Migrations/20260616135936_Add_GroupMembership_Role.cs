using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_GroupMembership_Role : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "role",
                table: "group_memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                table: "group_memberships");
        }
    }
}
