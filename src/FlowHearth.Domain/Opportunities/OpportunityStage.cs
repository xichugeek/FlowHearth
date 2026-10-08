namespace FlowHearth.Domain.Opportunities;

public enum OpportunityStage
{
    Lead,
    Qualified,
    Proposal,
    Negotiation,
    Won,
    Lost,
}

public static class OpportunityStagePolicy
{
    public static bool IsActive(OpportunityStage stage) =>
        stage is OpportunityStage.Lead
            or OpportunityStage.Qualified
            or OpportunityStage.Proposal
            or OpportunityStage.Negotiation;

    public static bool IsTerminal(OpportunityStage stage) =>
        stage is OpportunityStage.Won or OpportunityStage.Lost;

    public static bool CanTransition(
        OpportunityStage current,
        OpportunityStage target)
    {
        return current != target
            && IsActive(current)
            && (IsActive(target) || IsTerminal(target));
    }
}
