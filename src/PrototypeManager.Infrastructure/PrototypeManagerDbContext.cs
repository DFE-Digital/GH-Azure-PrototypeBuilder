using Microsoft.EntityFrameworkCore;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

public class PrototypeManagerDbContext(DbContextOptions<PrototypeManagerDbContext> options) : DbContext(options)
{
    public DbSet<BusinessArea> BusinessAreas => Set<BusinessArea>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppUserBusinessAreaAssignment> AppUserBusinessAreaAssignments => Set<AppUserBusinessAreaAssignment>();
    public DbSet<Prototype> Prototypes => Set<Prototype>();
    public DbSet<ProvisioningJob> ProvisioningJobs => Set<ProvisioningJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ExceptionRequest> ExceptionRequests => Set<ExceptionRequest>();
    public DbSet<PrototypeContact> PrototypeContacts => Set<PrototypeContact>();
    public DbSet<PrototypeAppSetting> PrototypeAppSettings => Set<PrototypeAppSetting>();
    public DbSet<ProvisioningJobSecret> ProvisioningJobSecrets => Set<ProvisioningJobSecret>();
    public DbSet<GitHubOrganisation> GitHubOrganisations => Set<GitHubOrganisation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BusinessArea>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(200);
            builder.Property(x => x.Slug).HasMaxLength(80);
            builder.HasIndex(x => x.Name).IsUnique();
            builder.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<AppUser>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Email).HasMaxLength(320);
            builder.Property(x => x.DisplayName).HasMaxLength(200);
            builder.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<AppUserBusinessAreaAssignment>(builder =>
        {
            builder.HasKey(x => new { x.AppUserId, x.BusinessAreaId });
            builder.HasOne(x => x.AppUser)
                .WithMany(x => x.BusinessAreaAssignments)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(x => x.BusinessArea)
                .WithMany(x => x.UserAssignments)
                .HasForeignKey(x => x.BusinessAreaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Prototype>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(200);
            builder.Property(x => x.Slug).HasMaxLength(80);
            builder.Property(x => x.BusinessAreaId);
            builder.Property(x => x.ServiceName).HasMaxLength(200);
            builder.Property(x => x.Team).HasMaxLength(100);
            builder.Property(x => x.Directorate).HasMaxLength(100);
            builder.Property(x => x.OwnerEmail).HasMaxLength(320);
            builder.Property(x => x.DeputyOwnerEmail).HasMaxLength(320);
            builder.Property(x => x.GitHubRepositoryUrl).HasMaxLength(500);
            builder.Property(x => x.AzureSubscriptionId).HasMaxLength(64);
            builder.Property(x => x.AzureResourceGroupName).HasMaxLength(120);
            builder.Property(x => x.AzureAppServicePlanName).HasMaxLength(120);
            builder.Property(x => x.AzureWebAppName).HasMaxLength(120);
            builder.Property(x => x.PrototypeUrl).HasMaxLength(500);
            builder.Property(x => x.CreatedBy).HasMaxLength(320);
            builder.Property(x => x.Reason).HasMaxLength(2000);
            builder.Property(x => x.Region).HasMaxLength(50);
            builder.Property(x => x.Sku).HasMaxLength(20);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(x => x.Status);
            builder.HasIndex(x => x.ExpiresAt);
            builder.HasOne(x => x.BusinessArea)
                .WithMany(x => x.Prototypes)
                .HasForeignKey(x => x.BusinessAreaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProvisioningJob>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
            builder.Property(x => x.SecretReference).HasMaxLength(100);
            builder.HasIndex(x => new { x.Status, x.CreatedAt });
            builder.HasOne(x => x.Prototype)
                .WithMany(x => x.ProvisioningJobs)
                .HasForeignKey(x => x.PrototypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ActorEmail).HasMaxLength(320);
            builder.Property(x => x.Action).HasMaxLength(100);
            builder.Property(x => x.Details).HasMaxLength(4000);
            builder.HasIndex(x => new { x.PrototypeId, x.CreatedAt });
            builder.HasOne(x => x.Prototype)
                .WithMany(x => x.AuditEvents)
                .HasForeignKey(x => x.PrototypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExceptionRequest>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.RequestedBy).HasMaxLength(320);
            builder.Property(x => x.Reason).HasMaxLength(2000);
            builder.Property(x => x.RequestedSku).HasMaxLength(20);
            builder.Property(x => x.ApprovedBy).HasMaxLength(320);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            builder.HasOne(x => x.Prototype)
                .WithMany(x => x.ExceptionRequests)
                .HasForeignKey(x => x.PrototypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrototypeContact>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Email).HasMaxLength(320);
            builder.Property(x => x.CreatedBy).HasMaxLength(320);
            builder.HasIndex(x => new { x.PrototypeId, x.Email }).IsUnique();
            builder.HasOne(x => x.Prototype)
                .WithMany(x => x.Contacts)
                .HasForeignKey(x => x.PrototypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrototypeAppSetting>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Key).HasMaxLength(64);
            builder.Property(x => x.Value).HasMaxLength(500);
            builder.Property(x => x.UpdatedBy).HasMaxLength(320);
            builder.HasIndex(x => new { x.PrototypeId, x.Key }).IsUnique();
            builder.HasOne(x => x.Prototype)
                .WithMany(x => x.AppSettings)
                .HasForeignKey(x => x.PrototypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProvisioningJobSecret>(builder =>
        {
            builder.ToTable("ProvisioningJobSecrets");
            builder.HasKey(x => x.Reference);
            builder.Property(x => x.Reference).HasMaxLength(100);
            builder.Property(x => x.Secret).HasMaxLength(500);
        });

        modelBuilder.Entity<GitHubOrganisation>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(39);
            builder.Property(x => x.CreatedBy).HasMaxLength(320);
            builder.HasIndex(x => x.Name).IsUnique();
        });
    }
}
