namespace FlowHearth.Application.Common;

public sealed class ForbiddenException(string message) : Exception(message);
