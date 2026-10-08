using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.TranslationWorkflow.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedTranslatorUserId",
                schema: "workflow",
                table: "translation_jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignmentChangedAtUtc",
                schema: "workflow",
                table: "translation_jobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignmentChangedByUserId",
                schema: "workflow",
                table: "translation_jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssignmentVersion",
                schema: "workflow",
                table: "translation_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "translation_job_assignment_changes",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TranslationJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousTranslatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TranslatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_job_assignment_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_translation_job_assignment_changes_translation_jobs_TranslationJobId",
                        column: x => x.TranslationJobId,
                        principalSchema: "workflow",
                        principalTable: "translation_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_translation_jobs_TenantId_AssignedTranslatorUserId_CreatedAtUtc",
                schema: "workflow",
                table: "translation_jobs",
                columns: new[] { "TenantId", "AssignedTranslatorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_translation_job_assignment_changes_TranslationJobId_Version",
                schema: "workflow",
                table: "translation_job_assignment_changes",
                columns: new[] { "TranslationJobId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_job_assignment_changes",
                schema: "workflow");

            migrationBuilder.DropIndex(
                name: "IX_translation_jobs_TenantId_AssignedTranslatorUserId_CreatedAtUtc",
                schema: "workflow",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "AssignedTranslatorUserId",
                schema: "workflow",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "AssignmentChangedAtUtc",
                schema: "workflow",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "AssignmentChangedByUserId",
                schema: "workflow",
                table: "translation_jobs");

            migrationBuilder.DropColumn(
                name: "AssignmentVersion",
                schema: "workflow",
                table: "translation_jobs");
        }
    }
}
