using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ETranslate.Documents.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
                "/api/v1/tenants/{tenantId:guid}/translation-jobs/{translationJobId:guid}/documents")
            .WithTags("Documents");

        group.MapPost("/", CreateDocumentAsync);
        group.MapGet("/", GetDocumentAsync);
        group.MapPost("/{documentId:guid}/draft-revisions", CreateDraftRevisionAsync);
        group.MapGet("/{documentId:guid}/draft-revisions", GetDraftRevisionsAsync);
        group.MapGet("/{documentId:guid}/draft-revisions/{revisionNumber:int}", GetDraftRevisionAsync);
        group.MapPost("/{documentId:guid}/source-files", UploadSourceFileAsync)
            .DisableAntiforgery();
        group.MapGet("/{documentId:guid}/source-files/{sourceFileId:guid}", DownloadSourceFileAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateDocumentAsync(
        Guid tenantId,
        Guid translationJobId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        TranslationJobClient translationJobClient,
        DocumentsDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var jobLookup = await translationJobClient.GetAsync(
            tenantId,
            translationJobId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var jobError = ToJobLookupError(jobLookup.Status);
        if (jobError is not null)
        {
            return jobError;
        }

        if (await database.TranslationDocuments.AnyAsync(
            document => document.TenantId == tenantId && document.TranslationJobId == translationJobId,
            cancellationToken))
        {
            return Results.Conflict(new { error = "translation_document_already_exists" });
        }

        var now = timeProvider.GetUtcNow();
        var document = TranslationDocument.Create(
            tenantId,
            translationJobId,
            access.Actor!.UserId,
            now);

        database.TranslationDocuments.Add(document);
        await publishEndpoint.Publish(
            new TranslationDocumentCreatedV1(
                Guid.NewGuid(),
                document.Id,
                document.TranslationJobId,
                document.TenantId,
                document.CreatedByUserId,
                now),
            cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = "translation_document_already_exists" });
        }

        return Results.Created(
            $"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents",
            ToResponse(document));
    }

    private static async Task<IResult> GetDocumentAsync(
        Guid tenantId,
        Guid translationJobId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var document = await database.TranslationDocuments
            .AsNoTracking()
            .Include(item => item.SourceFiles)
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.TranslationJobId == translationJobId,
                cancellationToken);

        return document is null ? Results.NotFound() : Results.Ok(ToResponse(document));
    }

    private static async Task<IResult> CreateDraftRevisionAsync(
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        CreateDraftRevisionRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var document = await FindDocumentAsync(
            database,
            tenantId,
            translationJobId,
            documentId,
            cancellationToken);
        if (document is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        DocumentDraftRevision revision;
        try
        {
            revision = document.AddDraftRevision(
                request.ExpectedCurrentRevision,
                request.EditorContentJson,
                request.PlainText,
                access.Actor!.UserId,
                now);
        }
        catch (DocumentValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        catch (DocumentRevisionConflictException exception)
        {
            return RevisionConflict(exception.ExpectedRevision, exception.ActualRevision);
        }

        await publishEndpoint.Publish(
            new DocumentDraftRevisionCreatedV1(
                Guid.NewGuid(),
                document.Id,
                document.TranslationJobId,
                document.TenantId,
                revision.RevisionNumber,
                revision.CreatedByUserId,
                now),
            cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new
            {
                error = "document_revision_conflict",
                expectedRevision = request.ExpectedCurrentRevision
            });
        }

        return Results.Created(
            $"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/draft-revisions/{revision.RevisionNumber}",
            ToResponse(revision));
    }

    private static async Task<IResult> GetDraftRevisionsAsync(
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var exists = await database.TranslationDocuments.AsNoTracking().AnyAsync(
            document =>
                document.Id == documentId &&
                document.TenantId == tenantId &&
                document.TranslationJobId == translationJobId,
            cancellationToken);
        if (!exists)
        {
            return Results.NotFound();
        }

        var revisions = await database.DraftRevisions
            .AsNoTracking()
            .Where(revision => revision.DocumentId == documentId)
            .OrderByDescending(revision => revision.RevisionNumber)
            .Select(revision => new DocumentDraftRevisionSummaryResponse(
                revision.Id,
                revision.RevisionNumber,
                revision.CreatedByUserId,
                revision.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(revisions);
    }

    private static async Task<IResult> GetDraftRevisionAsync(
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        int revisionNumber,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var revision = await (
                from candidate in database.DraftRevisions.AsNoTracking()
                join document in database.TranslationDocuments.AsNoTracking()
                    on candidate.DocumentId equals document.Id
                where document.Id == documentId &&
                      document.TenantId == tenantId &&
                      document.TranslationJobId == translationJobId &&
                      candidate.RevisionNumber == revisionNumber
                select candidate)
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null ? Results.NotFound() : Results.Ok(ToResponse(revision));
    }

    private static async Task<IResult> UploadSourceFileAsync(
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        IFormFile file,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        IDocumentBlobStore blobStore,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var document = await FindDocumentAsync(
            database,
            tenantId,
            translationJobId,
            documentId,
            cancellationToken);
        if (document is null)
        {
            return Results.NotFound();
        }

        var contentType = file.ContentType.Split(';', 2)[0].Trim();
        var fileName = Path.GetFileName(file.FileName);
        var validationErrors = ValidateUpload(fileName, contentType, file.Length);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var sourceFileId = Guid.NewGuid();
        var storageKey = string.Join(
            '/',
            tenantId.ToString("N"),
            documentId.ToString("N"),
            "sources",
            $"{sourceFileId:N}{SourceFilePolicy.GetExtension(contentType)}");

        StoredBlob storedBlob;
        try
        {
            await using var source = file.OpenReadStream();
            storedBlob = await blobStore.SaveAsync(
                storageKey,
                source,
                contentType,
                SourceFilePolicy.MaximumFileSizeBytes,
                cancellationToken);
        }
        catch (BlobTooLargeException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(file)] = [$"File cannot exceed {SourceFilePolicy.MaximumFileSizeBytes} bytes."]
            });
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(file)] = [exception.Message]
            });
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var sourceFile = document.AddSourceFile(
                sourceFileId,
                fileName,
                contentType,
                storedBlob.SizeBytes,
                storedBlob.Sha256,
                storageKey,
                access.Actor!.UserId,
                now);

            await publishEndpoint.Publish(
                new SourceFileUploadedV1(
                    Guid.NewGuid(),
                    sourceFile.Id,
                    document.Id,
                    document.TranslationJobId,
                    document.TenantId,
                    sourceFile.Sha256,
                    sourceFile.SizeBytes,
                    now),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/source-files/{sourceFile.Id}",
                ToResponse(sourceFile));
        }
        catch
        {
            await blobStore.DeleteIfExistsAsync(storageKey, CancellationToken.None);
            throw;
        }
    }

    private static async Task<IResult> DownloadSourceFileAsync(
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        Guid sourceFileId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        IDocumentBlobStore blobStore,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var sourceFile = await (
                from file in database.SourceFiles.AsNoTracking()
                join document in database.TranslationDocuments.AsNoTracking()
                    on file.DocumentId equals document.Id
                where file.Id == sourceFileId &&
                      document.Id == documentId &&
                      document.TenantId == tenantId &&
                      document.TranslationJobId == translationJobId
                select file)
            .SingleOrDefaultAsync(cancellationToken);
        if (sourceFile is null)
        {
            return Results.NotFound();
        }

        var stream = await blobStore.OpenReadAsync(sourceFile.StorageKey, cancellationToken);
        return stream is null
            ? Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Source file metadata exists, but its content is unavailable.")
            : Results.File(
                stream,
                sourceFile.ContentType,
                sourceFile.OriginalFileName,
                enableRangeProcessing: true);
    }

    private static Task<TranslationDocument?> FindDocumentAsync(
        DocumentsDbContext database,
        Guid tenantId,
        Guid translationJobId,
        Guid documentId,
        CancellationToken cancellationToken) =>
        database.TranslationDocuments.SingleOrDefaultAsync(
            document =>
                document.Id == documentId &&
                document.TenantId == tenantId &&
                document.TranslationJobId == translationJobId,
            cancellationToken);

    private static async Task<AuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        bool requiresWriteAccess,
        CancellationToken cancellationToken)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var error = ToAccessError(access.Status);
        if (error is not null)
        {
            return new AuthorizationResult(null, error);
        }

        if (requiresWriteAccess && !access.Actor!.CanManageDocuments)
        {
            return new AuthorizationResult(
                null,
                Results.StatusCode(StatusCodes.Status403Forbidden));
        }

        return new AuthorizationResult(access.Actor, null);
    }

    private static Dictionary<string, string[]> ValidateUpload(
        string fileName,
        string contentType,
        long length)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
        {
            errors[nameof(fileName)] = ["File name is required and cannot exceed 255 characters."];
        }

        if (!SourceFilePolicy.IsSupportedContentType(contentType))
        {
            errors[nameof(contentType)] = ["Only PDF, JPEG, PNG, and TIFF source files are supported."];
        }

        if (length <= 0 || length > SourceFilePolicy.MaximumFileSizeBytes)
        {
            errors[nameof(length)] =
                [$"File must be between 1 byte and {SourceFilePolicy.MaximumFileSizeBytes} bytes."];
        }

        return errors;
    }

    private static IResult? ToAccessError(TenantAccessStatus status) => status switch
    {
        TenantAccessStatus.Authorized => null,
        TenantAccessStatus.Unauthorized => Results.Unauthorized(),
        TenantAccessStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        TenantAccessStatus.IdentityServiceUnavailable => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Identity service is unavailable."),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static IResult? ToJobLookupError(TranslationJobLookupStatus status) => status switch
    {
        TranslationJobLookupStatus.Found => null,
        TranslationJobLookupStatus.NotFound => Results.NotFound(),
        TranslationJobLookupStatus.Unauthorized => Results.Unauthorized(),
        TranslationJobLookupStatus.WorkflowServiceUnavailable => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Translation workflow service is unavailable."),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static IResult RevisionConflict(int expectedRevision, int actualRevision) =>
        Results.Conflict(new
        {
            error = "document_revision_conflict",
            expectedRevision,
            actualRevision
        });

    private static TranslationDocumentResponse ToResponse(TranslationDocument document) =>
        new(
            document.Id,
            document.TenantId,
            document.TranslationJobId,
            document.CreatedByUserId,
            document.CurrentDraftRevision,
            document.SourceFiles
                .OrderBy(file => file.UploadedAtUtc)
                .Select(ToResponse)
                .ToArray(),
            document.CreatedAtUtc,
            document.UpdatedAtUtc);

    private static DocumentDraftRevisionResponse ToResponse(DocumentDraftRevision revision) =>
        new(
            revision.Id,
            revision.DocumentId,
            revision.RevisionNumber,
            revision.EditorContentJson,
            revision.PlainText,
            revision.CreatedByUserId,
            revision.CreatedAtUtc);

    private static SourceFileResponse ToResponse(SourceFile file) =>
        new(
            file.Id,
            file.OriginalFileName,
            file.ContentType,
            file.SizeBytes,
            file.Sha256,
            file.UploadedByUserId,
            file.UploadedAtUtc);

    private sealed record AuthorizationResult(TenantActor? Actor, IResult? Error);
}

public sealed record CreateDraftRevisionRequest(
    int ExpectedCurrentRevision,
    string EditorContentJson,
    string? PlainText);

public sealed record TranslationDocumentResponse(
    Guid Id,
    Guid TenantId,
    Guid TranslationJobId,
    Guid CreatedByUserId,
    int CurrentDraftRevision,
    IReadOnlyCollection<SourceFileResponse> SourceFiles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DocumentDraftRevisionSummaryResponse(
    Guid Id,
    int RevisionNumber,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record DocumentDraftRevisionResponse(
    Guid Id,
    Guid DocumentId,
    int RevisionNumber,
    string EditorContentJson,
    string? PlainText,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record SourceFileResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid UploadedByUserId,
    DateTimeOffset UploadedAtUtc);
