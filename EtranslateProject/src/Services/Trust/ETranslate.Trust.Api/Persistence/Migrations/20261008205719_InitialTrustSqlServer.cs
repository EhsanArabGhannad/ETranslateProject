using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Trust.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrustSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trust");

            migrationBuilder.CreateTable(
                name: "inbox_state",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_state", x => x.Id);
                    table.UniqueConstraint("AK_inbox_state_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "outbox_state",
                schema: "trust",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_state", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "signing_preparations",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TranslationJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewRound = table.Column<int>(type: "int", nullable: false),
                    PdfVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    TemplateRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PdfSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PdfSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceFilesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignaturePolicy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signing_preparations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "trust",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnqueueTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_outbox_messages_inbox_state_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalSchema: "trust",
                        principalTable: "inbox_state",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_outbox_messages_outbox_state_OutboxId",
                        column: x => x.OutboxId,
                        principalSchema: "trust",
                        principalTable: "outbox_state",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "planned_signature_stages",
                schema: "trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    SignerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedStatement = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RequestedMethod = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planned_signature_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_planned_signature_stages_signing_preparations_PreparationId",
                        column: x => x.PreparationId,
                        principalSchema: "trust",
                        principalTable: "signing_preparations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_state_Delivered",
                schema: "trust",
                table: "inbox_state",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_EnqueueTime",
                schema: "trust",
                table: "outbox_messages",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ExpirationTime",
                schema: "trust",
                table: "outbox_messages",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_InboxMessageId_InboxConsumerId_SequenceNumber",
                schema: "trust",
                table: "outbox_messages",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true,
                filter: "[InboxMessageId] IS NOT NULL AND [InboxConsumerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OutboxId_SequenceNumber",
                schema: "trust",
                table: "outbox_messages",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true,
                filter: "[OutboxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_state_Created",
                schema: "trust",
                table: "outbox_state",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_planned_signature_stages_PreparationId_Order",
                schema: "trust",
                table: "planned_signature_stages",
                columns: new[] { "PreparationId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signing_preparations_TenantId_DocumentId",
                schema: "trust",
                table: "signing_preparations",
                columns: new[] { "TenantId", "DocumentId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_signing_preparations_TenantId_TranslationJobId_DocumentId_CreatedAtUtc",
                schema: "trust",
                table: "signing_preparations",
                columns: new[] { "TenantId", "TranslationJobId", "DocumentId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "planned_signature_stages",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "inbox_state",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "outbox_state",
                schema: "trust");

            migrationBuilder.DropTable(
                name: "signing_preparations",
                schema: "trust");
        }
    }
}
