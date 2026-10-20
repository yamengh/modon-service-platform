using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Modon.Platform.Organizations;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;

namespace Modon.Platform.Data;

/// <summary>
/// Modon workflow data. Kept out of FormCMS dynamic entities on purpose: statuses may only change
/// through the request endpoints and workflow, never through FormCMS generic CRUD.
/// Lives in the same database as FormCMS, with its own "modon_" tables and EF migrations.
/// </summary>
public class ModonDbContext(DbContextOptions<ModonDbContext> options) : DbContext(options)
{
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<RequestStatusHistory> RequestStatusHistory => Set<RequestStatusHistory>();
    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();
    public DbSet<ExcavationPermitDetails> ExcavationPermitDetails => Set<ExcavationPermitDetails>();
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<EstablishmentMember> EstablishmentMembers => Set<EstablishmentMember>();
    public DbSet<BuildingLicenceDetails> BuildingLicenceDetails => Set<BuildingLicenceDetails>();
    public DbSet<BuildingContact> BuildingContacts => Set<BuildingContact>();
    public DbSet<BuildingTender> BuildingTenders => Set<BuildingTender>();
    public DbSet<BuildingQuotation> BuildingQuotations => Set<BuildingQuotation>();
    public DbSet<ProgressReport> ProgressReports => Set<ProgressReport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store every DateTime as UTC and read it back as UTC (SQLite would otherwise lose the kind).
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<decimal>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Establishment>(e =>
        {
            e.ToTable("modon_establishments");
            e.Property(x => x.CrNumber).HasMaxLength(20);
            e.HasIndex(x => x.CrNumber).IsUnique();
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
        });

        b.Entity<EstablishmentMember>(e =>
        {
            e.ToTable("modon_establishment_members");
            e.HasKey(x => new { x.EstablishmentId, x.UserId });
            e.Property(x => x.UserId).HasMaxLength(450);
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Establishment).WithMany(x => x.Members).HasForeignKey(x => x.EstablishmentId);
        });

        b.Entity<ServiceRequest>(e =>
        {
            e.ToTable("modon_service_requests");
            e.Property(x => x.RequestNumber).HasMaxLength(40);
            e.HasIndex(x => x.RequestNumber).IsUnique();
            e.Property(x => x.ServiceCode).HasMaxLength(60);
            // Concurrency token: a status change only saves if nobody else changed the status first.
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsConcurrencyToken();
            e.Property(x => x.ApplicantUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.ApplicantUserId, x.CreatedAt });
            e.Property(x => x.WorkflowInstanceKey).HasMaxLength(32);
            e.Property(x => x.WorkflowCorrelationKey).HasMaxLength(100);
            e.Property(x => x.WorkflowDefinitionKey).HasMaxLength(32);
            // One request per process instance.
            e.HasIndex(x => x.WorkflowInstanceKey).IsUnique();
            e.HasOne(x => x.Establishment).WithMany().HasForeignKey(x => x.EstablishmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Excavation).WithOne().HasForeignKey<ExcavationPermitDetails>(x => x.RequestId);
            e.HasOne(x => x.Building).WithOne().HasForeignKey<BuildingLicenceDetails>(x => x.RequestId);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.RequestId);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.RequestId);
        });

        b.Entity<RequestStatusHistory>(e =>
        {
            e.ToTable("modon_request_status_history");
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.Comment).HasMaxLength(2000);
        });

        b.Entity<RequestAttachment>(e =>
        {
            e.ToTable("modon_request_attachments");
            e.Property(x => x.SlotCode).HasMaxLength(60);
            e.HasIndex(x => new { x.RequestId, x.SlotCode }).IsUnique();
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.Property(x => x.UploadedByUserId).HasMaxLength(450);
        });

        b.Entity<ExcavationPermitDetails>(e =>
        {
            e.ToTable("modon_excavation_permit_details");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.RequestTypeCode).HasMaxLength(60);
            e.Property(x => x.ContractTypeCode).HasMaxLength(60);
            e.Property(x => x.IndustrialCityCode).HasMaxLength(60);
            e.Property(x => x.PermitTypeCode).HasMaxLength(60);
            e.Property(x => x.ExcavationMethodCode).HasMaxLength(60);
            e.Property(x => x.WorkDescription).HasMaxLength(ExcavationLimits.WorkDescription);
            e.Property(x => x.WorkLocation).HasMaxLength(ExcavationLimits.ShortText);
            e.Property(x => x.ProjectManagerName).HasMaxLength(ExcavationLimits.ShortText);
            e.Property(x => x.ProjectManagerPhone).HasMaxLength(ExcavationLimits.Phone);
            e.Property(x => x.ProjectManagerEmail).HasMaxLength(ExcavationLimits.ShortText);
            e.Property(x => x.SupervisingEntity).HasMaxLength(ExcavationLimits.ShortText);
        });

        ConfigureBuilding(b);
    }

    private static void ConfigureBuilding(ModelBuilder b)
    {
        b.Entity<BuildingLicenceDetails>(e =>
        {
            e.ToTable("modon_building_licence_details");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.ContractNumber).HasMaxLength(BuildingLicence.ContractNumberLength);
            e.Property(x => x.LicenceTypeCode).HasMaxLength(60);
            e.Property(x => x.ScopeOfWork).HasMaxLength(BuildingLicence.LongText);
            e.Property(x => x.Notes).HasMaxLength(BuildingLicence.LongText);
            e.HasMany(x => x.Contacts).WithOne().HasForeignKey(x => x.RequestId);
        });

        b.Entity<BuildingContact>(e =>
        {
            e.ToTable("modon_building_contacts");
            e.Property(x => x.Name).HasMaxLength(BuildingLicence.ShortText);
            e.Property(x => x.Email).HasMaxLength(BuildingLicence.ShortText);
            e.Property(x => x.Mobile).HasMaxLength(BuildingLicence.Phone);
            e.HasIndex(x => x.RequestId);
        });

        b.Entity<BuildingTender>(e =>
        {
            e.ToTable("modon_building_tenders");
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Number).HasMaxLength(40);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.OpenedByUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.RequestId, x.Stage });
            e.HasIndex(x => new { x.Stage, x.State });
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(x => x.RequestId);
            e.HasOne<Establishment>().WithMany().HasForeignKey(x => x.InvitedEstablishmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Quotations).WithOne().HasForeignKey(x => x.TenderId);
        });

        b.Entity<BuildingQuotation>(e =>
        {
            e.ToTable("modon_building_quotations");
            // One answer per office per round.
            e.HasIndex(x => new { x.TenderId, x.EstablishmentId }).IsUnique();
            e.HasIndex(x => x.RequestId);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SubmittedByUserId).HasMaxLength(450);
            e.Property(x => x.Comment).HasMaxLength(BuildingLicence.LongText);
            e.Property(x => x.AttachmentFileName).HasMaxLength(255);
            e.Property(x => x.AttachmentContentType).HasMaxLength(100);
            e.Property(x => x.AttachmentStorageKey).HasMaxLength(300);
            e.HasOne(x => x.Establishment).WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Phases, p =>
            {
                p.ToTable("modon_building_quotation_phases");
                p.WithOwner().HasForeignKey(x => x.QuotationId);
                p.HasKey(x => new { x.QuotationId, x.Phase });
                p.Property(x => x.Phase).HasConversion<string>().HasMaxLength(40);
                p.Property(x => x.SharePercent).HasPrecision(5, 2);
            });
        });

        b.Entity<ProgressReport>(e =>
        {
            e.ToTable("modon_building_progress_reports");
            // One report per calendar month per request.
            e.HasIndex(x => new { x.RequestId, x.PeriodYear, x.PeriodMonth }).IsUnique();
            e.Property(x => x.SubmittedByUserId).HasMaxLength(450);
            e.Property(x => x.Comment).HasMaxLength(BuildingLicence.LongText);
            e.Property(x => x.AttachmentFileName).HasMaxLength(255);
            e.Property(x => x.AttachmentContentType).HasMaxLength(100);
            e.Property(x => x.AttachmentStorageKey).HasMaxLength(300);
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(x => x.RequestId);
            e.HasOne(x => x.Establishment).WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Phases, p =>
            {
                p.ToTable("modon_building_progress_report_phases");
                p.WithOwner().HasForeignKey(x => x.ReportId);
                p.HasKey(x => new { x.ReportId, x.Phase });
                p.Property(x => x.Phase).HasConversion<string>().HasMaxLength(40);
                p.Property(x => x.CompletionPercent).HasPrecision(5, 2);
            });
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
