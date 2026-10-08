namespace FlowHearth.Domain.Service;

public enum ServiceRecordType
{
    Note,
    Diagnosis,
    Action,
    StatusChange,
    Assignment,
}

public static class ServiceRecordTypePolicy
{
    public static bool IsManual(ServiceRecordType type) =>
        type is ServiceRecordType.Note or ServiceRecordType.Diagnosis or ServiceRecordType.Action;
}
