namespace FlowHearth.Domain.Projects;

public enum ProjectStatus
{
    Planning,
    Active,
    OnHold,
    Completed,
    Cancelled,
}

public static class ProjectStatusPolicy
{
    public static bool IsTerminal(ProjectStatus status) =>
        status is ProjectStatus.Completed or ProjectStatus.Cancelled;

    public static bool CanTransition(ProjectStatus current, ProjectStatus target)
    {
        if (current == target || IsTerminal(current))
        {
            return false;
        }

        return current switch
        {
            ProjectStatus.Planning => target is ProjectStatus.Active or ProjectStatus.Cancelled,
            ProjectStatus.Active => target is ProjectStatus.OnHold or ProjectStatus.Completed or ProjectStatus.Cancelled,
            ProjectStatus.OnHold => target is ProjectStatus.Active or ProjectStatus.Cancelled,
            _ => false,
        };
    }
}
