namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

internal sealed record TaskInboxItemResponse(
    Guid Id,
    Guid WorkflowInstanceId,
    Guid AsignadoAUserId,
    TaskInboxEstado Estado,
    DateTime AsignadaAtUtc,
    Guid? ResueltaPorUserId,
    DateTime? ResueltaAtUtc,
    DateTime? LeidoAtUtc);
