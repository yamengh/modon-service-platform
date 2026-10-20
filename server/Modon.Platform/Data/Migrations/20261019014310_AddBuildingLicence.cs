using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modon.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingLicence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsConsultingOffice",
                table: "modon_establishments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsContractor",
                table: "modon_establishments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "modon_building_licence_details",
                columns: table => new
                {
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    LicenceTypeCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ConstructionBudgetSar = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    FloorCount = table.Column<int>(type: "integer", nullable: true),
                    ScopeOfWork = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TermsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    SelectedConsultantEstablishmentId = table.Column<long>(type: "bigint", nullable: true),
                    SelectedContractorEstablishmentId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_licence_details", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_modon_building_licence_details_modon_service_requests_Reque~",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_progress_reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    EstablishmentId = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttachmentFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AttachmentContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttachmentSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    AttachmentStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_progress_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_building_progress_reports_modon_establishments_Establ~",
                        column: x => x.EstablishmentId,
                        principalTable: "modon_establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_modon_building_progress_reports_modon_service_requests_Requ~",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_tenders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    InvitedEstablishmentId = table.Column<long>(type: "bigint", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OpenedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_tenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_building_tenders_modon_establishments_InvitedEstablis~",
                        column: x => x.InvitedEstablishmentId,
                        principalTable: "modon_establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_modon_building_tenders_modon_service_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_contacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_building_contacts_modon_building_licence_details_Requ~",
                        column: x => x.RequestId,
                        principalTable: "modon_building_licence_details",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_progress_report_phases",
                columns: table => new
                {
                    ReportId = table.Column<long>(type: "bigint", nullable: false),
                    Phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CompletionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_progress_report_phases", x => new { x.ReportId, x.Phase });
                    table.ForeignKey(
                        name: "FK_modon_building_progress_report_phases_modon_building_progre~",
                        column: x => x.ReportId,
                        principalTable: "modon_building_progress_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_quotations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenderId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    EstablishmentId = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountSar = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CommitmentsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttachmentFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AttachmentContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttachmentSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    AttachmentStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_quotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_building_quotations_modon_building_tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "modon_building_tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_modon_building_quotations_modon_establishments_Establishmen~",
                        column: x => x.EstablishmentId,
                        principalTable: "modon_establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "modon_building_quotation_phases",
                columns: table => new
                {
                    QuotationId = table.Column<long>(type: "bigint", nullable: false),
                    Phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SharePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_building_quotation_phases", x => new { x.QuotationId, x.Phase });
                    table.ForeignKey(
                        name: "FK_modon_building_quotation_phases_modon_building_quotations_Q~",
                        column: x => x.QuotationId,
                        principalTable: "modon_building_quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_contacts_RequestId",
                table: "modon_building_contacts",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_progress_reports_EstablishmentId",
                table: "modon_building_progress_reports",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_progress_reports_RequestId_PeriodYear_Period~",
                table: "modon_building_progress_reports",
                columns: new[] { "RequestId", "PeriodYear", "PeriodMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_quotations_EstablishmentId",
                table: "modon_building_quotations",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_quotations_RequestId",
                table: "modon_building_quotations",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_quotations_TenderId_EstablishmentId",
                table: "modon_building_quotations",
                columns: new[] { "TenderId", "EstablishmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_tenders_InvitedEstablishmentId",
                table: "modon_building_tenders",
                column: "InvitedEstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_tenders_Number",
                table: "modon_building_tenders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_tenders_RequestId_Stage",
                table: "modon_building_tenders",
                columns: new[] { "RequestId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_modon_building_tenders_Stage_State",
                table: "modon_building_tenders",
                columns: new[] { "Stage", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modon_building_contacts");

            migrationBuilder.DropTable(
                name: "modon_building_progress_report_phases");

            migrationBuilder.DropTable(
                name: "modon_building_quotation_phases");

            migrationBuilder.DropTable(
                name: "modon_building_licence_details");

            migrationBuilder.DropTable(
                name: "modon_building_progress_reports");

            migrationBuilder.DropTable(
                name: "modon_building_quotations");

            migrationBuilder.DropTable(
                name: "modon_building_tenders");

            migrationBuilder.DropColumn(
                name: "IsConsultingOffice",
                table: "modon_establishments");

            migrationBuilder.DropColumn(
                name: "IsContractor",
                table: "modon_establishments");
        }
    }
}
