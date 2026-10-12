using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "int_api_call_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wati_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    request_body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    is_success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_api_call_logs", x => x.id);
                },
                comment: "One row per HTTP attempt to CheckNumber.ai, its result storage or WATI; secrets are never stored.");

            migrationBuilder.CreateTable(
                name: "int_input_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_code = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    tenant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    upload_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    file_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    duplicate_of_input_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stored_file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    campaign_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sheet_count = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_input_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_input_files_int_input_files_duplicate_of_input_file_id",
                        column: x => x.duplicate_of_input_file_id,
                        principalTable: "int_input_files",
                        principalColumn: "id");
                },
                comment: "Every mobile-number file that reached the worker inbox, including duplicates and rejected files.");

            migrationBuilder.CreateTable(
                name: "int_job_sequences",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_job_sequences", x => new { x.name, x.period });
                },
                comment: "Daily counters behind the human-readable job references.");

            migrationBuilder.CreateTable(
                name: "int_phone_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    digits = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    region_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    country_iso = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    country_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    whatsapp_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    whatsapp_checked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_check_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_checked_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    check_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_phone_numbers", x => x.id);
                },
                comment: "One row per distinct E.164 number with its latest definitive WhatsApp answer (the reuse cache).");

            migrationBuilder.CreateTable(
                name: "int_wati_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    header_json = table.Column<string>(type: "jsonb", nullable: true),
                    footer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    buttons_json = table.Column<string>(type: "jsonb", nullable: true),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_wati_templates", x => x.id);
                },
                comment: "WhatsApp templates copied from WATI (or declared in configuration) for the Wati page.");

            migrationBuilder.CreateTable(
                name: "int_worker_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    command_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_worker_commands", x => x.id);
                },
                comment: "Requests the YDot web application leaves for the worker: retry, cancel, manual broadcast, template refresh.");

            migrationBuilder.CreateTable(
                name: "int_worker_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    host_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    environment_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_worker_instances", x => x.id);
                },
                comment: "Running / stopped worker processes with heartbeat and effective non-secret settings.");

            migrationBuilder.CreateTable(
                name: "int_integration_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current_step = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    run_checknumber = table.Column<bool>(type: "boolean", nullable: false),
                    run_wati = table.Column<bool>(type: "boolean", nullable: false),
                    force_recheck = table.Column<bool>(type: "boolean", nullable: false),
                    wati_template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    wati_broadcast_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    wati_parameters_json = table.Column<string>(type: "jsonb", nullable: true),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    numbers_found = table.Column<int>(type: "integer", nullable: false),
                    unique_numbers = table.Column<int>(type: "integer", nullable: false),
                    fresh_numbers = table.Column<int>(type: "integer", nullable: false),
                    reused_numbers = table.Column<int>(type: "integer", nullable: false),
                    duplicate_numbers = table.Column<int>(type: "integer", nullable: false),
                    invalid_lines = table.Column<int>(type: "integer", nullable: false),
                    checked_numbers = table.Column<int>(type: "integer", nullable: false),
                    whatsapp_yes = table.Column<int>(type: "integer", nullable: false),
                    whatsapp_no = table.Column<int>(type: "integer", nullable: false),
                    whatsapp_unknown = table.Column<int>(type: "integer", nullable: false),
                    wati_eligible = table.Column<int>(type: "integer", nullable: false),
                    wati_sent = table.Column<int>(type: "integer", nullable: false),
                    wati_failed = table.Column<int>(type: "integer", nullable: false),
                    wati_skipped = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    actual_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cost_currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    outcome_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    processing_attempts = table.Column<int>(type: "integer", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    working_directory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    results_csv_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    summary_json_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_integration_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_integration_jobs_int_input_files_input_file_id",
                        column: x => x.input_file_id,
                        principalTable: "int_input_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One pipeline run (parse -> CheckNumber.ai -> verify -> WATI -> output) per input file.");

            migrationBuilder.CreateTable(
                name: "int_checknumber_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    is_superseded = table.Column<bool>(type: "boolean", nullable: false),
                    provider_task_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    task_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    submitted_count = table.Column<int>(type: "integer", nullable: false),
                    country_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    total = table.Column<int>(type: "integer", nullable: true),
                    success = table.Column<int>(type: "integer", nullable: true),
                    failure = table.Column<int>(type: "integer", nullable: true),
                    cached_count = table.Column<int>(type: "integer", nullable: true),
                    estimated_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    actual_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    result_url = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    provider_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_numbers_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    poll_count = table.Column<int>(type: "integer", nullable: false),
                    last_polled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_poll_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    create_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_checknumber_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_checknumber_tasks_integration_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "int_integration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "CheckNumber.ai batch tasks: task_id, status, counts, cost, result_url and polling state.");

            migrationBuilder.CreateTable(
                name: "int_job_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    worker_instance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_job_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_job_steps_int_integration_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "int_integration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Each execution of a pipeline step for a job: when, how long, outcome and error.");

            migrationBuilder.CreateTable(
                name: "int_wati_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    integration_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    template_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    broadcast_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    rendered_preview = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    channel_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    batch_size = table.Column<int>(type: "integer", nullable: false),
                    total_recipients = table.Column<int>(type: "integer", nullable: false),
                    queued_count = table.Column<int>(type: "integer", nullable: false),
                    accepted_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_wati_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_wati_jobs_int_integration_jobs_integration_job_id",
                        column: x => x.integration_job_id,
                        principalTable: "int_integration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "WATI send runs: template, broadcast, parameters, recipients and outcome counts.");

            migrationBuilder.CreateTable(
                name: "int_checknumber_result_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checknumber_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_task_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_url = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    download_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    download_attempts = table.Column<int>(type: "integer", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    local_zip_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    download_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    download_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    download_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    download_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    extraction_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extracted_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    extracted_file_count = table.Column<int>(type: "integer", nullable: true),
                    extracted_entries_json = table.Column<string>(type: "jsonb", nullable: true),
                    extracted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extraction_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result_source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    parsed_row_count = table.Column<int>(type: "integer", nullable: true),
                    matched_count = table.Column<int>(type: "integer", nullable: true),
                    rewritten_count = table.Column<int>(type: "integer", nullable: true),
                    missing_count = table.Column<int>(type: "integer", nullable: true),
                    unmatched_row_count = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_checknumber_result_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_checknumber_result_files_check_number_tasks_check_numbe",
                        column: x => x.checknumber_task_id,
                        principalTable: "int_checknumber_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Download (HTTP status, size, duration, path) and extraction (entries, errors) of a task's result archive.");

            migrationBuilder.CreateTable(
                name: "int_checknumber_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checknumber_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    returned_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    activated_raw = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    whatsapp_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    validation_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    checked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_checknumber_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_checknumber_results_check_number_tasks_check_number_tas",
                        column: x => x.checknumber_task_id,
                        principalTable: "int_checknumber_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_int_checknumber_results_phone_numbers_phone_number_id",
                        column: x => x.phone_number_id,
                        principalTable: "int_phone_numbers",
                        principalColumn: "id");
                },
                comment: "CheckNumber.ai answer for each submitted number: returned number, activated value, WhatsApp yes/no/unknown and why.");

            migrationBuilder.CreateTable(
                name: "int_checknumber_task_polls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checknumber_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_number = table.Column<int>(type: "integer", nullable: false),
                    polled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    provider_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    total = table.Column<int>(type: "integer", nullable: true),
                    success = table.Column<int>(type: "integer", nullable: true),
                    failure = table.Column<int>(type: "integer", nullable: true),
                    cached_count = table.Column<int>(type: "integer", nullable: true),
                    has_result_url = table.Column<bool>(type: "boolean", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_checknumber_task_polls", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_checknumber_task_polls_int_checknumber_tasks_check_numb",
                        column: x => x.checknumber_task_id,
                        principalTable: "int_checknumber_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every gettasks status check of a CheckNumber.ai task, with the counts it reported.");

            migrationBuilder.CreateTable(
                name: "int_input_file_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    sheet_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    raw_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phone_number_id = table.Column<Guid>(type: "uuid", nullable: true),
                    country_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    country_iso = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    country_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duplicate_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invalid_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reused_whatsapp_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    reused_checked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reused_from_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checknumber_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_whatsapp_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    check_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_input_file_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_input_file_numbers_int_checknumber_tasks_checknumber_ta",
                        column: x => x.checknumber_task_id,
                        principalTable: "int_checknumber_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_int_input_file_numbers_int_input_file_numbers_duplicate_of_",
                        column: x => x.duplicate_of_id,
                        principalTable: "int_input_file_numbers",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_int_input_file_numbers_int_input_files_input_file_id",
                        column: x => x.input_file_id,
                        principalTable: "int_input_files",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_int_input_file_numbers_int_integration_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "int_integration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_int_input_file_numbers_phone_numbers_phone_number_id",
                        column: x => x.phone_number_id,
                        principalTable: "int_phone_numbers",
                        principalColumn: "id");
                },
                comment: "Every occurrence found in an input file: original text, normalised number, classification and the answer the job used.");

            migrationBuilder.CreateTable(
                name: "int_job_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wati_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    step_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: true),
                    worker_instance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_job_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_job_events_int_input_files_input_file_id",
                        column: x => x.input_file_id,
                        principalTable: "int_input_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_int_job_events_int_integration_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "int_integration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_int_job_events_wati_jobs_wati_job_id",
                        column: x => x.wati_job_id,
                        principalTable: "int_wati_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Append-only narrative: file registered, numbers reused, task created, retries, batches, completion.");

            migrationBuilder.CreateTable(
                name: "int_wati_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wati_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    accepted_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_count = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    request_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    response_body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_wati_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_wati_batches_wati_jobs_wati_job_id",
                        column: x => x.wati_job_id,
                        principalTable: "int_wati_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Each sendTemplateMessages HTTP call: request, response, HTTP status, attempts and timing.");

            migrationBuilder.CreateTable(
                name: "int_wati_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wati_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wati_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    integration_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checknumber_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checknumber_provider_task_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    phone_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    whatsapp_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    whatsapp_check = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    broadcast_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    request_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    wati_response_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_valid_whatsapp_number = table.Column<bool>(type: "boolean", nullable: true),
                    local_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    wati_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    whatsapp_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    conversation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    delivery_status_raw = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    failed_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    failed_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reply_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    queued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_times_observed = table.Column<bool>(type: "boolean", nullable: false),
                    status_check_count = table.Column<int>(type: "integer", nullable: false),
                    last_status_check_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_status_check_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_int_wati_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_int_wati_messages_int_phone_numbers_phone_number_id",
                        column: x => x.phone_number_id,
                        principalTable: "int_phone_numbers",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_int_wati_messages_int_wati_batches_wati_batch_id",
                        column: x => x.wati_batch_id,
                        principalTable: "int_wati_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_int_wati_messages_int_wati_jobs_wati_job_id",
                        column: x => x.wati_job_id,
                        principalTable: "int_wati_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One WhatsApp template message per number (or the reason it was skipped), followed to sent / delivered / read.");

            migrationBuilder.CreateIndex(
                name: "ix_int_api_call_logs_job_id",
                table: "int_api_call_logs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_api_call_logs_provider_operation_started_at_utc",
                table: "int_api_call_logs",
                columns: new[] { "provider", "operation", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_api_call_logs_started_at_utc",
                table: "int_api_call_logs",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_int_api_call_logs_wati_job_id",
                table: "int_api_call_logs",
                column: "wati_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_result_files_check_number_task_id",
                table: "int_checknumber_result_files",
                column: "checknumber_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_result_files_job_id",
                table: "int_checknumber_result_files",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_results_checknumber_task_id_phone_number_id",
                table: "int_checknumber_results",
                columns: new[] { "checknumber_task_id", "phone_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_results_job_id",
                table: "int_checknumber_results",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_results_phone_number_id",
                table: "int_checknumber_results",
                column: "phone_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_results_tenant_id_phone_number_id_checked_a",
                table: "int_checknumber_results",
                columns: new[] { "tenant_id", "phone_number_id", "checked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_task_polls_checknumber_task_id_poll_number",
                table: "int_checknumber_task_polls",
                columns: new[] { "checknumber_task_id", "poll_number" });

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_task_polls_job_id",
                table: "int_checknumber_task_polls",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_tasks_job_id_sequence",
                table: "int_checknumber_tasks",
                columns: new[] { "job_id", "sequence" },
                unique: true,
                filter: "is_superseded = false");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_tasks_provider_task_id",
                table: "int_checknumber_tasks",
                column: "provider_task_id",
                unique: true,
                filter: "provider_task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_int_checknumber_tasks_state_next_poll_at_utc",
                table: "int_checknumber_tasks",
                columns: new[] { "state", "next_poll_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_checknumber_task_id",
                table: "int_input_file_numbers",
                column: "checknumber_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_duplicate_of_id",
                table: "int_input_file_numbers",
                column: "duplicate_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_input_file_id",
                table: "int_input_file_numbers",
                column: "input_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_job_id_classification",
                table: "int_input_file_numbers",
                columns: new[] { "job_id", "classification" });

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_job_id_sequence",
                table: "int_input_file_numbers",
                columns: new[] { "job_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_phone_number_id",
                table: "int_input_file_numbers",
                column: "phone_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_file_numbers_tenant_id_normalized_number",
                table: "int_input_file_numbers",
                columns: new[] { "tenant_id", "normalized_number" });

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_campaign_id",
                table: "int_input_files",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_content_sha256",
                table: "int_input_files",
                column: "content_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_dedupe_key",
                table: "int_input_files",
                column: "dedupe_key",
                unique: true,
                filter: "dedupe_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_duplicate_of_input_file_id",
                table: "int_input_files",
                column: "duplicate_of_input_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_tenant_id_received_at_utc",
                table: "int_input_files",
                columns: new[] { "tenant_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_input_files_upload_reference",
                table: "int_input_files",
                column: "upload_reference",
                unique: true,
                filter: "upload_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_int_integration_jobs_campaign_id",
                table: "int_integration_jobs",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_integration_jobs_input_file_id",
                table: "int_integration_jobs",
                column: "input_file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_integration_jobs_job_number",
                table: "int_integration_jobs",
                column: "job_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_integration_jobs_status_next_attempt_at_utc",
                table: "int_integration_jobs",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_integration_jobs_tenant_id_created_at_utc",
                table: "int_integration_jobs",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_events_event_code",
                table: "int_job_events",
                column: "event_code");

            migrationBuilder.CreateIndex(
                name: "ix_int_job_events_input_file_id_occurred_at_utc",
                table: "int_job_events",
                columns: new[] { "input_file_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_events_job_id_occurred_at_utc",
                table: "int_job_events",
                columns: new[] { "job_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_events_tenant_id_occurred_at_utc",
                table: "int_job_events",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_events_wati_job_id_occurred_at_utc",
                table: "int_job_events",
                columns: new[] { "wati_job_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_steps_job_id_started_at_utc",
                table: "int_job_steps",
                columns: new[] { "job_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_job_steps_job_id_step_code",
                table: "int_job_steps",
                columns: new[] { "job_id", "step_code" });

            migrationBuilder.CreateIndex(
                name: "ix_int_phone_numbers_digits",
                table: "int_phone_numbers",
                column: "digits");

            migrationBuilder.CreateIndex(
                name: "ix_int_phone_numbers_e164",
                table: "int_phone_numbers",
                column: "e164",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_phone_numbers_whatsapp_status",
                table: "int_phone_numbers",
                column: "whatsapp_status");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_batches_status",
                table: "int_wati_batches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_batches_wati_job_id_batch_number",
                table: "int_wati_batches",
                columns: new[] { "wati_job_id", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_campaign_id",
                table: "int_wati_jobs",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_integration_job_id",
                table: "int_wati_jobs",
                column: "integration_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_reference",
                table: "int_wati_jobs",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_status_next_attempt_at_utc",
                table: "int_wati_jobs",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_jobs_tenant_id_created_at_utc",
                table: "int_wati_jobs",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_campaign_id",
                table: "int_wati_messages",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_integration_job_id",
                table: "int_wati_messages",
                column: "integration_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_local_message_id",
                table: "int_wati_messages",
                column: "local_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_phone_number_id",
                table: "int_wati_messages",
                column: "phone_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_status_next_status_check_at_utc",
                table: "int_wati_messages",
                columns: new[] { "status", "next_status_check_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_tenant_id_phone_number_id_template_name",
                table: "int_wati_messages",
                columns: new[] { "tenant_id", "phone_number_id", "template_name" });

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_tenant_id_status",
                table: "int_wati_messages",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_wati_batch_id",
                table: "int_wati_messages",
                column: "wati_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_messages_wati_job_id_phone_number_id",
                table: "int_wati_messages",
                columns: new[] { "wati_job_id", "phone_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_wati_templates_element_name",
                table: "int_wati_templates",
                column: "element_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_worker_commands_reference",
                table: "int_worker_commands",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_int_worker_commands_status_requested_at_utc",
                table: "int_worker_commands",
                columns: new[] { "status", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_worker_commands_tenant_id_requested_at_utc",
                table: "int_worker_commands",
                columns: new[] { "tenant_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_int_worker_instances_last_heartbeat_at_utc",
                table: "int_worker_instances",
                column: "last_heartbeat_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "int_api_call_logs");

            migrationBuilder.DropTable(
                name: "int_checknumber_result_files");

            migrationBuilder.DropTable(
                name: "int_checknumber_results");

            migrationBuilder.DropTable(
                name: "int_checknumber_task_polls");

            migrationBuilder.DropTable(
                name: "int_input_file_numbers");

            migrationBuilder.DropTable(
                name: "int_job_events");

            migrationBuilder.DropTable(
                name: "int_job_sequences");

            migrationBuilder.DropTable(
                name: "int_job_steps");

            migrationBuilder.DropTable(
                name: "int_wati_messages");

            migrationBuilder.DropTable(
                name: "int_wati_templates");

            migrationBuilder.DropTable(
                name: "int_worker_commands");

            migrationBuilder.DropTable(
                name: "int_worker_instances");

            migrationBuilder.DropTable(
                name: "int_checknumber_tasks");

            migrationBuilder.DropTable(
                name: "int_phone_numbers");

            migrationBuilder.DropTable(
                name: "int_wati_batches");

            migrationBuilder.DropTable(
                name: "int_wati_jobs");

            migrationBuilder.DropTable(
                name: "int_integration_jobs");

            migrationBuilder.DropTable(
                name: "int_input_files");
        }
    }
}
