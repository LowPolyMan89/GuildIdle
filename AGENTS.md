# GuildIdle Codex Instructions

## GuildIdle Google Drive Docs Reading Policy

Дизайн-данные GuildIdle хранятся в Google Drive. **DO NOT** загружать все документы или целые таблицы вслепую. **MUST** экономить контекст и читать только минимальный релевантный источник.

### Sources of truth

Перед чтением проектной документации начни с:

1. **GuildIdle — Project Index**  
   Главный реестр canonical project documents and configs.  
   https://docs.google.com/document/d/1OsIgOiYgb6NxxwvgjQfuhaBXCVGqxg6DUkVp6CdXFwo

Для задач, связанных с конфигами, после Project Index используй:

2. **GuildIdle — Config Schema**  
   Technical schema для config architecture, id ownership, sheet relations, import order, validation rules, and links to concrete config sheets/tabs.  
   https://docs.google.com/document/d/1z3f1RWfy5OqAZRYswMrWUzCF_bCJFOq8QW3GgBLZJqo

**DO NOT** искать по всему Google Drive, если нужный файл указан в Project Index или Config Schema.

### Token-saving rules

When working with Drive documents:

- **DO NOT** read entire documents unless the task explicitly requires a full review.
- Prefer document search, heading search, and specific section reads.
- If the task references a system, first find the canonical document in **Project Index**, then read only the relevant section.
- If a document has already been summarized in the current task, reuse the summary instead of reading it again.
- If information is missing, ask for the specific missing section instead of loading unrelated documents.
- **NEVER** paste large raw document contents into prompts unless absolutely necessary.

When working with Google Sheets configs:

- **DO NOT** load full spreadsheets.
- First read spreadsheet metadata: sheet names, sheet ids, and grid sizes.
- Then read only:
  - the header row;
  - the specific sheet involved in the task;
  - the specific bounded range needed for validation or implementation.
- For config architecture, use **Config Schema** first.
- For exact data, use the linked sheet/tab from **Config Schema**.
- Prefer targeted row search by id over reading large ranges.
- If validating references, collect ids from source sheets first, then check only the dependent sheets.

### Reading order for GuildIdle config tasks

For any task involving configs, use this order:

1. Find the relevant system and canonical sources in **Project Index**.
2. Read the related section of **Config Schema**.
3. Open only the linked config sheet/tab from the “Быстрые ссылки” section.
4. Read the header row of the relevant sheet.
5. Read or search only the rows related to the requested ids.
6. If the task may change config architecture, stop implementation and follow the architecture change approval flow below.

### Architecture update rule

If a task changes config architecture:

1. Present a concise implementation plan.
2. Ask targeted questions about decisions that cannot be inferred from the current schema.
3. Wait for explicit user approval.
4. Update **GuildIdle — Config Schema** before or together with the approved code/config changes.

Architecture changes include:

- adding a new config file;
- adding, deleting, or renaming a sheet;
- adding, deleting, or renaming columns;
- changing the meaning of a field;
- changing id format;
- changing packed reference format, for example `item_id:count` or `enemy_id:level`;
- moving an entity between configs;
- changing how `target_id`, `loot_id`, `req_type`, `reward_type`, `drop_type`, or `trigger_type` is resolved.

**DO NOT** implement importer or runtime assumptions that are not described in **Config Schema**. If the schema is missing a required rule, propose the schema change and wait for approval before implementation.

### Canonical config access pattern

Use **Config Schema** links instead of manually guessing sheet tabs.

Typical examples:

- Need activity ids: open `Activity Configs / Activities`.
- Need activity rewards: open `Activity Configs / ActivityRewards`.
- Need skill ids: open `Activity Configs / Skills`.
- Need item/resource ids: open `Items Configs / Ресурсы`, `Снаряжение`, `Рецепты`, or `Расходники`.
- Need currencies: open `Items Configs / Валюты`.
- Need building ids: open `Buildings Configs / Index`.
- Need map cells or locations: open `Map Configs / MapCells` or `MapLocations`.
- Need enemy ids: open `Enemies Configs / Enemies`.
- Need enemy groups: open `Enemies Configs / EnemyGroups`.
- Need loot tables: open `Loot Configs / LootTables` and `LootTableEntries`.

### Important GuildIdle data rules

- `gold_id` is a `currency_id`, not an `item_id`.
- `item_gold` is forbidden legacy data.
- **DO NOT** treat currency as an inventory item, craft material, stackable item, or craftable entity.
- Polymorphic fields **MUST** be resolved through their type field:
  - `ActivityRequirements.target_id` depends on `req_type`;
  - `ActivityRewards.target_id` depends on `reward_type`;
  - `LootTableEntries.target_id` depends on `drop_type`;
  - `ActivityTriggers.target_id` depends on `trigger_type`.
- `EnemyGroups.enemy_ref` uses the format `enemy_id:level`.
- Packed material refs use the format `id:count`.

### If Drive access is unavailable

If Google Drive tools or MCP are unavailable:

- **DO NOT** guess document or config structure.
- Ask the user to provide the relevant export, screenshot, or copied section.
- Use local project files only if they are clearly up to date.
- If a local file conflicts with **Project Index** or **Config Schema**, treat the canonical Drive documents as the source of truth.

### Response discipline

When answering or implementing:

- Mention which canonical sources were used.
- Mention which sections, sheets, or bounded ranges were read.
- Avoid dumping raw document or config data into the response.
- Prefer concise summaries, ids, and exact changed files.

## Project Tracking

Project work is tracked through GitHub Issues:

https://github.com/LowPolyMan89/GuildIdle/issues

### GitHub Issue discipline

Changes to code, configs, assets, documentation, or project files should be performed within the scope of a GitHub Issue in the `LowPolyMan89/GuildIdle` repository.

- Before changing project files, identify the active GitHub Issue.
- If the user has not provided an Issue number or URL, ask which Issue should own the work.
- Read the active Issue before implementation and keep changes within its stated scope and acceptance criteria.
- Read linked Issues only when they are directly relevant to the requested work.
- If implementation reveals additional work outside the active Issue scope, stop before making those extra changes.
- Ask the user whether to create a separate Issue, extend the current Issue, or link an existing related Issue.
- **DO NOT** silently expand the active Issue scope.
- **DO NOT** create, edit, close, reopen, label, assign, or comment on GitHub Issues without explicit user permission.
- **DO NOT** modify the repository, create commits, push branches, or open Pull Requests without explicit user permission.
- Reviews, explanations, planning, and other read-only work do not require an active GitHub Issue unless the user explicitly requests Issue-based tracking.
- When project files are changed, mention the active Issue number and the exact changed files in the final response.

## Build And Verification

- **DO NOT** run code builds after completing code tasks unless the user explicitly asks for a build.
- **DO NOT** try to run Unity EditMode tests.
- **DO NOT** search for Unity in PATH, Program Files, the registry, or typical local install paths.
- **DO NOT** generate or rebuild runtime config JSON files yourself. If runtime config regeneration is needed, ask the user to run the existing Unity Config Downloader pipeline, wait for the result, then continue from the reported errors or updated files.
- For default verification, inspect the resulting diff and run `git diff --check`.
- Run additional verification only when explicitly requested by the user.

## Unity Assets

- **DO NOT** edit Unity scenes or prefabs unless the user explicitly asks for scene or prefab changes.
- Treat `.unity`, `.prefab`, and their related metadata changes as out of scope without explicit permission.
- **DO NOT** create Unity `.meta` files manually. Let Unity generate and update `.meta` files.
