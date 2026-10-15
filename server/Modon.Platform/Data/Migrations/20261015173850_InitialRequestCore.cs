using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modon.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialRequestCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "modon_establishments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CrNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_establishments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "modon_establishment_members",
                columns: table => new
                {
                    EstablishmentId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_establishment_members", x => new { x.EstablishmentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_modon_establishment_members_modon_establishments_Establishm~",
                        column: x => x.EstablishmentId,
                        principalTable: "modon_establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_service_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ApplicantUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EstablishmentId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_service_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_service_requests_modon_establishments_EstablishmentId",
                        column: x => x.EstablishmentId,
                        principalTable: "modon_establishments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "modon_excavation_permit_details",
                columns: table => new
                {
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RequestTypeCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ContractTypeCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IndustrialCityCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PermitTypeCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    WorkDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExcavationMethodCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    WorkLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LengthM = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    WidthM = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    DepthM = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    TotalPathLengthM = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ProjectManagerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProjectManagerPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProjectManagerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupervisingEntity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_excavation_permit_details", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_modon_excavation_permit_details_modon_service_requests_Requ~",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_request_attachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    SlotCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_request_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_request_attachments_modon_service_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modon_request_status_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modon_request_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modon_request_status_history_modon_service_requests_Request~",
                        column: x => x.RequestId,
                        principalTable: "modon_service_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_modon_establishment_members_UserId",
                table: "modon_establishment_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_establishments_CrNumber",
                table: "modon_establishments",
                column: "CrNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_request_attachments_RequestId_SlotCode",
                table: "modon_request_attachments",
                columns: new[] { "RequestId", "SlotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_request_status_history_RequestId",
                table: "modon_request_status_history",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_service_requests_ApplicantUserId_CreatedAt",
                table: "modon_service_requests",
                columns: new[] { "ApplicantUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_modon_service_requests_EstablishmentId",
                table: "modon_service_requests",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_modon_service_requests_RequestNumber",
                table: "modon_service_requests",
                column: "RequestNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modon_establishment_members");

            migrationBuilder.DropTable(
                name: "modon_excavation_permit_details");

            migrationBuilder.DropTable(
                name: "modon_request_attachments");

            migrationBuilder.DropTable(
                name: "modon_request_status_history");

            migrationBuilder.DropTable(
                name: "modon_service_requests");

            migrationBuilder.DropTable(
                name: "modon_establishments");
        }
    }
}
