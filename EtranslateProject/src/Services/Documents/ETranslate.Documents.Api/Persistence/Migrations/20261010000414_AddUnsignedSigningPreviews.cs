using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnsignedSigningPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signing_previews",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePdfVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePdfSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RendererVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlanSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signing_previews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_signing_previews_document_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "documents",
                        principalTable: "document_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_signing_previews_pdf_versions_SourcePdfVersionId",
                        column: x => x.SourcePdfVersionId,
                        principalSchema: "documents",
                        principalTable: "pdf_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_signing_previews_translation_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "translation_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_signing_previews_DocumentId_CreatedAtUtc",
                schema: "documents",
                table: "signing_previews",
                columns: new[] { "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_signing_previews_PreparationId_RendererVersion",
                schema: "documents",
                table: "signing_previews",
                columns: new[] { "PreparationId", "RendererVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signing_previews_ReviewId",
                schema: "documents",
                table: "signing_previews",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_signing_previews_SourcePdfVersionId",
                schema: "documents",
                table: "signing_previews",
                column: "SourcePdfVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_signing_previews_StorageKey",
                schema: "documents",
                table: "signing_previews",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signing_previews",
                schema: "documents");
        }
    }
}
