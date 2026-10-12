# YDot.Integration

A .NET 10 Worker Service that takes a file of mobile numbers, finds out which of them are on
WhatsApp (CheckNumber.ai), sends a WhatsApp template message to those that are (WATI), and
records every step in PostgreSQL.

```
number file ──► saved to the database ──► already checked? ──► CheckNumber.ai ──► result file
                                                │ yes (not paid for again)            │
                                                ▼                                     ▼
                              completed output ◄── WATI template message ◄── numbers on WhatsApp
```

It runs beside the YDot web application and shares its database (`ydotphaseupdated`). The web
pages `/app/whatsapp/check-number` and `/app/whatsapp/wati` show what this worker records.

---

## Contents

1. [What it does, in one page](#1-what-it-does-in-one-page)
2. [Run it in five minutes](#2-run-it-in-five-minutes)
3. [How a file becomes a job](#3-how-a-file-becomes-a-job)
4. [Where the code is](#4-where-the-code-is)
5. [Settings](#5-settings)
6. [Ways to run it](#6-ways-to-run-it)
7. [The database](#7-the-database)
8. [Never paying twice](#8-never-paying-twice)
9. [Surviving restarts and failures](#9-surviving-restarts-and-failures)
10. [Retries](#10-retries)
11. [CheckNumber.ai, as it actually behaves](#11-checknumberai-as-it-actually-behaves)
12. [WATI](#12-wati)
13. [The web application](#13-the-web-application)
14. [Watching it work](#14-watching-it-work)
15. [For QA: what to test and what to expect](#15-for-qa-what-to-test-and-what-to-expect)
16. [What was verified, and what was not](#16-what-was-verified-and-what-was-not)
17. [Troubleshooting](#17-troubleshooting)
18. [Known limits and next steps](#18-known-limits-and-next-steps)
19. [Words used here](#19-words-used-here)

---

## 1. What it does, in one page

| Step | What happens | Where it is recorded |
|---|---|---|
| 1. A file arrives | A `.txt`, `.csv`, `.xls` or `.xlsx` file appears in the inbox folder - uploaded on the CheckNumber page, or copied there by hand. | `int_input_files` |
| 2. It is registered | The worker waits until the file is complete, moves it out of the inbox (so no second worker can take it), hashes it, and creates a job. The same file for the same organisation and campaign is refused as a duplicate. | `int_integration_jobs` |
| 3. It is read | Every line is scanned for phone numbers. Each is normalised to `+<country><number>`; the original text is kept. Repeats inside the file are marked duplicate; lines with no usable number are marked invalid, with the reason. | `int_input_file_numbers`, `int_phone_numbers` |
| 4. Saved answers are reused | A number that already has a Yes or No answer is **not** sent to CheckNumber.ai again. | `reused_whatsapp_status` on the number |
| 5. The rest is checked | The new numbers go to CheckNumber.ai as one task. The task id is stored before anything else happens. | `int_checknumber_tasks` |
| 6. The task is polled | Every 30 seconds (configurable) until its status is `exported`. Between polls the job is parked; the worker is free for other jobs. | `int_checknumber_task_polls` |
| 7. The result is fetched | The result archive is downloaded, unpacked and read. | `int_checknumber_result_files`, `int_checknumber_results` |
| 8. Verified numbers are picked | Every number now has a final answer: Yes, No or Unknown. | `final_whatsapp_status` |
| 9. WATI sends | A template message goes to the numbers on WhatsApp, in batches. Everybody left out gets a reason. | `int_wati_jobs`, `int_wati_batches`, `int_wati_messages` |
| 10. Output is written | A results CSV and a summary JSON, per job. | `data/output/{job}/` |

Everything the worker does is also written as a step (`int_job_steps`), as an event
(`int_job_events`) and, for every HTTP call, as a request/response record (`int_api_call_logs`).

**Each part can be switched off.** `Pipeline:EnableCheckNumber` and `Pipeline:EnableWati` decide
which stages run - check only, check then message, or check now and message later from the Wati
page. See [Settings](#5-settings).

---

## 2. Run it in five minutes

You need the .NET 10 SDK and the YDot Docker stack running (it provides PostgreSQL on
`localhost:6710`).

```powershell
cd D:\Permaspace\Ydot-Goldwin-Latest_Wati\YDot.Integration

# 1. Secrets. Copy the template and fill in the CheckNumber.ai key and the WATI values.
copy .env.example .env
notepad .env

# 2. Run. The worker creates its own tables (int_*) on first start.
cd src\YDot.Integration.Worker
dotnet run
```

You should see:

```
YDot.Integration worker starting (Development). Settings file .env: D:\...\YDot.Integration\.env
Integration database schema (int_*) is up to date.
Job processor started: up to 2 job(s) at a time, checking every 5s.
Drop number files (.txt, .csv, .xls, .xlsx) into D:\...\YDot.Integration\data\inbox - or into ...\inbox/{organisation-subdomain}/.
```

Now give it a file. A file copied in by hand has to say which organisation it belongs to, and
it does that by the folder it is put in:

```powershell
mkdir data\inbox\smilefoundation
copy D:\Permaspace\Phase3\samples\WAChecker2.txt data\inbox\smilefoundation\
```

Within about ten seconds the log shows the file registered as job `INT-<date>-000001`, read,
sent to CheckNumber.ai, polled, downloaded and finished. The results are in
`data\output\INT-<date>-000001\whatsapp-results.csv`.

> **Before you run it with real numbers, read [Never paying twice](#8-never-paying-twice) and the
> WATI safety settings in [WATI](#12-wati).** The `.env` template ships with a test allow-list:
> while it is set, only the listed numbers can ever be messaged.

---

## 3. How a file becomes a job

### 3.1 The folders

Everything lives under one data folder (`Worker:DataRoot`; `data/` at the solution root in
development, `/app/data` in Docker).

```
data/
├── inbox/                              files waiting to be picked up
│   ├── UPL-20261010-PE5KJ3.xlsx            a web upload ...
│   ├── UPL-20261010-PE5KJ3.manifest.json   ... and the file that says whose it is
│   └── smilefoundation/                    files copied in by hand, per organisation
├── files/2026/10/{file-id}/            the original, kept for good, with claim.json
├── checknumber/{job}/                  submitted-numbers.txt, result.zip, extracted/
├── output/{job}/                       whatsapp-results.csv, summary.json
└── rejected/2026/10/{file-id}/         duplicates and refused files, with reason.txt
```

### 3.2 Two ways in

| | Web upload | Folder drop |
|---|---|---|
| Who | A person on `/app/whatsapp/check-number` | An operator, a script, an SFTP job |
| Organisation | From the manifest (the signed-in person's organisation) | From the folder: `inbox/{subdomain}/`, or `Worker:DefaultTenantSubdomain` |
| Campaign, uploader | From the manifest | None |
| "Is it complete?" | Yes the moment it appears - the web application writes it under a hidden name and renames it | Only after its size and time stayed unchanged for `Worker:FileStabilitySeconds` and it can be opened exclusively |

Both take exactly the same path from there on. Folder drops can be switched off with
`Worker:AcceptFolderDrops`.

### 3.3 The job's states

```
Queued ─► Parsing ─► CheckingNumbers ─► Verifying ─► SendingWati ─► GeneratingOutput ─► Completed
                                                                                    └─► CompletedWithErrors
        any state ─► Failed        (can be retried)
        any state ─► Cancelled     (can be retried)
```

`CompletedWithErrors` means the check finished but WATI turned the messages down. It can be
retried too; only the failed messages are tried again.

### 3.4 Phone numbers

The rules are the same ones the upload page applies in the browser, so the review a person sees
before starting and the job's own counts always agree:

| Written in the file | Stored as |
|---|---|
| `9876543210` | `+919876543210` (ten digits starting 6-9 are Indian mobiles) |
| `098765 43210` | `+919876543210` |
| `Ravi 98765-43210` | `+919876543210` |
| `+44 7700 900123` | `+447700900123` |
| `12345` | invalid: "Too short (5 digits)" |

Both the original text (`raw_value`) and the normalised number (`normalized_number`) are stored.

---

## 4. Where the code is

Four projects, each depending only on the ones above it.

```
src/
├── YDot.Integration.Domain          entities, enums, the constants (step, error, skip, event codes)
├── YDot.Integration.Application     the logic: pipeline stages, WATI sending, retry, phone numbers
├── YDot.Integration.Infrastructure  PostgreSQL (EF Core), the HTTP clients, the file system
└── YDot.Integration.Worker          Program.cs, the seven background services, appsettings
```

| If you want to change... | Look in |
|---|---|
| How numbers are recognised | `Application/PhoneNumbers/` |
| What happens to a job at each stage | `Application/Pipeline/Stages/` (`ParseStage`, `CheckNumberStage`, `VerifyStage`, `WatiStage`, `OutputStage`) |
| Who gets a WATI message | `Application/Wati/WatiRecipientPlanner.cs` |
| How batches are sent | `Application/Wati/WatiJobSender.cs` |
| The retry rule | `Application/Retry/RetryExecutor.cs`, `Application/Common/TransientErrors.cs` |
| The CheckNumber.ai and WATI requests | `Infrastructure/ExternalApis/` |
| A table or a column | `Domain/Entities/`, `Infrastructure/Persistence/Configurations/`, then add a migration |
| A setting | `Application/Options/` and `Worker/appsettings.json` |

The seven background services (`Worker/Services/`):

| Service | Every | Does |
|---|---|---|
| `InboxScannerWorker` | 10 s | Finds new files, registers them as jobs |
| `JobProcessorWorker` | 5 s | Moves jobs through their stages (up to 2 at once) |
| `WatiDispatchWorker` | 5 s | Sends queued WATI batches |
| `WatiStatusSyncWorker` | 30 s | Asks WATI whether accepted messages were sent, delivered, read |
| `WatiTemplateSyncWorker` | 60 min | Copies the approved template list from WATI |
| `CommandWorker` | 3 s | Carries out requests from the web pages (retry, stop, broadcast) |
| `HeartbeatWorker` | 30 s | Says "I am alive" and publishes its non-secret settings |

There are no unit or integration test projects yet. That was a deliberate decision for this
phase; see [Known limits](#18-known-limits-and-next-steps).

---

## 5. Settings

Defaults are in `src/YDot.Integration.Worker/appsettings.json`, each with a comment. Any of them
can be overridden by an environment variable or a line in `.env`, using a double underscore for
the section: `Wati:BatchSize` becomes `Wati__BatchSize=50`.

### 5.1 `.env` - secrets and per-machine values

`.env` sits at the solution root and is git-ignored. `.env.example` is the template.

| Key | What it is |
|---|---|
| `ConnectionStrings__IntegrationDatabase` | The `ydotphaseupdated` database. From your machine: `Host=localhost;Port=6710;...`. Inside Docker the compose file sets `Host=ydot-postgres;Port=5432;...`. |
| `CheckNumber__ApiKey` | The CheckNumber.ai `X-API-Key` |
| `Wati__TenantId`, `Wati__ApiToken`, `Wati__ChannelNumber` | The WATI account, its bearer token and the sending number - see [12.0](#120-the-connection-details) for where to find each |
| `Wati__AllowedNumbers__0`, `__1`, ... | **Test safety.** While any are set, only these numbers are ever messaged |
| `Wati__MaxRecipientsPerJob` | **Test safety.** The most messages one WATI job may send (0 = no limit) |

A variable already set in the environment always wins over `.env`.

### 5.2 Which stages run - `Pipeline`

| Setting | Default | Effect |
|---|---|---|
| `EnableCheckNumber` | `true` | `false`: numbers are read and saved, nothing is sent to CheckNumber.ai |
| `EnableWati` | `true` | `false`: nothing is ever sent to WATI |
| `AutoSendWati` | `true` | `false`: the check finishes and stops; messages are sent only from the Wati page |
| `AllowManualWatiBroadcasts` | `true` | `false`: the Wati page cannot send |

### 5.3 `CheckNumber`

| Setting | Default | Meaning |
|---|---|---|
| `TaskType` | `ws` | `ws` = is the number on WhatsApp |
| `PollingIntervalSeconds` | `30` | Wait between polls of a task |
| `InitialPollDelaySeconds` | `30` | Wait before the first poll |
| `MaxPollingAttempts` | `20` | After this many polls the job fails with `CHECKNUMBER_POLL_TIMEOUT` (retrying it polls the **same** task again) |
| `PollingBackoffMultiplier` | `1.0` | Above 1, each wait grows, up to `MaxPollingIntervalSeconds` |
| `MaxNumbersPerTask` | `50000` | A larger file is split into several tasks |
| `SplitTasksByCountry` | `false` | `true`: one task per country. See [section 11](#11-checknumberai-as-it-actually-behaves) |
| `ReuseScope` | `Global` | `Global`: a saved answer is reused for every organisation. `Tenant`: only for the organisation that paid for it |
| `ResultValidityDays` | `0` | Above 0, an answer older than this is checked again |
| `PricePerNumberUsd`, `MinimumChargePerTaskUsd` | `0.00009`, `0.045` | Used only for the estimate shown before a check. The bill is whatever CheckNumber.ai reports |

### 5.4 `Wati`

| Setting | Default | Meaning |
|---|---|---|
| `BatchSize` | `100` | Messages per request. 430 numbers go as 100, 100, 100, 100, 30 |
| `DelayBetweenBatchesMilliseconds` | `1000` | Pause between batches |
| `DefaultTemplateName`, `DefaultBroadcastName`, `DefaultParameters` | `l4l_001`, `l4l_001`, `image_url` | Used when an upload does not name its own |
| `MaxRecipientsPerJob` | `5` | Safety cap. **Raise it on purpose for production** |
| `AllowedNumbers` | empty | Test allow-list |
| `SkipAlreadyMessagedNumbers` | `true` | The same template is not sent twice to the same number for the same campaign |
| `SendToUnverifiedNumbers` | `false` | `true`: also message numbers CheckNumber.ai could not verify |
| `ResendWhenDeliveryUnknown` | `false` | See [section 9](#9-surviving-restarts-and-failures) |
| `Templates` | one entry | Templates offered while WATI's own list cannot be read |

### 5.5 `Worker`, `Retry`, `Database`, `Output`

| Setting | Default | Meaning |
|---|---|---|
| `Worker:DataRoot` | `data` | The data folder |
| `Worker:ScanIntervalSeconds` | `10` | How often the inbox is scanned |
| `Worker:FileStabilitySeconds` | `3` | How long a hand-copied file must stay unchanged |
| `Worker:MaxFileSizeMegabytes` | `10` | Larger files are refused |
| `Worker:MaxConcurrentJobs` | `2` | Jobs worked on at the same time |
| `Worker:JobLeaseSeconds` | `300` | How long a job stays claimed by an instance that stopped answering |
| `Worker:MaxConsecutiveStageFailures` | `10` | Temporary failures in a row before a job is failed |
| `Retry:MaxAttempts` / `InitialDelaySeconds` / `MaxDelaySeconds` | `3` / `2` / `60` | See [Retries](#10-retries) |
| `Database:ApplyMigrationsOnStartup` | `true` | Create and upgrade the `int_*` tables on start |
| `Output:WriteResultsCsv`, `WriteSummaryJson` | `true` | The two output files |

Settings are checked when the worker starts. A wrong one stops it at once with a sentence
saying which, for example `CheckNumber:BaseUrl must be an absolute https URL`.

---

## 6. Ways to run it

### 6.1 From source (development)

```powershell
cd src\YDot.Integration.Worker
dotnet run
```

Uses `appsettings.Development.json`: the data folder is `data/` at the solution root and the
worker's own log lines are at Debug level.

### 6.2 Docker, beside the YDot stack

```powershell
cd D:\Permaspace\Ydot-Goldwin-Latest
docker compose up -d                    # the YDot stack first: it creates the network and the volume

cd D:\Permaspace\Ydot-Goldwin-Latest_Wati\YDot.Integration
docker compose up -d --build
docker logs -f ydot-integration-worker
```

The worker's compose file joins two things the YDot stack creates:

| | Name | Why |
|---|---|---|
| Network | `ydot_ydot-network` | To reach PostgreSQL as `ydot-postgres:5432` |
| Volume | `ydot_ydot-integration-data` | The data folder. The worker mounts it as `/app/data`; the Campaign API mounts the same volume as `/var/ydot/integration` and writes uploads into its `inbox/` |

The container restarts by itself (`restart: unless-stopped`), including after the machine does.

### 6.3 As a Windows service

```powershell
dotnet publish src\YDot.Integration.Worker -c Release -o C:\Services\YDotIntegration
copy .env C:\Services\YDotIntegration\

sc.exe create "YDot Integration Worker" binPath= "C:\Services\YDotIntegration\YDot.Integration.Worker.exe" start= auto
sc.exe start  "YDot Integration Worker"
```

As a service it uses its installation folder for `data/` and `logs/` (not `C:\Windows\System32`).

### 6.4 Worker from source, web application in Docker

The two must see the same data folder. Add one line to the YDot stack's `.env` and recreate the
Campaign container:

```
WHATSAPP_INTEGRATION_DATA=D:/Permaspace/Ydot-Goldwin-Latest_Wati/YDot.Integration/data
```

```powershell
docker compose up -d --no-deps ydot-cam
```

### 6.5 More than one instance

Start as many as you like against the same database and data folder. A job, a WATI job, a
request and an inbox file are each taken by exactly one instance; see
[section 9](#9-surviving-restarts-and-failures).

---

## 7. The database

The worker owns every table starting `int_`, and its own migration history table
(`__ef_migrations_history_int`). It does not touch the web application's tables, except to read
organisation names from `iam_tenants`.

### 7.1 The tables

**The file and its job**

| Table | One row per | Notable columns |
|---|---|---|
| `int_input_files` | File that arrived | `content_sha256`, `dedupe_key` (unique), `status` (Registered / Duplicate / Rejected), `campaign_id`, `uploaded_by_name`, `stored_file_path` |
| `int_integration_jobs` | Registered file | `job_number`, `status`, `current_step`, the counts (`fresh_numbers`, `reused_numbers`, `whatsapp_yes`...), `estimated_cost`, `actual_cost`, `error_code`, `locked_by`, `locked_until_utc`, `next_attempt_at_utc` |
| `int_input_file_numbers` | Number (or bad line) found in the file | `raw_value`, `normalized_number`, `classification` (Fresh / AlreadyChecked / Duplicate / Invalid), `invalid_reason`, `reused_whatsapp_status`, `final_whatsapp_status`, `result_note` |

**The registry**

| Table | One row per | Notable columns |
|---|---|---|
| `int_phone_numbers` | Distinct number ever seen | `e164` (unique), `whatsapp_status`, `whatsapp_checked_at_utc`, `check_count` |

**CheckNumber.ai**

| Table | One row per | Notable columns |
|---|---|---|
| `int_checknumber_tasks` | Task | `provider_task_id` (unique), `state`, `provider_status`, `poll_count`, `estimated_amount`, `actual_amount`, `is_superseded` |
| `int_checknumber_task_polls` | Poll | `poll_number`, `provider_status`, `http_status_code`, `duration_ms` |
| `int_checknumber_result_files` | Downloaded archive | `result_url` (signature removed), `http_status_code`, `file_size_bytes`, `download_duration_ms`, `local_zip_path`, `extraction_status`, `extraction_error` |
| `int_checknumber_results` | Answer for one number | `submitted_number`, `returned_number`, `whatsapp_status`, `validation_status`, `note` |

**WATI**

| Table | One row per | Notable columns |
|---|---|---|
| `int_wati_jobs` | Broadcast | `reference`, `source` (Automatic / Manual), `integration_job_id`, `campaign_id`, `template_name`, `broadcast_name`, `parameters_json`, the counts |
| `int_wati_batches` | Request to WATI | `batch_number`, `status`, `http_status_code`, `request_payload_json`, `response_body`, `duration_ms` |
| `int_wati_messages` | Number in a broadcast | `whatsapp_number`, `status`, `skip_reason`, `request_status`, `local_message_id` (unique), `wati_response_json`, `retry_count`, `sent_at_utc`, `delivered_at_utc`, `read_at_utc` |
| `int_wati_templates` | Template | `element_name`, `body`, `parameters_json`, `source` (Wati / Configuration) |

**Audit and operations**

| Table | One row per | Notable columns |
|---|---|---|
| `int_job_steps` | Step attempt | `step_code`, `status`, `attempt`, `duration_ms`, `message`, `error_message` |
| `int_job_events` | Thing that happened | `event_code`, `level`, `message`, `data_json` |
| `int_api_call_logs` | HTTP call | `provider`, `operation`, `url`, `response_status_code`, `duration_ms`, `attempt` |
| `int_worker_instances` | Worker process | `status` (Running / Stopped / Lost), `last_heartbeat_at_utc`, `settings_json` |
| `int_worker_commands` | Request from the web pages | `command_type`, `status`, `payload_json`, `result_message` |
| `int_job_sequences` | Day | The counter behind `INT-20261010-000001` |

### 7.2 How they join

```
int_input_files 1───1 int_integration_jobs 1───* int_input_file_numbers *───1 int_phone_numbers
                              │                                                     │
                              ├───* int_checknumber_tasks 1───* int_checknumber_results
                              │            ├───* int_checknumber_task_polls
                              │            └───1 int_checknumber_result_files
                              │
                              ├───* int_wati_jobs 1───* int_wati_batches
                              │            └────────* int_wati_messages *───1 int_phone_numbers
                              │
                              ├───* int_job_steps
                              ├───* int_job_events
                              └───* int_api_call_logs
```

**The campaign link.** `campaign_id` is on the file, the job, the WATI job and every WATI
message. That is what lets the campaign's page show its own checks and broadcasts.

### 7.3 Template parameters need no schema change

A template's values are a JSON list in a `jsonb` column:

```json
[{ "name": "image_url", "value": "https://example.org/picture.jpg" },
 { "name": "donor_name", "value": "Asha" }]
```

A new template with new parameters is a new row, not a new column.

### 7.4 Queries worth keeping

```sql
-- What happened to a job, step by step
select step_code, status, attempt, duration_ms, message, error_message
from int_job_steps
where job_id = (select id from int_integration_jobs where job_number = 'INT-20261010-000001')
order by started_at_utc;

-- Every call made to CheckNumber.ai and WATI for it
select provider, operation, response_status_code, duration_ms, attempt, error_message
from int_api_call_logs
where job_id = (select id from int_integration_jobs where job_number = 'INT-20261010-000001')
order by started_at_utc;

-- Why each number was or was not messaged
select status, coalesce(skip_reason, failed_code) as reason, count(*)
from int_wati_messages
where integration_job_id = (select id from int_integration_jobs where job_number = 'INT-20261010-000001')
group by 1, 2;

-- What CheckNumber.ai has cost
select date_trunc('day', created_at_utc) as day, count(*) as tasks, sum(actual_amount) as usd
from int_checknumber_tasks where not is_superseded group by 1 order by 1;
```

### 7.5 Changing the schema

```powershell
$env:YDOT_INTEGRATION_CONNECTION = "Host=localhost;Port=6710;Database=ydotphaseupdated;Username=postgres;Password=user"
dotnet ef migrations add <Name> --project src\YDot.Integration.Infrastructure --startup-project src\YDot.Integration.Worker
```

The migration is applied the next time the worker starts. Do not pass `--namespace`; it puts the
model snapshot in the wrong folder.

---

## 8. Never paying twice

CheckNumber.ai charges per number, with a minimum per task (see
[section 11](#11-checknumberai-as-it-actually-behaves)). Four rules keep the bill down:

| Rule | How |
|---|---|
| **A number with a saved answer is not checked again** | Before a task is created, each number is looked up in `int_phone_numbers`. A saved Yes or No is reused (`classification = AlreadyChecked`). An Unknown is not an answer, so it is checked again. |
| **The same file is not processed twice** | `dedupe_key` = organisation + SHA-256 of the content + campaign, with a unique index. The second copy is recorded as `Duplicate`, moved to `rejected/`, and no job is created. |
| **A number repeated inside a file is sent once** | Later occurrences are `classification = Duplicate`. |
| **One file never produces two tasks by accident** | The task row is committed before the request is sent; a unique index allows one live task per job and sequence. See [section 9](#9-surviving-restarts-and-failures). |

To check numbers again on purpose, tick **"Check them again anyway"** on the upload page. That
is the only way a saved answer is ignored.

**`ReuseScope`.** With `Global` (the default) one organisation's answer saves another
organisation the cost of the same number - and tells it whether that number is on WhatsApp.
Use `Tenant` if organisations must not learn anything from each other's checks.

---

## 9. Surviving restarts and failures

The rule throughout: **write down what you are about to do, then do it, then write down what
happened.** After a stop, the worker reads what was written and carries on from there.

| What happened | What the worker does next time |
|---|---|
| Stopped while a file was still being copied in | Nothing was taken. The file is picked up once it is complete. |
| Stopped after taking a file but before saving it | The file sits in `files/.../` with a `claim.json`. The next scan finds it and registers it. |
| Stopped before the CheckNumber.ai request was sent | The task row is `Creating` with no "request sent" mark. It is created now. |
| Stopped (or timed out) **after** the request was sent, before the answer | The task is `Uncertain` and the job fails with `CHECKNUMBER_CREATE_UNCERTAIN`. It is **not** created again automatically: the first one may exist and be billed. A person checks the CheckNumber.ai dashboard and presses Retry. |
| Task created, worker stopped | It polls the **same** task id. No new task, no new charge. |
| Task finished, result not downloaded | It downloads now. If the download link has expired it asks for a fresh one. |
| Downloaded, not read | It reads the archive already on disk. |
| A WATI batch was sent, no answer came back | The batch is `Uncertain`. Each of its messages is looked up at WATI by its own id. Only a message WATI has no record of is reconsidered, and with `ResendWhenDeliveryUnknown = false` (the default) it is marked failed rather than sent again. |
| A WATI batch was half accepted | Each message has its own answer; the accepted ones are done, the refused ones are failed with the reason. |
| The database went away | The job keeps its state and is tried again with a growing delay. After `MaxConsecutiveStageFailures` it is failed and can be retried. |
| The worker was killed | Its jobs' leases run out (`JobLeaseSeconds`) and another instance - or the same one, restarted - takes them over. Its row in `int_worker_instances` is marked `Lost`. |

### Two workers never take the same thing

| Thing | How it is guarded |
|---|---|
| An inbox file | The first to move it out of the inbox has it; the other's move fails |
| A job, a WATI job, a request | Claimed with `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED)`, with a lease that is renewed while the work goes on |
| A WATI message | Claimed with a conditional `UPDATE` (Queued to Sending) before the request leaves |
| A job number | One atomic `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` |

---

## 10. Retries

A call is retried only when trying again can help.

| Retried | Not retried |
|---|---|
| HTTP 408, 429, 500, 502, 503, 504 | 400, 401, 403, 404, 422 and every other answer |
| Timeouts | A request that was refused for what it says |
| Connection failures | |

With the defaults (`MaxAttempts 3`, `InitialDelaySeconds 2`, `MaxDelaySeconds 60`) the waits are
**2, 4 and 8 seconds**. A `Retry-After` header is honoured.

One extra rule protects the money: a request that **creates something** (a CheckNumber.ai task,
a WATI batch) is retried only when it certainly did not arrive - the connection was never made,
or the server answered 429 or 503. After a timeout nobody knows whether it arrived, so it is not
blindly repeated; see the "Uncertain" rows above.

Every retry is logged and stored as a `RETRY_SCHEDULED` event:

```
Error "HTTP 503 Service Unavailable" occurred during job INT-20260917-000001 at step WATI_SEND_BATCH. Retry attempt 2/3 after 4 seconds.
Job [INT-20260917-000001] Step [WATI_SEND_BATCH] Attempt [2] Error [HTTP 503 Service Unavailable] Retrying after [4] seconds.
```

When the three retries are used up, the job is not failed yet. It is parked and the whole step
is tried again later (15 s, 30 s, ... growing), up to `MaxConsecutiveStageFailures` times.

---

## 11. CheckNumber.ai, as it actually behaves

These were found by calling the live service, and differ in places from the written brief.

| | |
|---|---|
| Create a task | `POST https://api.checknumber.ai/v1/tasks`, multipart: `file` (one number per line), `task_type=ws`. Header `X-API-Key`. Answers HTTP 202 with `task_id`. |
| Poll a task | **`POST /v1/gettasks`** with form field `task_id`. A `GET` on that address answers 404. |
| Done when | `status` is `exported` and `result_url` is present |
| The result | A zip holding `{task_id}/all.csv` (`number,activated` with `yes`/`no`), `activated.txt`, `unregistered.txt` and `result.xlsx`. The worker reads `all.csv`, and falls back to the two text files. |
| The download link | A pre-signed storage link that expires. Its signature is removed before it is stored or logged. |
| Price | About 0.00009 USD per number, **with a minimum of about 0.045 USD per task** - the price of 500 numbers. Two numbers cost the same as 500. |

### Foreign numbers in a mostly-Indian file

When a task holds numbers from several countries, CheckNumber.ai puts the main country's code in
front of the others: `+6285219223209` was checked as `916285219223209`. That answer is for a
different number, so the worker does **not** use it. The number is stored as Unknown with the
note *"CheckNumber.ai put 91 in front of this number and checked ... instead"*, is not messaged,
and is checked again next time.

In the 550-number sample this happened to 49 numbers. When two of them were later submitted in a
task of their own, both came back correctly as on WhatsApp.

To avoid it, set `CheckNumber:SplitTasksByCountry = true`: one task per country. The cost is one
minimum charge per country.

---

## 12. WATI

### 12.0 The connection details

All three come from the WATI dashboard, **Connectors → API**, tab **API Docs**
(`https://live.wati.io/10235826/api-docs`). The tab **Create API Token** makes a new token.

| WATI shows | Value for this account | Setting |
|---|---|---|
| API Endpoint | `https://live-mt-server.wati.io/10235826` | The part before the last `/` is `Wati:BaseUrl` (already the default, `https://live-mt-server.wati.io`); the number after it is `Wati__TenantId=10235826` |
| Access Token | `Bearer eyJhbGciOiJI…17kqow` (553 characters after "Bearer ") | `Wati__ApiToken` in `.env`. Paste it with or without the word `Bearer`; the worker accepts both |
| CONNECTED, top right | `+919597304379` | `Wati__ChannelNumber=919597304379` |

The token in use was created on **10 October 2026 at 16:26 UTC** by the account's administrator
user. It carries no practical expiry date (WATI writes 31 Dec 9999 into it), but WATI recommends
resetting it **every six months**. The full value is only in `.env`, which git ignores; it is never
written to a log, a table or this file.

**Changing the token:**

1. In WATI: Connectors → API → Create API Token, then copy the Access Token.
2. Put it in `.env` as `Wati__ApiToken=...`.
3. Restart the worker. In Docker that means **recreating** the container, because a plain restart
   keeps the old environment: `docker compose up -d --force-recreate`.
4. Check it without sending anything: within a few seconds the log says
   `WATI templates synced: 7 received, 6 approved.` A bad token logs
   `WATI templates could not be read (GetTemplates failed (HTTP 401) ...)` instead.

### 12.1 What is sent

`POST {BaseUrl}/{TenantId}/api/v2/sendTemplateMessages` with a bearer token:

```json
{
  "template_name": "l4l_001",
  "broadcast_name": "l4l_001",
  "channel_number": "91XXXXXXXXXX",
  "receivers": [
    { "whatsappNumber": "919500873627",
      "localMessageId": "ydot-01a125b7d68071b3a1591a77e426b8f1",
      "customParams": [{ "name": "image_url", "value": "https://.../blind_stick_cam1.jpg" }] }
  ]
}
```

Nothing here is hard-coded. The template, the broadcast name and the parameters come from, in
order: the upload (its manifest), else the `Wati:Default...` settings. A broadcast sent from the
Wati page carries its own.

`localMessageId` is created by the worker, one per recipient. It is how an answer is matched to
a number, and how a message is found again at WATI after a lost response.

**Reading WATI's answer.** WATI answers `{"result": true, "receivers": [{"localMessageId": ...,
"isValidWhatsAppNumber": false, "errors": []}]}`. A receiver counts as **refused only when WATI
lists errors for it** (or names it in the older `invalidWhatsappNumbers` list).
`isValidWhatsAppNumber` is stored, but it decides nothing: WATI answers `false` for numbers it has
not validated itself - most of them - and sends the message anyway. This was found on
10 October 2026, when three receivers answered `false` were delivered and read.

### 12.2 Who gets a message

Applied in this order. Everyone left out is stored as `Skipped` with the reason.

| # | Rule | Reason stored |
|---|---|---|
| 1 | Not on WhatsApp | `WHATSAPP_NOT_AVAILABLE` |
| 2 | CheckNumber.ai could not tell | `WHATSAPP_UNVERIFIED` |
| 3 | An allow-list is set and the number is not on it | `NOT_IN_ALLOWLIST` |
| 4 | This template was already sent to this number for this campaign | `ALREADY_SENT` |
| 5 | The cap for one job is reached | `MAX_RECIPIENTS_REACHED` |

A broadcast from the Wati page goes through the same five rules. A number asked for there that
is not one of the organisation's verified numbers is left out altogether (`NOT_IN_POOL`).

### 12.3 What is recorded for each number

Integration job, CheckNumber.ai task id, WATI job, number, template, broadcast, request status
(`NotSent` / `Accepted` / `Rejected` / `Uncertain`), HTTP status, WATI's answer, retry count,
error, started, completed, duration - and then sent, delivered, read and replied times as WATI
reports them.

### 12.4 Delivery status

After WATI accepts a message, the worker asks about it
(`GET .../api/v1/whatsApp/messages/{channel}/{localMessageId}`): first after 20 seconds, then at
a widening interval, for up to 72 hours. A status never moves backwards.

WATI's lookup carries one time: when the message was created. That dates **Sent**. Delivered, Read,
Replied and Failed are dated when the worker **noticed** the change - within one check interval -
and the message is flagged `status_times_observed = true`. Exact times and reply text need WATI
webhooks; see [Known limits](#18-known-limits-and-next-steps).

### 12.5 Before going live

1. Make sure the token is current (see [12.0](#120-the-connection-details)).
2. Remove the `Wati__AllowedNumbers__*` lines.
3. Set `Wati__MaxRecipientsPerJob` to `0` or to the largest broadcast you intend.
4. Restart the worker. The Wati page shows the limits it is running with.

---

## 13. The web application

The pages were built by the UI team on made-up data held in the browser. They now read this
worker's tables, through the Campaign service (CAM) of `D:\Permaspace\Ydot-Goldwin-Latest`.

```
Browser ──► nginx ──► Campaign API ──┬── reads   int_* tables (same database)
 /app/whatsapp/...   /cam-api/v1/    ├── writes  {data}/inbox/UPL-....csv + .manifest.json
                     whatsapp/...    └── inserts int_worker_commands   (retry, stop, broadcast)
                                                         │
                                          this worker ◄──┘  does the work
```

The Campaign API never calls CheckNumber.ai or WATI and holds neither key.

### 13.1 Endpoints (`/cam-api/v1/whatsapp/...`)

| Method and path | For |
|---|---|
| `GET overview` | Totals, whether the worker is running, its limits and prices |
| `POST numbers/lookup` | "Which of these numbers already have an answer?" - the upload review |
| `POST check-jobs` | Upload a file (multipart: `file`, `campaignId`, `forceRecheck`, optional `watiTemplateName`, `watiBroadcastName`, `watiParameters`) |
| `GET uploads/{reference}` | Has the worker turned the upload into a job yet |
| `GET check-jobs`, `GET check-jobs/{job}` | The jobs list; one job with its tasks, timeline and WATI outcome |
| `GET check-jobs/{job}/results`, `/invalid-lines` | The report |
| `GET check-jobs/{job}/files/{kind}` | Downloads: `original`, `submitted`, `results`, `raw`, `invalid`, `summary` |
| `POST check-jobs/{job}/retry`, `/cancel` | Leave a request for the worker |
| `GET wati/templates`, `wati/recipients`, `wati/broadcasts`, `wati/messages` | The Wati page |
| `POST wati/broadcasts` | Send a broadcast |
| `POST wati/templates/refresh` | Read the template list from WATI again |
| `GET commands/{reference}` | What became of a request |

`{job}` is the job number (`INT-20261010-000001`) or its id.

### 13.2 Who may do what

| | Permission (an existing campaign permission) | Roles that hold it |
|---|---|---|
| See the pages | `cam.campaigns.view` | Campaign Executive, Campaign Manager, Organisation Admin |
| Upload, send, retry, stop | `cam.campaigns.create` | Campaign Executive, Organisation Admin |

No change was made to IAM. Every answer is limited to the caller's own organisation.

### 13.3 The campaign's Recent Activity

When an upload or a broadcast names a campaign, the campaign's page shows it:

| Entry | Comes from |
|---|---|
| "Whatsapp list uploaded", "Whatsapp broadcast requested" | The campaign audit trail, written when the person asked |
| "Whatsapp number check completed" / "failed" | This worker's job, read live |
| "Whatsapp messages sent" / "failed" | This worker's WATI job, read live |

### 13.4 Files changed in the web application

| Area | Files |
|---|---|
| Campaign API | `Services/CAM/YDots.CAM.API/Controllers/WhatsAppController.cs`, `YDots.CAM.Application/Features/WhatsApp/`, `YDots.CAM.Infrastructure/Integration/WhatsApp/`, plus small edits to `PermissionCodes.cs`, both `DependencyInjection.cs`, `CampaignReadService.cs`, `appsettings.json`, `Dockerfile` |
| Deployment | `docker-compose.yml` (the shared volume and two settings for `ydot-cam`), `UI/YDots.UI/nginx.conf` (12 MB allowed on the upload route) |
| Pages | `UI/YDots.UI/src/app/Service/whatsapp-api.service.ts` (new), and in `Features/YDot/WhatsApp/`: `whatsapp-store.service.ts`, `whatsapp.models.ts`, `wa-format.ts`, `check-number/`, `wati/`; `app.routes.ts` and `Shared/sidebar/sidebar.ts` (permission checks) |

The page layout and styling were not changed. One control was added: a campaign picker on the
upload review, using the existing drop-down component.

---

## 14. Watching it work

| Where | What you see |
|---|---|
| Console / `docker logs ydot-integration-worker` | One line per thing that happened |
| `logs/integration-<date>.log` | The same, with the class name; 30 days kept |
| Seq (`http://localhost:6711`, set `Seq__ServerUrl`) | Searchable, structured logs |
| `int_job_events` | The story of a job, in sentences |
| `int_job_steps` | Each step, its attempts and how long it took |
| `int_api_call_logs` | Every HTTP call, with status and duration |
| `int_worker_instances` | Which workers are alive |
| The web pages | Everything above, for people who should not need SQL |

**Secrets are kept out of all of them.** The API key and token are never logged, the storage
link's signature is removed, and the sending number is masked in the published settings.

On the very first start against a database, EF Core logs one
`Failed executing DbCommand ... __ef_migrations_history_int` error. That is its way of finding
out the history table does not exist yet; the next lines create it. It is harmless.

---

## 15. For QA: what to test and what to expect

Test with the allow-list and the cap in place (the `.env` template has both), so a mistake
cannot message a stranger.

| # | Do this | Expect |
|---|---|---|
| 1 | Upload a new list | A job; `fresh_numbers` = numbers never seen before; one CheckNumber.ai task |
| 2 | Upload the same file again | "This exact file was already processed..."; no new job; no new task |
| 3 | Upload a different file holding numbers already checked | Those numbers are `AlreadyChecked`; only new ones are sent; with none new, cost is 0 and no task is created |
| 4 | Tick "Check them again anyway" | Every number is sent again |
| 5 | Put the same number on two lines | The second is `Duplicate` |
| 6 | Include `12345`, `n/a`, an empty line | Invalid lines with reasons; the empty line is ignored |
| 7 | Upload an `.xlsx` with two sheets | Both sheets read; sheet name and line kept per number |
| 8 | Upload a `.pdf` | Refused: "is not a .txt, .csv, .xls or .xlsx file" |
| 9 | Stop the worker, upload, start it | "The integration worker is not running" on the page; the file is processed on start |
| 10 | Kill the worker while a task is being polled | On restart it polls the same task id; `int_checknumber_tasks` still has one row |
| 11 | Start with `CheckNumber__BaseUrl=https://127.0.0.1:9/` | Retries at 2, 4, 8 s in the log; the job fails with `CHECKNUMBER_UNAVAILABLE` and can be retried |
| 12 | Press Stop on a running job | `Cancelled`, `CANCELLED_BY_USER`; answers already received are kept |
| 13 | Send a broadcast to a number outside the allow-list | Skipped, `NOT_IN_ALLOWLIST` |
| 14 | Send the same template to the same number twice | Second time skipped, `ALREADY_SENT` |
| 15 | Sign in as another organisation | None of the first organisation's jobs, numbers or messages |
| 16 | Sign in as a Campaign Manager | Can see everything; upload and send are refused (403) |

Sample file: `D:\Permaspace\Phase3\samples\WAChecker2.txt` (550 numbers).

---

## 16. What was verified, and what was not

Run on 10 October 2026 against the live CheckNumber.ai and WATI services and the YDot Docker
database, for the organisation Smile Foundation.

### Verified

| Test | Result |
|---|---|
| **550 numbers**, `WAChecker2.txt`, copied into the inbox | Job `INT-20261010-000001`. One task (`db52l06p2jvrajg15eb0`), finished in 39 seconds, **0.0495 USD**. 96 on WhatsApp, 405 not, 49 not verifiable (the foreign numbers of [section 11](#11-checknumberai-as-it-actually-behaves)). |
| The same file again | Recorded as `Duplicate`. No job, no task, nothing charged. |
| A 5-line CSV with a heading, mixed number formats and one repeat | Job `...000002`. 3 distinct numbers: 1 answered from the first job (not charged), 2 sent (`db52ljup2jvrajg15ed0`, 0.045 USD), 1 duplicate. WATI job with **exactly the 3 test numbers**. |
| A two-sheet `.xlsx`, uploaded through the Campaign API while the worker was **stopped** | Picked up when the worker started. Job `...000003`, linked to the campaign "Educate a Child 2026". 3 numbers, all answered from saved results: **no task, no cost**. 2 duplicates, 2 invalid lines. |
| A CSV uploaded on the real page in a browser | Job `...000004`. 3 reused, 2 new (`db55t7up2jvrajg163s0`, 0.045 USD, both on WhatsApp). WATI: 3 attempted, 2 skipped `NOT_IN_ALLOWLIST`. |
| The whole stack in Docker (worker container, Campaign API, pages on port 6700) | A list uploaded on `http://smilefoundation.localhost:6700` became job `...000006` in 16 seconds: 3 numbers, all from saved results, no task, no cost. Downloads were served from the shared volume. |
| **WATI, with the token from the WATI dashboard** | The template list was read: 7 templates, 6 approved, so the Wati page now offers the real ones with their real wording. |
| **A real send:** "Retry job" on `INT-20261010-000004` (campaign "Educate a Child 2026") | `l4l_001` was sent to the three test numbers and **WATI accepted all three**. WATI's own records: 919500873627 **delivered**, 919841465676 **read**, 919876543210 **failed inside WhatsApp** (WATI gives no reason). This test exposed a bug: the worker took WATI's `isValidWhatsAppNumber: false` as a refusal and recorded the three as failed. Fixed the same day (see 12.1). The three old records still say "failed"; correcting them needs a database change that was not made. |
| **A real send after the fix:** the Wati page, broadcast "WATI test after fix" to 919500873627 and 919841465676 | Template `donation_request_image`. Both accepted; the status sync read both as **READ** 30 seconds later, and the delivery report showed it. |
| WATI safety | Across every test, only the three allow-listed numbers were ever put in a request. 552 recipients were skipped with a reason. In total WATI sent **five** messages: three in the first send, two in the second. |
| Retry and backoff | With CheckNumber.ai unreachable: retries at 2, 4, 8 s logged in the required format, the step tried again after 15 s and 30 s, then the job failed with `CHECKNUMBER_UNAVAILABLE`. A retry request resumed it; a stop request cancelled it. (That test job was removed afterwards.) |
| Restarts | The worker was stopped and started many times, including an unplanned restart of the machine. No file was lost and no task was created twice. A clean stop is recorded as `Stopped`, a killed worker as `Lost`. |
| Start-up validation | A wrong setting stops the worker with a sentence naming it. |
| The web pages | Jobs list, job page with report and downloads, upload-review-check-report flow, Wati compose and send, delivery report, and the campaign's Recent Activity - in a headless browser against the real API. |
| Isolation and permissions | Another organisation sees nothing of Smile Foundation's. A Campaign Manager can read but not upload or send. |

**Spend for all of this:** CheckNumber.ai, four tasks, about 0.19 USD. WATI, five template messages.

### Not verified

| | Why | What to do |
|---|---|---|
| Replies | WATI's message lookup reports a status, not the reply text. | WATI webhooks (see 18). |
| A message arriving on the phone itself | Checked through WATI's records (delivered, read), not by looking at the phones. | Ask the owners of 919500873627 and 919841465676. |

Before the token from the WATI dashboard was used, WATI had answered **HTTP 401 (`invalid_token`)**
to every request made with the token found in the two older WATI projects; nothing was sent or
charged then.

---

## 17. Troubleshooting

| You see | It means | Do |
|---|---|---|
| `WATI refused the API token (HTTP 401)` | The token is wrong, expired or was reset in WATI | Update `Wati__ApiToken`, recreate the container (see 12.0), press Retry |
| A message dated 10 Oct 2026 says `WATI_INVALID_NUMBER` but it arrived | Recorded before the fix described in 12.1 | Nothing to do for new sends; the old records can be corrected in the database |
| `No organisation could be determined` | A file was put in `inbox/` itself | Put it in `inbox/{subdomain}/`, or set `Worker:DefaultTenantSubdomain` |
| `No organisation has the subdomain or code '...'` | The folder name is not an organisation | Use the organisation's web address prefix, e.g. `smilefoundation` |
| A file stays in the inbox | It is still being written, or is open in another program | Close it. The worker logs "is still changing" |
| The upload page says the worker is not running | No heartbeat for two minutes | Start the worker; the upload is processed then |
| `CHECKNUMBER_CREATE_UNCERTAIN` | The create request was sent but no answer came | Look in the CheckNumber.ai dashboard for the task, then press Retry |
| `CHECKNUMBER_POLL_TIMEOUT` | The task took longer than `MaxPollingAttempts` polls | Press Retry: it polls the same task again |
| Many numbers "could not be verified" | Foreign numbers in a mixed file | `CheckNumber__SplitTasksByCountry=true` |
| `WATI_DELIVERY_UNCERTAIN` | A batch was sent and the answer was lost | Check WATI, then press Retry |
| The worker stops at start with `OptionsValidationException` | A setting is wrong | The message names it |
| `relation "int_..." does not exist` in the web application | The worker has never run against that database | Start it once |
| Uploads refused: "DataRoot is not configured" | The Campaign API does not know the data folder | Set `WhatsAppIntegration__DataRoot` (the compose file does) |

---

## 18. Known limits and next steps

| Limit | Suggested next step |
|---|---|
| **No automated tests.** Left out of this phase on purpose. | Unit tests for the number rules, the recipient rules and the retry rule first; then integration tests with fake CheckNumber.ai and WATI servers. |
| **Delivery status is polled.** Times are when a change was noticed; reply text is not captured. | Give WATI a webhook address (a small endpoint in the Campaign API writing to `int_wati_messages`). |
| **The "Donate Now" button of `l4l_001` is a fixed link** (`https://donate.peoplesplanet.in/donor-form`), so a message cannot open a particular campaign's donation page. | A template whose button has a dynamic part (`.../donor-form?ref={{1}}`), approved in WATI; the worker then sends each campaign's tracking reference as that parameter, with no schema change. |
| **One WATI account for every organisation.** Templates are shared. | Per-organisation WATI settings, kept the way payment gateway settings already are. |
| **Costs are in USD**, as CheckNumber.ai bills them. | Add a conversion rate if the pages should show rupees. |
| **CheckNumber.ai answers a file at once**, so the page cannot show answers arriving one by one. | Nothing to do; the page shows the provider's own progress counter. |
| **The inbox is a shared folder.** | For several servers, put it on shared storage, or replace it with object storage behind the same interface (`IInboxFileSystem`). |
| **The WhatsApp pages have no IAM menu entry or permission of their own**; they use the campaign permissions. | Add them to IAM's catalogue and change two constants in `PermissionCodes.cs`. |

---

## 19. Words used here

| Word | Meaning |
|---|---|
| **Job** | One file being processed. `INT-20261010-000001`. |
| **Task** | CheckNumber.ai's unit of work: one upload of numbers, with a `task_id`. |
| **WATI job / broadcast** | One sending of one template to a set of numbers. `WAT-20261010-000001`. |
| **Batch** | One request to WATI, holding up to `BatchSize` numbers. |
| **Fresh** | A number with no saved answer; it will be checked and paid for. |
| **Already checked / saved / reused** | A number with a saved Yes or No; not checked again. |
| **Unknown / unverified** | CheckNumber.ai gave no usable answer for the number. |
| **Lease** | A worker's temporary claim on a job, so no other worker takes it. |
| **Parked** | A job waiting for a time to pass (a poll, a retry) while the worker does other things. |
| **Manifest** | The small file beside a web upload saying whose it is and which campaign it is for. |
| **Organisation / tenant** | The same thing: the NGO whose data it is. |
