using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialNetwork.Migrations
{
    /// <inheritdoc />
    public partial class Add_FriendRequestId_To_Notification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Post_PostId",
                table: "Notifications");

            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "Notifications",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "FriendRequestId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Post_PostId",
                table: "Notifications",
                column: "PostId",
                principalTable: "Post",
                principalColumn: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Post_PostId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "FriendRequestId",
                table: "Notifications");

            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Post_PostId",
                table: "Notifications",
                column: "PostId",
                principalTable: "Post",
                principalColumn: "PostId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
