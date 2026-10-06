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
#pragma warning restore 612, 618
    }
}
