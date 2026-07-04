namespace PasswordManagerLocal.Frontend.Security;

public sealed record PasswordStrengthResult(int Score, double EstimatedEntropyBits);
