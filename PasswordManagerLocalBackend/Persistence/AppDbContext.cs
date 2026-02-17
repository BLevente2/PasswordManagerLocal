using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<SyncItem> SyncItems => Set<SyncItem>();
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    public DbSet<LocalDeviceIdentity> LocalDeviceIdentities => Set<LocalDeviceIdentity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        model.Entity<User>()
            .HasKey(u => u.UId);

        model.Entity<Group>()
            .HasKey(g => g.Id);

        var device = model.Entity<Device>();
        device.HasKey(d => d.Id);

        device.Property(d => d.PublicKey).IsRequired();
        device.Property(d => d.SignPublicKey).IsRequired();

        device.Property(d => d.TlsCertFingerprint)
            .IsRequired()
            .HasMaxLength(128);

        device.HasIndex(d => d.TlsCertFingerprint)
            .IsUnique();

        var syncItem = model.Entity<SyncItem>();
        syncItem.HasKey(si => si.Id);

        syncItem.HasIndex(si => new { si.ModelId, si.ModelType })
            .IsUnique();

        var queue = model.Entity<SyncQueueItem>();
        queue.ToTable("SyncQueueItems");

        queue.HasKey(q => q.QueueId);

        queue.Property(q => q.QueueId)
            .ValueGeneratedOnAdd();

        queue.HasAlternateKey(q => q.Id);

        queue.Property(q => q.EnqueuedAt)
            .IsRequired();

        queue.HasOne(q => q.Device)
            .WithMany(d => d.ItemsNeedingSync)
            .HasForeignKey(q => q.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        queue.HasOne(q => q.SyncItem)
            .WithMany(si => si.QueueItems)
            .HasForeignKey(q => q.SyncItemId)
            .OnDelete(DeleteBehavior.Cascade);

        queue.HasIndex(q => new { q.ProcessedAt, q.QueueId });
        queue.HasIndex(q => new { q.DeviceId, q.ProcessedAt, q.QueueId });

        queue.HasIndex(q => new { q.DeviceId, q.SyncItemId })
            .IsUnique();

        model.Entity<User>()
            .HasMany(u => u.Groups)
            .WithMany(g => g.Users)
            .UsingEntity(j => j.ToTable("GroupMembers"));

        model.Entity<User>()
            .HasMany(u => u.Devices)
            .WithMany(d => d.Users)
            .UsingEntity(j => j.ToTable("UserDevices"));

        var ldi = model.Entity<LocalDeviceIdentity>();

        ldi.ToTable("LocalDeviceIdentity", t =>
            t.HasCheckConstraint("CK_LocalDeviceIdentity_SingletonKey", "SingletonKey = 1"));

        ldi.HasKey(x => x.Id);

        ldi.Property<int>("SingletonKey")
            .HasDefaultValue(1)
            .IsRequired();

        ldi.HasIndex("SingletonKey")
            .IsUnique();

        ldi.Property(x => x.AgreementPrivateKeyBlob).IsRequired();
        ldi.Property(x => x.SignPrivateKeyBlob).IsRequired();
        ldi.Property(x => x.PFXCertificate).IsRequired();
        ldi.Property(x => x.CreatedAt).IsRequired();
    }
}
