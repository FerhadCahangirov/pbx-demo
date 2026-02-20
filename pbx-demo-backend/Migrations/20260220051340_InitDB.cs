using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace pbx_demo_backend.Migrations
{
    /// <inheritdoc />
    public partial class InitDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThreeCxGroupId = table.Column<int>(type: "int", nullable: false),
                    ThreeCxGroupNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PromptSet = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DisableCustomPrompt = table.Column<bool>(type: "bit", nullable: false),
                    PropsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    RoutingJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    LiveChatLink = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LiveChatWebsite = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ThreeCxWebsiteLinkId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnedExtension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ControlDn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PromptSet = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    VmEmailOptions = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SendEmailMissedCalls = table.Column<bool>(type: "bit", nullable: false),
                    Require2Fa = table.Column<bool>(type: "bit", nullable: false),
                    CallUsEnableChat = table.Column<bool>(type: "bit", nullable: false),
                    ClickToCallId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WebMeetingFriendlyName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipUsername = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipAuthId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipPassword = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SipDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ThreeCxUserId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    AppDepartmentId = table.Column<int>(type: "int", nullable: false),
                    ThreeCxRoleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Departments_AppDepartmentId",
                        column: x => x.AppDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_AppDepartmentId",
                table: "DepartmentMemberships",
                column: "AppDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_AppUserId_AppDepartmentId",
                table: "DepartmentMemberships",
                columns: new[] { "AppUserId", "AppDepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ThreeCxGroupId",
                table: "Departments",
                column: "ThreeCxGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailAddress",
                table: "Users",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OwnedExtension",
                table: "Users",
                column: "OwnedExtension");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ThreeCxUserId",
                table: "Users",
                column: "ThreeCxUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepartmentMemberships");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
