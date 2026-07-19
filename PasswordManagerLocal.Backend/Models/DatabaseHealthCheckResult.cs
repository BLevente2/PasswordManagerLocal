namespace PasswordManagerLocal.Backend.Models;

public sealed record DatabaseHealthCheckResult(bool IsHealthy, string DiagnosticCode);
