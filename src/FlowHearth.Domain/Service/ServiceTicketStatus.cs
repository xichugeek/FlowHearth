namespace FlowHearth.Domain.Service;

public enum ServiceTicketStatus
{
    New,
    InProgress,
    Waiting,
    Resolved,
    Closed,
    Cancelled,
}

public static class ServiceTicketStatusPolicy
{
    public static bool IsTerminal(ServiceTicketStatus status) =>
        status is ServiceTicketStatus.Closed or ServiceTicketStatus.Cancelled;

    public static bool CanTransition(ServiceTicketStatus current, ServiceTicketStatus target)
    {
        if (current == target || IsTerminal(current))
        {
            return false;
        }

        return current switch
        {
            ServiceTicketStatus.New => target is ServiceTicketStatus.InProgress
                or ServiceTicketStatus.Waiting or ServiceTicketStatus.Resolved
                or ServiceTicketStatus.Cancelled,
            ServiceTicketStatus.InProgress => target is ServiceTicketStatus.Waiting
                or ServiceTicketStatus.Resolved or ServiceTicketStatus.Cancelled,
            ServiceTicketStatus.Waiting => target is ServiceTicketStatus.InProgress
                or ServiceTicketStatus.Resolved or ServiceTicketStatus.Cancelled,
            ServiceTicketStatus.Resolved => target is ServiceTicketStatus.InProgress
                or ServiceTicketStatus.Closed,
            _ => false,
        };
    }
}
