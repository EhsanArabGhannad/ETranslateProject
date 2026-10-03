using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAssetsAndDocumentPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateRevisionId",
                schema: "documents",
                table: "translation_documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "template_assets",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_assets_document_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "documents",
                        principalTable: "document_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_translation_documents_TemplateRevisionId",
                schema: "documents",
                table: "translation_documents",
                column: "TemplateRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_template_assets_StorageKey",
                schema: "documents",
                table: "template_assets",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_assets_TemplateId",
                schema: "documents",
                table: "template_assets",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_translation_documents_document_template_revisions_TemplateRevisionId",
                schema: "documents",
                table: "translation_documents",
                column: "TemplateRevisionId",
                principalSchema: "documents",
                principalTable: "document_template_revisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_translation_documents_document_template_revisions_TemplateRevisionId",
                schema: "documents",
                table: "translation_documents");

            migrationBuilder.DropTable(
                name: "template_assets",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "IX_translation_documents_TemplateRevisionId",
                schema: "documents",
                table: "translation_documents");

            migrationBuilder.DropColumn(
                name: "TemplateRevisionId",
                schema: "documents",
                table: "translation_documents");
        }
    }
}
