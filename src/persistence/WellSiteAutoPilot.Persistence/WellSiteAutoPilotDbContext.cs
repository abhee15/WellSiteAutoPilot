using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Messaging;

namespace WellSiteAutoPilot.Persistence;

public sealed class WellSiteAutoPilotDbContext(DbContextOptions<WellSiteAutoPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<AssetTypeEntity> AssetTypes => Set<AssetTypeEntity>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var assetType = modelBuilder.Entity<AssetTypeEntity>();
        assetType.ToTable("asset_types", "assets");
        assetType.HasKey(x => x.Id);
        assetType.Property(x => x.Key).HasMaxLength(128).IsRequired();
        assetType.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        assetType.Property(x => x.SchemaVersion).IsRequired();
        assetType.Property(x => x.AttributeSchemaJson).IsRequired();
        assetType.Property(x => x.IsActive).IsRequired();
        assetType.Property(x => x.CreatedAtUtc).IsRequired();
        assetType.HasIndex(x => x.Key).IsUnique();

        var asset = modelBuilder.Entity<AssetEntity>();
        asset.ToTable("assets", "assets");
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Name).HasMaxLength(256).IsRequired();
        asset.Property(x => x.AttributeValuesJson).IsRequired();
        asset.Property(x => x.IsActive).IsRequired();
        asset.Property(x => x.CreatedAtUtc).IsRequired();
        asset.HasIndex(x => x.AssetTypeId);
        asset.HasIndex(x => x.ParentAssetId);
        asset.HasOne<AssetTypeEntity>()
            .WithMany()
            .HasForeignKey(x => x.AssetTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        asset.HasOne<AssetEntity>()
            .WithMany()
            .HasForeignKey(x => x.ParentAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        var outbox = modelBuilder.Entity<OutboxMessageEntity>();
        outbox.ToTable("outbox_messages", "messaging");
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Type).HasMaxLength(256).IsRequired();
        outbox.Property(x => x.Payload).IsRequired();
        outbox.Property(x => x.CreatedAtUtc).IsRequired();
        outbox.HasIndex(x => x.ProcessedAtUtc);

        var inbox = modelBuilder.Entity<InboxMessageEntity>();
        inbox.ToTable("inbox_messages", "messaging");
        inbox.HasKey(x => new { x.MessageId, x.Consumer });
        inbox.Property(x => x.Consumer).HasMaxLength(256).IsRequired();
        inbox.Property(x => x.ProcessedAtUtc).IsRequired();
    }
}
