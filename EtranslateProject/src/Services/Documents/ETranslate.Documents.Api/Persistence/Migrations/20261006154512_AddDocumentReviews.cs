using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewRound",
                schema: "documents",
                table: "translation_documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                schema: "documents",
                table: "translation_documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "documents",
                table: "translation_documents",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_reviews",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    PdfVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    PdfSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceFilesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_reviews_draft_revisions_DraftRevisionId",
                        column: x => x.DraftRevisionId,
                        principalSchema: "documents",
                        principalTable: "draft_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_reviews_pdf_versions_PdfVersionId",
                        column: x => x.PdfVersionId,
                        principalSchema: "documents",
                        principalTable: "pdf_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_reviews_translation_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "translation_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_reviews_DocumentId_Round",
                schema: "documents",
                table: "document_reviews",
                columns: new[] { "DocumentId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_reviews_DraftRevisionId",
                schema: "documents",
                table: "document_reviews",
                column: "DraftRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_reviews_PdfVersionId",
                schema: "documents",
                table: "document_reviews",
                column: "PdfVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_reviews",
                schema: "documents");

            migrationBuilder.DropColumn(
                name: "ReviewRound",
                schema: "documents",
                table: "translation_documents");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                schema: "documents",
                table: "translation_documents");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "documents",
                table: "translation_documents");
        }
    }
}
