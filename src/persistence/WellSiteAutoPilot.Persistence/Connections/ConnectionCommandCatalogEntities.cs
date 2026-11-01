namespace WellSiteAutoPilot.Persistence.Connections;

/// <summary>The current command catalog of one connection. <see cref="Version"/> is what a change must quote to be accepted.</summary>
public sealed class ConnectionCommandCatalogEntity
{
    public Guid ConnectionId { get; init; }
    public int Version { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
}

/// <summary>One adapter command at one site. Limits are required before <see cref="Enabled"/> can be true, in the database too.</summary>
public sealed class ConnectionCommandCatalogEntryEntity
{
    public Guid ConnectionId { get; init; }
    public string Command { get; init; } = string.Empty;
    public bool Enabled { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public decimal? MaximumStep { get; set; }
    public string TargetJson { get; set; } = "{}";
    public int SettleSeconds { get; set; }
    public int DeadlineSeconds { get; set; }
}

/// <summary>Append-only: the whole catalog as it was after each change, so any past dispatch can be explained.</summary>
public sealed class ConnectionCommandCatalogChangeEntity
{
    public Guid Id { get; init; }
    public Guid ConnectionId { get; init; }
    public int Version { get; init; }
    public string SnapshotJson { get; init; } = "[]";
    public string ChangedBy { get; init; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; init; }
    public string? Reason { get; init; }
}

internal static class ConnectionCommandCatalogModelConfiguration
{
    public static void Configure(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        var catalog = modelBuilder.Entity<ConnectionCommandCatalogEntity>();
        catalog.ToTable("connection_command_catalogs", "integration", table =>
            table.HasCheckConstraint("CK_connection_command_catalogs_version", "\"Version\" > 0"));
        catalog.HasKey(x => x.ConnectionId);
        catalog.Property(x => x.ChangedBy).HasMaxLength(256).IsRequired();
        catalog.Property(x => x.Reason).HasMaxLength(1024);
        catalog.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);

        var entry = modelBuilder.Entity<ConnectionCommandCatalogEntryEntity>();
        entry.ToTable("connection_command_catalog_entries", "integration", table =>
        {
            table.HasCheckConstraint("CK_connection_command_catalog_entries_timing",
                "\"SettleSeconds\" >= 0 AND \"DeadlineSeconds\" > \"SettleSeconds\"");
            table.HasCheckConstraint("CK_connection_command_catalog_entries_limits",
                "NOT \"Enabled\" OR (\"Minimum\" IS NOT NULL AND \"Maximum\" IS NOT NULL AND \"Minimum\" <= \"Maximum\")");
            table.HasCheckConstraint("CK_connection_command_catalog_entries_step",
                "\"MaximumStep\" IS NULL OR \"MaximumStep\" > 0");
        });
        entry.HasKey(x => new { x.ConnectionId, x.Command });
        entry.Property(x => x.Command).HasMaxLength(128).IsRequired();
        entry.Property(x => x.TargetJson).IsRequired();
        entry.HasOne<ConnectionCommandCatalogEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);

        var change = modelBuilder.Entity<ConnectionCommandCatalogChangeEntity>();
        change.ToTable("connection_command_catalog_changes", "integration", table =>
            table.HasCheckConstraint("CK_connection_command_catalog_changes_version", "\"Version\" > 0"));
        change.HasKey(x => x.Id);
        change.Property(x => x.SnapshotJson).IsRequired();
        change.Property(x => x.ChangedBy).HasMaxLength(256).IsRequired();
        change.Property(x => x.Reason).HasMaxLength(1024);
        change.HasIndex(x => new { x.ConnectionId, x.Version }).IsUnique();
        change.HasOne<ConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
    }
}
