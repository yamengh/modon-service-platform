namespace Modon.Platform.Organizations;

/// <summary>
/// A company registered with Modon, identified by its commercial registration (السجل التجاري).
/// The office flags are set by seeding or (later) Modon staff only, never by the company's own users.
/// </summary>
public class Establishment
{
    public long Id { get; set; }
    public string CrNumber { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";

    /// <summary>A registered consulting office (مكتب استشاري): may quote for and report on building projects.</summary>
    public bool IsConsultingOffice { get; set; }

    /// <summary>A registered contracting company (مقاول): may quote for building projects.</summary>
    public bool IsContractor { get; set; }

    public List<EstablishmentMember> Members { get; set; } = [];
}

/// <summary>
/// Links a user to an establishment they may act for. Only seeding or (later) Modon staff create these,
/// so a user cannot attach themselves to someone else's commercial registration.
/// </summary>
public class EstablishmentMember
{
    public long EstablishmentId { get; set; }
    public string UserId { get; set; } = "";
    public Establishment? Establishment { get; set; }
}
