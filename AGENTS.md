# GuildIdle Codex Instructions

## GuildIdle Google Drive Docs Reading Policy

Дизайн-данные GuildIdle живут в Google Drive. **DO NOT** загружать все документы или целые таблицы вслепую. **MUST** экономить контекст и читать только самый маленький релевантный источник.

### Source of truth

Before reading any project document or config, start from:

1. **GuildIdle — Project Index**  
   Главный реестр canonical project documents and configs.  
   https://docs.google.com/document/d/1OsIgOiYgb6NxxwvgjQfuhaBXCVGqxg6DUkVp6CdXFwo

2. **GuildIdle — Config Schema**  
   Technical schema для config architecture, id ownership, sheet relations, import order, validation rules, and links to concrete config sheets/tabs.  
   https://docs.google.com/document/d/1z3f1RWfy5OqAZRYswMrWUzCF_bCJFOq8QW3GgBLZJqo

Use these two files as the entry point. **DO NOT** search the whole Drive unless the needed file is not listed there.

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
- First read spreadsheet metadata: sheet names, sheet ids, grid sizes.
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

1. Read the **Config Schema** section related to the system.
2. Open only the linked config sheet/tab from the “Быстрые ссылки” section.
3. Read the header row of the relevant sheet.
4. Read or search only the rows related to the requested ids.
5. If the task may change config architecture, stop implementation and follow the architecture change approval flow below before making changes.

### Architecture update rule

If a task changes any config architecture, first switch to **Plan Mode** and ask the user targeted questions about the intended changes. Make architecture changes only after explicit user approval, then update **GuildIdle — Config Schema** before or together with the approved code/config changes.

Architecture changes include:

- adding a new config file;
- adding, deleting, or renaming a sheet;
- adding, deleting, or renaming columns;
- changing the meaning of a field;
- changing id format;
- changing packed reference format, for example `item_id:count` or `enemy_id:level`;
- moving an entity between configs;
- changing how `target_id`, `loot_id`, `req_type`, `reward_type`, `drop_type`, or `trigger_type` is resolved.

**DO NOT** implement importer/runtime assumptions that are not described in **Config Schema**. If the schema is missing a rule, update the schema first.

### Canonical config access pattern

Use **Config Schema** links instead of manually guessing sheet tabs.

Typical examples:

- Need activity ids: open `Activity Configs / Activities`.
- Need activity rewards: open `Activity Configs / ActivityRewards`.
- Need skill ids: open `Activity Configs / Skills`.
- Need item/resource ids: open `Items Configs / Ресурсы`, `Снаряжение`, `Рецепты`, `Расходники`.
- Need currencies: open `Items Configs / Валюты`.
- Need building ids: open `Buildings Configs / Index`.
- Need map cells or locations: open `Map Configs / MapCells` or `MapLocations`.
- Need enemy ids: open `Enemies Configs / Enemies`.
- Need enemy groups: open `Enemies Configs / EnemyGroups`.
- Need loot tables: open `Loot Configs / LootTables` and `LootTableEntries`.

### Important GuildIdle data rules

- `gold_id` is a `currency_id`, not an `item_id`.
- `item_gold` is forbidden legacy data.
- **DO NOT** treat currency as inventory item, craft material, stackable item, or craftable.
- Polymorphic fields **MUST** be resolved through their type field:
  - `ActivityRequirements.target_id` depends on `req_type`;
  - `ActivityRewards.target_id` depends on `reward_type`;
  - `LootTableEntries.target_id` depends on `drop_type`;
  - `ActivityTriggers.target_id` depends on `trigger_type`.
- `EnemyGroups.enemy_ref` uses the format `enemy_id:level`.
- Packed material refs use the format `id:count`.

### If Drive access is unavailable

If Google Drive tools or MCP are unavailable:

- **DO NOT** guess config structure.
- Ask the user to provide the relevant export, screenshot, or copied section.
- Use already cached local project files only if they are clearly up to date.
- If a local file conflicts with **Project Index** or **Config Schema**, treat Drive canonical docs as the source of truth.

### Response discipline

When answering or implementing:

- Mention which canonical source was used.
- Mention which sheets/sections were read.
- Avoid dumping raw config data into the response.
- Prefer concise summaries, ids, and exact changed files.

## Project Documentation

- Use the Asana project `GuildIdle` for project tracking.

### Asana task discipline

Разработка должна идти в рамках задач Asana проекта `GuildIdle`. Если работа не привязана к задаче, сначала уточнить task context.

- Development should happen only within the scope of an Asana task in the `GuildIdle` project.
- Before changing or creating code, configs, assets, documentation, or project files, identify the active Asana task or ask the user which task should own the work.
- Keep changes limited to the active task scope and directly related linked tasks.
- If implementation reveals work in another system, feature area, config architecture, or asset category that is not covered by the active task, stop and ask the user whether to create a new Asana task or link an existing related task before making those extra changes.
- **DO NOT** silently expand the task scope. Record or mention the Asana task context in the final response when changes are made.

## Build And Verification

- **DO NOT** run code builds after completing code tasks unless the user explicitly asks for a build.

## Unity Assets

- **DO NOT** edit Unity scenes or prefabs unless the user explicitly asks for scene or prefab changes.
- Treat `.unity`, `.prefab`, and related scene/prefab metadata changes as out of scope without explicit permission.
- **DO NOT** create Unity `.meta` files manually. Let Unity generate and update `.meta` files.
