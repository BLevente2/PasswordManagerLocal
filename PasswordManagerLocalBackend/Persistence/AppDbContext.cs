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
    public DbSet<LocalUserDevice> LocalUserDevices => Set<LocalUserDevice>();

    public override int SaveChanges()
    {
        GenerateDerivedValuesAndRelationshipIntegrityHashes();
        return base.SaveChanges(true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GenerateDerivedValuesAndRelationshipIntegrityHashes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GenerateDerivedValuesAndRelationshipIntegrityHashes();
        return base.SaveChangesAsync(true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GenerateDerivedValuesAndRelationshipIntegrityHashes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GenerateDerivedValuesAndRelationshipIntegrityHashes()
    {
        foreach (var entry in ChangeTracker.Entries<Device>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.SignPublicKeyHash = entry.Entity.SignPublicKey.Length == 0
                ? []
                : Security.Hashing.SHA256Hash(entry.Entity.SignPublicKey);
            entry.Entity.GenerateIntegrityHash();
        }

        foreach (var entry in ChangeTracker.Entries<UserDevice>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ModelId = Sync.SyncIdentityUtil.BuildUserDeviceModelId(entry.Entity.UserId, entry.Entity.DeviceId);
            entry.Entity.GenerateIntegrityHash();
        }

        foreach (var entry in ChangeTracker.Entries<LocalUserDevice>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.GenerateIntegrityHash();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        model.Entity<User>().HasKey(u => u.UId);
        model.Entity<Group>().HasKey(g => g.Id);

        var device = model.Entity<Device>();
        device.HasKey(d => d.Id);
        device.Property(d => d.PublicKey).IsRequired();
        device.Property(d => d.SignPublicKey).IsRequired();
        device.Property(d => d.SignPublicKeyHash).IsRequired();
        device.Property(d => d.TlsCertFingerprint).IsRequired().HasMaxLength(128);
        device.Property(d => d.DeviceType).HasConversion<byte>().IsRequired();
        device.Property(d => d.BlockedReason).HasMaxLength(512);
        device.HasIndex(d => d.TlsCertFingerprint).IsUnique();
        device.HasIndex(d => d.SignPublicKeyHash);

        var syncItem = model.Entity<SyncItem>();
        syncItem.HasKey(si => si.Id);
        syncItem.HasIndex(si => new { si.ModelId, si.ModelType }).IsUnique();

        var queue = model.Entity<SyncQueueItem>();
        queue.ToTable("SyncQueueItems");
        queue.HasKey(q => q.QueueId);
        queue.Property(q => q.QueueId).ValueGeneratedOnAdd();
        queue.HasAlternateKey(q => q.Id);
        queue.Property(q => q.EnqueuedAt).IsRequired();
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
        queue.HasIndex(q => new { q.DeviceId, q.SyncItemId }).IsUnique();

        var user = model.Entity<User>();
        user.Property(u => u.EncryptedPayload).IsRequired();
        user.Property(u => u.EncryptedGeneralUserDataPayload).IsRequired();
        user.Property(u => u.EncryptedUserPasswordsDataPayload).IsRequired();
        user.Property(u => u.EncryptedUserDevicesDataPayload).IsRequired();
        user.Property(u => u.UserDataLastModifiedAt).IsRequired();
        user.Property(u => u.GeneralUserDataLastModifiedAt).IsRequired();
        user.Property(u => u.UserPasswordsDataLastModifiedAt).IsRequired();
        user.Property(u => u.UserDevicesDataLastModifiedAt).IsRequired();

        model.Entity<User>()
            .HasMany(u => u.Groups)
            .WithMany(g => g.Users)
            .UsingEntity(j => j.ToTable("GroupMembers"));

        var userDevice = model.Entity<UserDevice>();
        userDevice.ToTable("UserDevices");
        userDevice.HasKey(ud => new { ud.UserId, ud.DeviceId });
        userDevice.HasOne(ud => ud.User)
            .WithMany(u => u.UserDevices)
            .HasForeignKey(ud => ud.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        userDevice.HasOne(ud => ud.Device)
            .WithMany(d => d.UserDevices)
            .HasForeignKey(ud => ud.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
        userDevice.Property(ud => ud.ModelId).IsRequired();
        userDevice.Property(ud => ud.LastModifiedAt).IsRequired();
        userDevice.Property(ud => ud.IntegrityHash).IsRequired();
        userDevice.Property(ud => ud.IsSyncOn).IsRequired();
        userDevice.Property(ud => ud.IsDeleted).IsRequired();
        userDevice.HasIndex(ud => ud.ModelId).IsUnique();
        userDevice.HasIndex(ud => ud.DeviceId);
        userDevice.HasIndex(ud => new { ud.UserId, ud.IsDeleted, ud.IsSyncOn });
        userDevice.HasIndex(ud => new { ud.DeviceId, ud.IsDeleted });

        var localUserDevice = model.Entity<LocalUserDevice>();
        localUserDevice.ToTable("LocalUserDevices");
        localUserDevice.HasKey(x => new { x.UserId, x.LocalDeviceIdentityId });
        localUserDevice.HasOne(x => x.User)
            .WithMany(u => u.LocalUserDevices)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        localUserDevice.HasOne(x => x.LocalDeviceIdentity)
            .WithMany(d => d.LocalUsers)
            .HasForeignKey(x => x.LocalDeviceIdentityId)
            .OnDelete(DeleteBehavior.Cascade);
        localUserDevice.Property(x => x.IsSyncOn).IsRequired().HasDefaultValue(true);
        localUserDevice.Property(x => x.IntegrityHash).IsRequired();
        localUserDevice.HasIndex(x => x.UserId).IsUnique();
        localUserDevice.HasIndex(x => x.IsSyncOn);

        var tombstone = model.Entity<SyncTombstone>();
        tombstone.HasKey(t => t.Id);
        tombstone.HasIndex(t => new { t.ModelId, t.ModelType }).IsUnique();
        tombstone.Property(t => t.DeletedAtTs).IsRequired();

        var ldi = model.Entity<LocalDeviceIdentity>();
        ldi.ToTable("LocalDeviceIdentity", t =>
            t.HasCheckConstraint("CK_LocalDeviceIdentity_SingletonKey", "SingletonKey = 1"));
        ldi.HasKey(x => x.Id);
        ldi.Property<int>("SingletonKey").HasDefaultValue(1).IsRequired();
        ldi.HasIndex("SingletonKey").IsUnique();
        ldi.Property(x => x.AgreementPrivateKeyBlob).IsRequired();
        ldi.Property(x => x.SignPrivateKeyBlob).IsRequired();
        ldi.Property(x => x.PFXCertificate).IsRequired();
        ldi.Property(x => x.DeviceType).HasConversion<byte>().IsRequired();
        ldi.Property(x => x.IsSyncOn).IsRequired().HasDefaultValue(false);
        ldi.Property(x => x.CreatedAt).IsRequired();
    }
}
