using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace WellSiteAutoPilot.Persistence.Migrations;

[DbContext(typeof(WellSiteAutoPilotDbContext))]
sealed partial class WellSiteAutoPilotDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Assets.AssetTypeEntity",
            entity =>
            {
                entity.Property<Guid>("Id").HasColumnType("uuid");
                entity.Property<string>("AttributeSchemaJson").IsRequired().HasColumnType("text");
                entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<string>("DisplayName").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.Property<bool>("IsActive").HasColumnType("boolean");
                entity.Property<string>("Key").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
                entity.Property<int>("SchemaVersion").HasColumnType("integer");
                entity.HasKey("Id");
                entity.HasIndex("Key").IsUnique();
                entity.ToTable("asset_types", "assets");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Assets.AssetEntity",
            entity =>
            {
                entity.Property<Guid>("Id").HasColumnType("uuid");
                entity.Property<Guid>("AssetTypeId").HasColumnType("uuid");
                entity.Property<string>("AttributeValuesJson").IsRequired().HasColumnType("text");
                entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<bool>("IsActive").HasColumnType("boolean");
                entity.Property<string>("Name").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.Property<Guid?>("ParentAssetId").HasColumnType("uuid");
                entity.HasKey("Id");
                entity.HasIndex("AssetTypeId");
                entity.HasIndex("ParentAssetId");
                entity.ToTable("assets", "assets");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Executions.ExecutionEntity",
            entity =>
            {
                entity.Property<Guid>("Id").HasColumnType("uuid");
                entity.Property<Guid>("AssetId").HasColumnType("uuid");
                entity.Property<string>("AssetExternalId").IsRequired().HasMaxLength(512).HasColumnType("character varying(512)");
                entity.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<Guid>("ConfigurationRevisionId").HasColumnType("uuid");
                entity.Property<string>("CorrelationId").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
                entity.Property<string>("FailureCode").HasMaxLength(128).HasColumnType("character varying(128)");
                entity.Property<Guid>("LogicInstanceId").HasColumnType("uuid");
                entity.Property<string>("Mode").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
                entity.Property<string>("ModuleId").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.Property<string>("ModuleVersion").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
                entity.Property<string>("OutputJson").HasColumnType("text");
                entity.Property<string>("Quantity").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.Property<DateTimeOffset>("RequestedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<string>("ResultCode").HasMaxLength(128).HasColumnType("character varying(128)");
                entity.Property<DateTimeOffset?>("StartedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
                entity.HasKey("Id");
                entity.HasIndex("AssetId");
                entity.HasIndex("LogicInstanceId");
                entity.HasIndex("Status", "RequestedAtUtc");
                entity.ToTable("executions", "operations");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Messaging.InboxMessageEntity",
            entity =>
            {
                entity.Property<Guid>("MessageId").HasColumnType("uuid");
                entity.Property<string>("Consumer").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.Property<DateTimeOffset>("ProcessedAtUtc").HasColumnType("timestamp with time zone");
                entity.HasKey("MessageId", "Consumer");
                entity.ToTable("inbox_messages", "messaging");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Messaging.OutboxMessageEntity",
            entity =>
            {
                entity.Property<Guid>("Id").HasColumnType("uuid");
                entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<string>("Payload").IsRequired().HasColumnType("text");
                entity.Property<DateTimeOffset?>("ProcessedAtUtc").HasColumnType("timestamp with time zone");
                entity.Property<string>("Type").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
                entity.HasKey("Id");
                entity.HasIndex("ProcessedAtUtc");
                entity.ToTable("outbox_messages", "messaging");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Assets.AssetEntity",
            entity =>
            {
                entity.HasOne("WellSiteAutoPilot.Persistence.Assets.AssetTypeEntity", null)
                    .WithMany()
                    .HasForeignKey("AssetTypeId")
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();

                entity.HasOne("WellSiteAutoPilot.Persistence.Assets.AssetEntity", null)
                    .WithMany()
                    .HasForeignKey("ParentAssetId")
                    .OnDelete(DeleteBehavior.Restrict);
            });
#pragma warning restore 612, 618
    }
}
