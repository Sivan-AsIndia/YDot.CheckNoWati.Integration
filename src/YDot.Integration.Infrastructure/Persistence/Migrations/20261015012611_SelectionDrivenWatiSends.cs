using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelectionDrivenWatiSends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "int_wati_messages",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_job_id",
                table: "int_wati_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_job_number",
                table: "int_wati_messages",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "excluded_json",
                table: "int_wati_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parameter_bindings_json",
                table: "int_wati_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_group_id",
                table: "int_wati_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_group_name",
                table: "int_wati_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requested_count",
                table: "int_wati_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "tags_json",
                table: "int_wati_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_body",
                table: "int_wati_jobs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_header_json",
                table: "int_wati_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "int_input_file_numbers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_source_job_id",
                table: "int_wati_messages",
                column: "source_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_recipient_group_id",
                table: "int_wati_jobs",
                column: "recipient_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_verified_pool",
                table: "int_input_file_numbers",
                columns: new[] { "tenant_id", "phone_number_id", "created_at_utc" },
                filter: "final_whatsapp_status = 'Yes'");

            // ---- Backfill what older rows can honestly say ----------------------------------------------
            //
            // An older AUTOMATIC message came from its own check, so that check is its source. An older
            // MANUAL message did not record where the number was picked from; its source stays empty
            // rather than being guessed.
            migrationBuilder.Sql("""
                UPDATE int_wati_messages m
                   SET source_job_id = m.integration_job_id,
                       source_job_number = j.job_number
                  FROM int_integration_jobs j
                 WHERE j.id = m.integration_job_id
                   AND m.source_job_id IS NULL;
                """);

            // An older send held exactly what it was asked to send (numbers left out were not kept).
            migrationBuilder.Sql("UPDATE int_wati_jobs SET requested_count = total_recipients WHERE requested_count = 0;");

            // ---- Column comments, so the schema explains itself to whoever reads it in SQL ---------------
            migrationBuilder.Sql("""
                COMMENT ON COLUMN int_input_file_numbers.contact_name IS 'Name the file gave beside the number (Name column of a headed sheet, else the first text cell or the words around the number). Used for {{name}} in templates.';
                COMMENT ON COLUMN int_wati_messages.source_job_id IS 'Check job (int_integration_jobs.id) the number was picked from - the job whose verified answer made it eligible. Soft link.';
                COMMENT ON COLUMN int_wati_messages.source_job_number IS 'That check job''s number (INT-yyyyMMdd-NNNNNN), denormalised for exports.';
                COMMENT ON COLUMN int_wati_messages.contact_name IS 'Contact name used for this message (bound template parameters such as {{name}}).';
                COMMENT ON COLUMN int_wati_jobs.template_body IS 'Template body with {{placeholders}} as it was when the send was created.';
                COMMENT ON COLUMN int_wati_jobs.template_header_json IS 'Template header (e.g. an image header with {{image_url}}) as it was when the send was created.';
                COMMENT ON COLUMN int_wati_jobs.parameter_bindings_json IS 'Template parameters filled per recipient: [{"name":"name","source":"contactName"}]. parameters_json holds the fallback values.';
                COMMENT ON COLUMN int_wati_jobs.requested_count IS 'Numbers the person selected for this send (message rows plus excluded_json).';
                COMMENT ON COLUMN int_wati_jobs.excluded_json IS 'Selected numbers that never became a message row: [{"number","reason","detail"}] (NOT_IN_POOL, INVALID_NUMBER, DUPLICATE_IN_JOB).';
                COMMENT ON COLUMN int_wati_jobs.recipient_group_id IS 'Recipient group (cam_whatsapp_recipient_groups.id) the selection came from. Soft link.';
                COMMENT ON COLUMN int_wati_jobs.recipient_group_name IS 'That group''s name when the send was created.';
                COMMENT ON COLUMN int_wati_jobs.tags_json IS 'Labels of the send: ["diwali","chennai"].';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_int_wati_messages_source_job_id",
                table: "int_wati_messages");

            migrationBuilder.DropIndex(
                name: "ix_int_wati_jobs_recipient_group_id",
                table: "int_wati_jobs");

            migrationBuilder.DropIndex(
                name: "ix_int_input_file_numbers_verified_pool",
                table: "int_input_file_numbers");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "int_wati_messages");

            migrationBuilder.DropColumn(
                name: "source_job_id",
                table: "int_wati_messages");

            migrationBuilder.DropColumn(
                name: "source_job_number",
                table: "int_wati_messages");

            migrationBuilder.DropColumn(
                name: "excluded_json",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "parameter_bindings_json",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "recipient_group_id",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "recipient_group_name",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "requested_count",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "tags_json",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "template_body",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "template_header_json",
                table: "int_wati_jobs");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "int_input_file_numbers");
        }
    }
}
