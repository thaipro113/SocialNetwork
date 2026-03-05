using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialNetwork.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalPostId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginalPostId",
                table: "Post",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Post_OriginalPostId",
                table: "Post",
                column: "OriginalPostId");

            migrationBuilder.AddForeignKey(
                name: "FK_Post_Post_OriginalPostId",
                table: "Post",
                column: "OriginalPostId",
                principalTable: "Post",
                principalColumn: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Post_Post_OriginalPostId",
                table: "Post");

            migrationBuilder.DropIndex(
                name: "IX_Post_OriginalPostId",
                table: "Post");

            migrationBuilder.DropColumn(
                name: "OriginalPostId",
                table: "Post");
        }
    }
}
