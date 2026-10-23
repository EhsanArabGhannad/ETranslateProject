using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Documents.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentContentFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                schema: "documents",
                table: "draft_revisions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentSchemaVersion",
                schema: "documents",
                table: "draft_revisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                schema: "documents",
                table: "document_template_revisions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentSchemaVersion",
                schema: "documents",
                table: "document_template_revisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_draft_revisions_content_metadata",
                schema: "documents",
                table: "draft_revisions",
                sql: "([ContentFormat] IS NULL AND [ContentSchemaVersion] IS NULL) OR ([ContentFormat] IS NOT NULL AND LEN([ContentFormat]) > 0 AND [ContentSchemaVersion] IS NOT NULL AND [ContentSchemaVersion] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_template_revisions_content_metadata",
                schema: "documents",
                table: "document_template_revisions",
                sql: "([ContentFormat] IS NULL AND [ContentSchemaVersion] IS NULL) OR ([ContentFormat] IS NOT NULL AND LEN([ContentFormat]) > 0 AND [ContentSchemaVersion] IS NOT NULL AND [ContentSchemaVersion] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_draft_revisions_content_metadata",
                schema: "documents",
                table: "draft_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_template_revisions_content_metadata",
                schema: "documents",
                table: "document_template_revisions");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                schema: "documents",
                table: "draft_revisions");

            migrationBuilder.DropColumn(
                name: "ContentSchemaVersion",
                schema: "documents",
                table: "draft_revisions");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                schema: "documents",
                table: "document_template_revisions");

            migrationBuilder.DropColumn(
                name: "ContentSchemaVersion",
                schema: "documents",
                table: "document_template_revisions");
        }
    }
}
