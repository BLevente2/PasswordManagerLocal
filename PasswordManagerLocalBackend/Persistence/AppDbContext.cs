using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Persistence;

public class AppDbContext : DbContext
{

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>()
            .HasKey(u => u.UId);

        model.Entity<Group>()
            .HasKey(g => g.Id);

        model.Entity<Device>()
            .HasKey(d => d.Id);

        model.Entity<User>()
            .HasMany(u => u.Groups)
            .WithMany(g => g.Users);

        model.Entity<User>()
            .HasMany(u => u.Devices)
            .WithMany(d => d.Users);

        model.Entity("GroupUser").ToTable("GroupMembers");
        model.Entity("DeviceUser").ToTable("UserDevices");
    }
}