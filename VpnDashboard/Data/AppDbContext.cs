using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data.Entities;

namespace VpnDashboard.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LocalUser> LocalUsers => Set<LocalUser>();
    public DbSet<HiddifyServer> HiddifyServers => Set<HiddifyServer>();
    public DbSet<UserServerBinding> UserServerBindings => Set<UserServerBinding>();
    public DbSet<ManualSubscription> ManualSubscriptions => Set<ManualSubscription>();
    public DbSet<GlobalSubscription> GlobalSubscriptions => Set<GlobalSubscription>();
    public DbSet<UserGlobalSubscription> UserGlobalSubscriptions => Set<UserGlobalSubscription>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LocalUser>(e =>
        {
            e.HasIndex(x => x.ShowcaseToken).IsUnique();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.ShowcaseToken).IsRequired();
        });

        b.Entity<HiddifyServer>(e =>
        {
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Domain).IsRequired();
        });

        b.Entity<UserServerBinding>(e =>
        {
            e.HasIndex(x => new { x.LocalUserId, x.HiddifyServerId }).IsUnique();

            e.HasOne(x => x.LocalUser)
                .WithMany(u => u.Bindings)
                .HasForeignKey(x => x.LocalUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.HiddifyServer)
                .WithMany(s => s.Bindings)
                .HasForeignKey(x => x.HiddifyServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ManualSubscription>(e =>
        {
            e.HasOne(x => x.LocalUser)
                .WithMany(u => u.ManualSubscriptions)
                .HasForeignKey(x => x.LocalUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserGlobalSubscription>(e =>
        {
            e.HasKey(x => new { x.LocalUserId, x.GlobalSubscriptionId });

            e.HasOne(x => x.LocalUser)
                .WithMany(u => u.GlobalSubscriptions)
                .HasForeignKey(x => x.LocalUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.GlobalSubscription)
                .WithMany(g => g.Assignments)
                .HasForeignKey(x => x.GlobalSubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Проставляет CreatedAt/UpdatedAt для сущностей, у которых они есть.</summary>
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
                entry.Property("UpdatedAt").CurrentValue = now;

            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null)
                entry.Property("CreatedAt").CurrentValue = now;
        }
    }
}
