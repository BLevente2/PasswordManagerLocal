namespace PasswordManagerLocal.Security;

public sealed record PasswordStrengthResult(int Score, double EstimatedEntropyBits);
