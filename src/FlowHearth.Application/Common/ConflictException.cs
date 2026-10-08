namespace FlowHearth.Application.Common;

public sealed class ConflictException(string message) : Exception(message);
