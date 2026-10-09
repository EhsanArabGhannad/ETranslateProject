namespace ETranslate.Trust.Api.Preparations;

public sealed class PlannedSignatureStage
{
    private PlannedSignatureStage() { }
    internal PlannedSignatureStage(Guid preparationId, int order, PlannedSignerRole role, Guid signer,
        string proposedStatement, RequestedSignatureMethod method)
    {
        if (signer == Guid.Empty || !Enum.IsDefined(method) || string.IsNullOrWhiteSpace(proposedStatement) || proposedStatement.Length > 4000)
            throw new ArgumentException("A proposed statement (maximum 4000 characters) and valid requested method are required.");
        Id = Guid.NewGuid(); PreparationId = preparationId; Order = order; Role = role; SignerUserId = signer;
        ProposedStatement = proposedStatement; RequestedMethod = method;
    }
    public Guid Id { get; private init; }
    public Guid PreparationId { get; private init; }
    public int Order { get; private init; }
    public PlannedSignerRole Role { get; private init; }
    public Guid SignerUserId { get; private init; }
    public string ProposedStatement { get; private init; } = "";
    public RequestedSignatureMethod RequestedMethod { get; private init; }
}
