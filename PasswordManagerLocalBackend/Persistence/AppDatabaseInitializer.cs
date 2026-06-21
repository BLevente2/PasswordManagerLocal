using Microsoft.EntityFrameworkCore;

namespace PasswordManagerLocalBackend.Persistence;

internal static class AppDatabaseInitializer
{
    public static Task InitializeAsync(AppDbContext db, CancellationToken ct = default) =>
        db.Database.EnsureCreatedAsync(ct);
}
