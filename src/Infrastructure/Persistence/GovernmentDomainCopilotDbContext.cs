
using GovernmentDomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovernmentDomainCopilot.Infrastructure.Persistence;

public sealed class GovernmentDomainCopilotDbContext(
    DbContextOptions<GovernmentDomainCopilotDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isNpgsql = Database.IsNpgsql();

        if (isNpgsql)
        {
            modelBuilder.HasPostgresExtension("vector");
        }

        ConfigureTenant(modelBuilder.Entity<Tenant>());
        ConfigureUser(modelBuilder.Entity<User>());
        ConfigureDocument(modelBuilder.Entity<Document>());
        ConfigureDocumentChunk(modelBuilder.Entity<DocumentChunk>(), isNpgsql);
        ConfigureConversationSession(modelBuilder.Entity<ConversationSession>());
        ConfigureRun(modelBuilder.Entity<Run>());
        ConfigureApproval(modelBuilder.Entity<Approval>());
        ConfigureAuditLog(modelBuilder.Entity<AuditLog>());
    }

    private static void ConfigureTenant(EntityTypeBuilder<Tenant> entity)
    {
        entity.ToTable("Tenants");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
        entity.Property(item => item.CreatedAtUtc).IsRequired();
    }

    private static void ConfigureUser(EntityTypeBuilder<User> entity)
    {
        ConfigureTenantOwnedEntity(entity, "Users");
        entity.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
        entity.Property(item => item.DisplayName).HasMaxLength(200).IsRequired();
        entity.Property(item => item.CreatedAtUtc).IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.ExternalId }).IsUnique();
    }

    private static void ConfigureDocument(EntityTypeBuilder<Document> entity)
    {
        ConfigureTenantOwnedEntity(entity, "Documents");
        entity.Property(item => item.Title).HasMaxLength(500).IsRequired();
        entity.Property(item => item.SourceReference).HasMaxLength(2_000).IsRequired();
        entity.Property(item => item.IngestionStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(item => item.FailureReason)
            .HasMaxLength(Document.MaxFailureReasonLength)
            .IsRequired(false);
        entity.Property(item => item.CreatedAtUtc).IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.SourceReference }).IsUnique();
    }

    private static void ConfigureDocumentChunk(EntityTypeBuilder<DocumentChunk> entity, bool isNpgsql)
    {
        ConfigureTenantOwnedEntity(entity, "DocumentChunks");
        entity.Property(item => item.Content).IsRequired();
        entity.Property(item => item.Sequence).IsRequired();

        if (isNpgsql)
        {
            entity.Property(item => item.Embedding)
                .HasColumnType("vector(768)")
                .HasConversion(
                    v => v == null ? null : new Pgvector.Vector(v),
                    v => v == null ? null : v.ToArray())
                .IsRequired(false);

            entity.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
                .HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('simple', coalesce(\"Content\", ''))", stored: true);

            entity.HasIndex("SearchVector")
                .HasMethod("gin");
        }
        else
        {
            entity.Property(item => item.Embedding)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v),
                    v => string.IsNullOrEmpty(v) ? null : v.Split(',', StringSplitOptions.None).Select(float.Parse).ToArray())
                .IsRequired(false);
        }

        entity.HasIndex(item => new { item.TenantId, item.DocumentId, item.Sequence }).IsUnique();
        entity.HasOne<Document>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.DocumentId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureConversationSession(EntityTypeBuilder<ConversationSession> entity)
    {
        ConfigureTenantOwnedEntity(entity, "ConversationSessions");
        entity.Property(item => item.CreatedAtUtc).IsRequired();
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.UserId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRun(EntityTypeBuilder<Run> entity)
    {
        ConfigureTenantOwnedEntity(entity, "Runs");
        entity.Property(item => item.Status).HasMaxLength(100).IsRequired();
        entity.Property(item => item.CreatedAtUtc).IsRequired();
        entity.HasOne<ConversationSession>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ConversationSessionId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureApproval(EntityTypeBuilder<Approval> entity)
    {
        ConfigureTenantOwnedEntity(entity, "Approvals");
        entity.Property(item => item.Status).HasMaxLength(100).IsRequired();
        entity.Property(item => item.RequestedAtUtc).IsRequired();
        entity.HasOne<Run>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.RunId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAuditLog(EntityTypeBuilder<AuditLog> entity)
    {
        ConfigureTenantOwnedEntity(entity, "AuditLogs");
        entity.Property(item => item.EventType).HasMaxLength(200).IsRequired();
        entity.Property(item => item.OccurredAtUtc).IsRequired();
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ActorUserId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTenantOwnedEntity<TEntity>(
        EntityTypeBuilder<TEntity> entity,
        string tableName)
        where TEntity : TenantOwnedEntity
    {
        entity.ToTable(tableName);
        entity.HasKey(item => item.Id);
        entity.Property(item => item.TenantId).IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.Id }).IsUnique();
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


