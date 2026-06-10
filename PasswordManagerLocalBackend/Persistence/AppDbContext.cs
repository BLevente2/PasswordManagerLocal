using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<SyncItem> SyncItems => Set<SyncItem>();
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    public DbSet<SyncTombstone> SyncTombstones => Set<SyncTombstone>();
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

        device.Property(d => d.DeviceName)
            .IsRequired()
            .HasMaxLength(64);

        device.Property(d => d.BlockedReason)
            .HasMaxLength(512);

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
            .UsingEntity<UserDevice>(
                r => r.HasOne(ud => ud.Device)
                    .WithMany(d => d.UserDevices)
                    .HasForeignKey(ud => ud.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne(ud => ud.User)
                    .WithMany(u => u.UserDevices)
                    .HasForeignKey(ud => ud.UserId)
                    .OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.ToTable("UserDevices");
                    j.HasKey(ud => new { ud.UserId, ud.DeviceId });

                    j.Property(ud => ud.Name)
                        .IsRequired()
                        .HasMaxLength(64);

                    j.Property(ud => ud.LinkedAt)
                        .IsRequired();

                    j.Property(ud => ud.LastModifiedAt)
                        .IsRequired();

                    j.Property(ud => ud.IsSyncEnabled)
                        .IsRequired();

                    j.Property(ud => ud.IsDeleted)
                        .IsRequired();

                    j.HasIndex(ud => ud.DeviceId);
                    j.HasIndex(ud => new { ud.UserId, ud.Name })
                        .IsUnique()
                        .HasFilter("\"IsDeleted\" = 0");
                    j.HasIndex(ud => new { ud.UserId, ud.IsDeleted, ud.IsSyncEnabled });
                    j.HasIndex(ud => new { ud.DeviceId, ud.IsDeleted });
                });



        var tombstone = model.Entity<SyncTombstone>();
        tombstone.HasKey(t => t.Id);
        tombstone.HasIndex(t => new { t.ModelId, t.ModelType }).IsUnique();
        tombstone.Property(t => t.DeletedAtTs).IsRequired();

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
        ldi.Property(x => x.DeviceName).IsRequired().HasMaxLength(64);
        ldi.Property(x => x.IsSyncOn).IsRequired().HasDefaultValue(true);
        ldi.Property(x => x.CreatedAt).IsRequired();
    }
}
