using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modon.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkflowCorrelationKey",
                table: "modon_service_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowDefinitionKey",
                table: "modon_service_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowDefinitionVersion",
                table: "modon_service_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowInstanceKey",
                table: "modon_service_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_modon_service_requests_WorkflowInstanceKey",
                table: "modon_service_requests",
                column: "WorkflowInstanceKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_modon_service_requests_WorkflowInstanceKey",
                table: "modon_service_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowCorrelationKey",
                table: "modon_service_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionKey",
                table: "modon_service_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionVersion",
                table: "modon_service_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceKey",
                table: "modon_service_requests");
        }
    }
}
