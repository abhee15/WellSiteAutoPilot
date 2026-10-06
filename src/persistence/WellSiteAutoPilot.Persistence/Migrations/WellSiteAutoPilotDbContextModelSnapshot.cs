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
            "WellSiteAutoPilot.Persistence.Messaging.InboxMessageEntity",
            entity =>
            {
                entity.Property<Guid>("MessageId")
                    .HasColumnType("uuid");

                entity.Property<string>("Consumer")
                    .IsRequired()
                    .HasMaxLength(256)
                    .HasColumnType("character varying(256)");

                entity.Property<DateTimeOffset>("ProcessedAtUtc")
                    .HasColumnType("timestamp with time zone");

                entity.HasKey("MessageId", "Consumer");

                entity.ToTable("inbox_messages", "messaging");
            });

        modelBuilder.Entity(
            "WellSiteAutoPilot.Persistence.Messaging.OutboxMessageEntity",
            entity =>
            {
                entity.Property<Guid>("Id")
                    .HasColumnType("uuid");

                entity.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone");

                entity.Property<string>("Payload")
                    .IsRequired()
                    .HasColumnType("text");

                entity.Property<DateTimeOffset?>("ProcessedAtUtc")
                    .HasColumnType("timestamp with time zone");

                entity.Property<string>("Type")
                    .IsRequired()
                    .HasMaxLength(256)
                    .HasColumnType("character varying(256)");

                entity.HasKey("Id");

                entity.HasIndex("ProcessedAtUtc");

                entity.ToTable("outbox_messages", "messaging");
            });
#pragma warning restore 612, 618
    }
}
