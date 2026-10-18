using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PinTemplatePerDraftRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions",
                type: "uniqueidentifier",
                nullable: true);

            // Before this release, a document's template could never change after its first draft.
            // All legacy drafts therefore share that immutable document-level pin (including null).
            migrationBuilder.Sql("""
                UPDATE draft SET TemplateRevisionId = document.TemplateRevisionId
                FROM documents.draft_revisions AS draft
                INNER JOIN documents.translation_documents AS document ON draft.DocumentId = document.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_draft_revisions_TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions",
                column: "TemplateRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_draft_revisions_document_template_revisions_TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions",
                column: "TemplateRevisionId",
                principalSchema: "documents",
                principalTable: "document_template_revisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM documents.draft_revisions AS draft
                    INNER JOIN documents.translation_documents AS document ON draft.DocumentId = document.Id
                    WHERE draft.TemplateRevisionId <> document.TemplateRevisionId
                       OR (draft.TemplateRevisionId IS NULL AND document.TemplateRevisionId IS NOT NULL)
                       OR (draft.TemplateRevisionId IS NOT NULL AND document.TemplateRevisionId IS NULL))
                    THROW 51000, 'Cannot remove per-draft template pins after template changes. Restore a compatible backup instead.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_draft_revisions_document_template_revisions_TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions");

            migrationBuilder.DropIndex(
                name: "IX_draft_revisions_TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions");

            migrationBuilder.DropColumn(
                name: "TemplateRevisionId",
                schema: "documents",
                table: "draft_revisions");
        }
    }
}
