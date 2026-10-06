using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableDraftPdfs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pdf_versions",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    TemplateRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RendererVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pdf_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pdf_versions_document_template_revisions_TemplateRevisionId",
                        column: x => x.TemplateRevisionId,
                        principalSchema: "documents",
                        principalTable: "document_template_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pdf_versions_draft_revisions_DraftRevisionId",
                        column: x => x.DraftRevisionId,
                        principalSchema: "documents",
                        principalTable: "draft_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pdf_versions_translation_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "translation_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pdf_versions_DocumentId",
                schema: "documents",
                table: "pdf_versions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_pdf_versions_DraftRevisionId_RendererVersion",
                schema: "documents",
                table: "pdf_versions",
                columns: new[] { "DraftRevisionId", "RendererVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pdf_versions_StorageKey",
                schema: "documents",
                table: "pdf_versions",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pdf_versions_TemplateRevisionId",
                schema: "documents",
                table: "pdf_versions",
                column: "TemplateRevisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pdf_versions",
                schema: "documents");
        }
    }
}
