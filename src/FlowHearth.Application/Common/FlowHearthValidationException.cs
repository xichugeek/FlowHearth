namespace FlowHearth.Application.Common;

public sealed class FlowHearthValidationException : Exception
{
    public FlowHearthValidationException(
        IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static FlowHearthValidationException For(string field, string message)
    {
        return new FlowHearthValidationException(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            });
    }
}
