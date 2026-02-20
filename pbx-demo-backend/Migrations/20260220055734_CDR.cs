using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace pbx_demo_backend.Migrations
{
    /// <inheritdoc />
    public partial class CDR : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallCdrs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OperatorUserId = table.Column<int>(type: "int", nullable: false),
                    OperatorUsername = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperatorExtension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TrackingKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CallScopeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: true),
                    PbxCallId = table.Column<long>(type: "bigint", nullable: true),
                    PbxLegId = table.Column<long>(type: "bigint", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RemoteParty = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RemoteName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AnsweredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCdrs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallCdrs_Users_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CallCdrStatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CallCdrId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCdrStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallCdrStatusHistory_CallCdrs_CallCdrId",
                        column: x => x.CallCdrId,
                        principalTable: "CallCdrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_IsActive",
                table: "CallCdrs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_OperatorUserId_PbxCallId",
                table: "CallCdrs",
                columns: new[] { "OperatorUserId", "PbxCallId" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_OperatorUserId_StartedAtUtc",
                table: "CallCdrs",
                columns: new[] { "OperatorUserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_Source_TrackingKey",
                table: "CallCdrs",
                columns: new[] { "Source", "TrackingKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrStatusHistory_CallCdrId_OccurredAtUtc",
                table: "CallCdrStatusHistory",
                columns: new[] { "CallCdrId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallCdrStatusHistory");

            migrationBuilder.DropTable(
                name: "CallCdrs");
        }
    }
}
