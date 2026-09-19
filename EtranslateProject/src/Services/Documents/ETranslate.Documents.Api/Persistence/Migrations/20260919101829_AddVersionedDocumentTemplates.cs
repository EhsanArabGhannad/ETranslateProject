using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedDocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_templates",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentRevision = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_template_revisions",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    EditorContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HeaderContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FooterContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PageLayoutJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WatermarkJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_template_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_template_revisions_document_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "documents",
                        principalTable: "document_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_template_revisions_TemplateId_RevisionNumber",
                schema: "documents",
                table: "document_template_revisions",
                columns: new[] { "TemplateId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_templates_TenantId_IsActive_UpdatedAtUtc",
                schema: "documents",
                table: "document_templates",
                columns: new[] { "TenantId", "IsActive", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_document_templates_TenantId_Name",
                schema: "documents",
                table: "document_templates",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_template_revisions",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "document_templates",
                schema: "documents");
        }
    }
}
