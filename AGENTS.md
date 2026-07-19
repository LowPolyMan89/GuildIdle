# GuildIdle Codex Instructions

Serialized SaveData must remain below 200 KB. Persistent collections must have explicit bounded retention. Do not introduce unbounded receipts, histories, logs, completed executions, or similar append-only data.

## 1. Permission And Scope

- Reviews, analysis, planning, Issue checks, commit checks, and explanations are **read-only** unless the user explicitly asks to apply changes.
- An Issue URL, commit SHA, branch name, suggested patch, or request to review something is **not** permission to modify files, Issues, branches, commits, or Pull Requests.
- Modify repository, Drive, config, documentation, or project files only after an explicit user request to implement or apply changes.
- Create branches, commits, pushes, Pull Requests, or GitHub Issue changes only when explicitly requested.
- Keep every change minimal and scoped. **DO NOT** perform opportunistic refactoring, broad renaming, formatting cleanup, or unrelated fixes.
- If additional work is discovered outside the requested scope, continue the valid in-scope work and report the extra finding separately. **DO NOT** implement it without approval.
- Before asking a question, check the current conversation, active Issue, Project Index, relevant documents, and repository context. **DO NOT** ask the user to repeat information already available.

## 2. Source Ownership

Different sources own different facts:

- **GuildIdle — Project Index** defines which project documents and configs are canonical.
- **GuildIdle — Config Schema** defines config architecture, id ownership, relations, formats, import order, and validation rules.
- Canonical Google Sheets define exact config data.
- The active GitHub Issue defines implementation scope and acceptance criteria.
- The repository defines the currently implemented code and project state.

**DO NOT** silently resolve conflicts between an Issue, canonical documentation, configs, and repository implementation. Report the conflict and continue only with the unambiguous part of the task.

## 3. GuildIdle Google Drive Reading Policy

Design data is stored in Google Drive. Read only the smallest relevant source.

Start with:

1. **GuildIdle — Project Index**  
   https://docs.google.com/document/d/1OsIgOiYgb6NxxwvgjQfuhaBXCVGqxg6DUkVp6CdXFwo

2. For config-related tasks, then read **GuildIdle — Config Schema**  
   https://docs.google.com/document/d/1z3f1RWfy5OqAZRYswMrWUzCF_bCJFOq8QW3GgBLZJqo

- **DO NOT** search the whole Drive when the required source is linked from Project Index or Config Schema.
- **DO NOT** read entire documents unless the task explicitly requires a full review.
- Prefer document search, heading search, and narrow section reads.
- Reuse relevant summaries already established in the current task.
- **DO NOT** paste large raw document contents into prompts or responses unless necessary.

### Google Sheets

- **DO NOT** load complete spreadsheets by default.
- First inspect sheet metadata: names, ids, and grid sizes.
- Then read only the relevant header, sheet, rows, ids, or bounded range.
- Use Config Schema links and ownership rules. **DO NOT** guess spreadsheet names, sheet names, tabs, or id ownership from memory.
- For reference validation, collect ids from source sheets first, then inspect only the dependent rows.

### Creating Or Changing Drive Documents

Before creating or changing a GuildIdle document or spreadsheet:

1. Check Project Index.
2. Check the GuildIdle folder.
3. Check the `Дизайн Документы` folder.
4. Use an existing canonical file when suitable.
5. If no suitable file exists, ask the user before creating a new one.

**DO NOT** create duplicate documents as a workaround.

### If Drive Access Is Unavailable

- Continue repository-only analysis when canonical Drive information is not required.
- **DO NOT** guess missing document, schema, or config structure.
- Ask for a specific export, screenshot, or copied section only when the missing canonical information materially blocks the task.

## 4. Config Architecture Changes

For config tasks, use this order:

1. Find the relevant system and canonical sources in Project Index.
2. Read the related Config Schema section.
3. Open only the linked config sheet or tab.
4. Read its header and the rows related to the requested ids.

Architecture changes include:

- adding a new config file;
- adding, deleting, or renaming a sheet or column;
- changing field meaning or id format;
- changing packed reference formats;
- moving an entity between configs;
- changing how polymorphic ids or type-dependent references are resolved.

Before implementing an architecture change:

1. Present a concise implementation plan.
2. Ask only about decisions that cannot be inferred from the current schema or task.
3. Wait for explicit approval.
4. Update Config Schema before or together with the approved code and config changes.

- **DO NOT** implement importer or runtime assumptions not described in Config Schema.
- Currencies are not inventory items.
- Polymorphic ids must be resolved through their type fields.
- Packed references must follow the current Config Schema.
- Config Schema defines the current exact field names and formats; **DO NOT** duplicate or infer stale formats from memory.

## 5. GitHub Issue Workflow

Project work is tracked through GitHub Issues:

https://github.com/LowPolyMan89/GuildIdle/issues

- Determine the active Issue from the user request, provided URL, current branch, Pull Request, or established task context.
- Ask which Issue owns the work only when project changes are requested and ownership remains genuinely ambiguous.
- Read the active Issue before implementation and keep changes within its scope and acceptance criteria.
- Read linked Issues only when directly relevant.
- **DO NOT** silently expand Issue scope.
- **DO NOT** create, edit, close, reopen, label, assign, or comment on Issues without explicit user permission.
- Reviews, explanations, planning, and other read-only work do not require an active Issue unless the user explicitly requests Issue-based tracking.

### Issue Or Commit Reviews

For review tasks:

- read the Issue and acceptance criteria when provided;
- inspect the actual diff or changed files;
- consult canonical documentation only where needed;
- separate confirmed defects, probable risks, and optional suggestions;
- do not modify files, Issues, or branches during the review;
- conclude whether the work is ready, conditionally ready, or not ready.

## 6. Repository Editing Rules

Before modifying files in a local workspace:

- inspect `git status` and the relevant diff;
- preserve unrelated user changes;
- **DO NOT** reset, discard, overwrite, or reformat unrelated files;
- **DO NOT** use destructive Git commands such as `reset --hard`, `checkout --`, `clean`, force-push, or history rewrite without explicit permission.

When editing:

- make the smallest coherent change that satisfies the task;
- preserve existing conventions unless the task explicitly changes them;
- mention the active Issue, exact changed files, and any out-of-scope findings in the final response.

## 7. Generated Files And Config Downloads

- **DO NOT** hand-edit generated runtime config JSON.
- **DO NOT** generate or rebuild runtime config JSON manually.
- Fix the canonical source, schema, parser, validator, or downloader pipeline instead.
- When regeneration is required, ask the user to run the existing Unity Config Downloader pipeline and continue from the resulting files or reported errors.
- Generated outputs do not define design intent when they conflict with canonical sources.

## 8. Build And Verification

Default verification:

- inspect the resulting diff;
- run `git diff --check` when working in a local repository;
- run only focused static checks that do not alter project files.

- **DO NOT** run builds or Unity tests unless explicitly requested.
- When Unity tests are explicitly requested, use the workspace-configured Unity executable; do not search PATH, Program Files, or the registry.
- Do not pass `-quit` to Unity Test Framework command-line runs in this project.
- Ensure Unity is not already running before a command-line test run.
- Keep temporary test results outside tracked project files and remove any generated temporary files before finishing.

## 9. Unity Assets

- **DO NOT** edit Unity scenes or prefabs unless the user explicitly requests scene or prefab changes.
- Treat `.unity`, `.prefab`, and their related metadata changes as out of scope without that permission.
- **DO NOT** create Unity `.meta` files manually. Let Unity generate and update them.

## 10. Final Response

After implementation, report:

- active Issue, or state that no Issue was linked;
- changed files;
- behavior or rules changed;
- verification performed;
- verification not performed;
- remaining risks or out-of-scope findings.

When canonical Drive sources were consulted, briefly name the documents, sections, sheets, or ranges used. Avoid dumping raw source contents.