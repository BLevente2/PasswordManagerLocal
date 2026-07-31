namespace PasswordManagerLocal.Contracts.Notifications;

public sealed record FrontendChangeNotification(
    Guid RuntimeInstanceId,
    long Sequence,
    FrontendChangeScope Scope,
    Guid? UserId,
    DateTimeOffset OccurredAtUtc);
