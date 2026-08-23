using System;
using System.Collections.Generic;
using System.Linq;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Player;
using GuildIdle.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

namespace GuildIdle.Editor.Activities
{
    public sealed class RuntimeUiDebugWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 0.25d;
        private const string PreviewExecutionId = "runtime-ui-preview";
        private const int MaxCombatLogEntries = 200;

        private PlayerState _boundState;
        private PlayerStateActivityAdapter _activityState;
        private IStorageService _storage;
        private ProgressionRuntimeService _progression;
        private Label _status;
        private Label _notice;
        private VisualElement _stagePanel;
        private VisualElement _buildingGrid;
        private VisualElement _heroPanel;
        private Button _storageButton;
        private VisualElement _modalLayer;
        private VisualElement _modalBody;
        private Label _modalTitle;
        private string _openModalKind;
        private string _openModalId;
        private string _openCraftBuildingId;
        private int _openCraftBuildingLevel;
        private double _nextRefreshAt;
        private string _combatLogExecutionId;
        private readonly List<string> _combatLog = new List<string>();

        [MenuItem("GuildIdle/Runtime UI Debug")]
        public static void Open()
        {
            var window = GetWindow<RuntimeUiDebugWindow>("Runtime UI Prototype");
            window.minSize = new Vector2(900f, 560f);
        }

        private void OnEnable()
        {
            EditorApplication.update += HandleEditorUpdate;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            RuntimeConfigs.OnLoaded += HandleConfigsLoaded;
            RuntimeConfigs.OnLoadFailed += HandleConfigsLoadFailed;
            OnlineActivityRuntime.Updated += HandleRuntimeUpdated;
            OnlineActivityRuntime.CombatAdvanced += HandleCombatAdvanced;
            OnlineActivityRuntime.Failed += HandleRuntimeFailed;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(0.055f, 0.075f, 0.06f);
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var header = Row();
            header.style.height = 38f;
            header.style.paddingLeft = 12f;
            header.style.paddingRight = 8f;
            header.style.alignItems = Align.Center;
            header.style.backgroundColor = new Color(0.09f, 0.12f, 0.09f);
            rootVisualElement.Add(header);

            _status = Text("Откройте Play Mode — прототип использует реальный PlayerState.", true);
            _status.style.flexGrow = 1f;
            header.Add(_status);
            header.Add(new Button(RefreshAll) { text = "Обновить" });

            _notice = Text(string.Empty);
            _notice.style.height = 22f;
            _notice.style.paddingLeft = 12f;
            _notice.style.color = new Color(1f, 0.78f, 0.3f);
            rootVisualElement.Add(_notice);

            var world = Row();
            world.style.flexGrow = 1f;
            world.style.paddingLeft = 10f;
            world.style.paddingRight = 10f;
            world.style.paddingBottom = 10f;
            rootVisualElement.Add(world);

            var left = Panel(245f);
            left.style.marginRight = 10f;
            var stageScroll = new ScrollView(ScrollViewMode.Vertical);
            stageScroll.style.flexGrow = 1f;
            left.Add(stageScroll);
            _stagePanel = new VisualElement();
            stageScroll.Add(_stagePanel);
            world.Add(left);

            var center = Panel();
            center.style.flexGrow = 1f;
            center.style.marginRight = 10f;
            center.Add(Heading("ПОСЕЛЕНИЕ"));
            center.Add(Text("Выберите здание, чтобы открыть его активности."));
            var centerScroll = new ScrollView(ScrollViewMode.Vertical);
            centerScroll.style.flexGrow = 1f;
            center.Add(centerScroll);
            _buildingGrid = new VisualElement();
            _buildingGrid.style.flexDirection = FlexDirection.Row;
            _buildingGrid.style.flexWrap = Wrap.Wrap;
            _buildingGrid.style.justifyContent = Justify.Center;
            _buildingGrid.style.paddingTop = 18f;
            centerScroll.Add(_buildingGrid);
            world.Add(center);

            var right = Panel(275f);
            right.Add(Heading("АКТИВНЫЕ ГЕРОИ"));
            var heroScroll = new ScrollView(ScrollViewMode.Vertical);
            heroScroll.style.flexGrow = 1f;
            right.Add(heroScroll);
            _heroPanel = new VisualElement();
            heroScroll.Add(_heroPanel);
            _storageButton = new Button(OpenStorage) { text = "СКЛАД" };
            _storageButton.style.height = 58f;
            _storageButton.style.marginTop = 8f;
            right.Add(_storageButton);
            world.Add(right);

            BuildModalLayer();
            RefreshAll();
        }

        private void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            RuntimeConfigs.OnLoaded -= HandleConfigsLoaded;
            RuntimeConfigs.OnLoadFailed -= HandleConfigsLoadFailed;
            OnlineActivityRuntime.Updated -= HandleRuntimeUpdated;
            OnlineActivityRuntime.CombatAdvanced -= HandleCombatAdvanced;
            OnlineActivityRuntime.Failed -= HandleRuntimeFailed;
            ReleaseBindings();
        }

        private void BuildModalLayer()
        {
            _modalLayer = new VisualElement();
            _modalLayer.style.position = Position.Absolute;
            _modalLayer.style.left = 0f;
            _modalLayer.style.right = 0f;
            _modalLayer.style.top = 0f;
            _modalLayer.style.bottom = 0f;
            _modalLayer.style.backgroundColor = new Color(0f, 0f, 0f, 0.76f);
            _modalLayer.style.alignItems = Align.Center;
            _modalLayer.style.justifyContent = Justify.Center;
            _modalLayer.style.display = DisplayStyle.None;
            rootVisualElement.Add(_modalLayer);

            var card = Panel();
            card.style.width = new Length(64f, LengthUnit.Percent);
            card.style.maxWidth = 720f;
            card.style.height = new Length(78f, LengthUnit.Percent);
            card.style.maxHeight = 680f;
            card.style.borderTopWidth = 2f;
            card.style.borderBottomWidth = 2f;
            card.style.borderLeftWidth = 2f;
            card.style.borderRightWidth = 2f;
            card.style.borderTopColor = Accent();
            card.style.borderBottomColor = Accent();
            card.style.borderLeftColor = Accent();
            card.style.borderRightColor = Accent();
            _modalLayer.Add(card);

            var modalHeader = Row();
            _modalTitle = Heading(string.Empty);
            _modalTitle.style.flexGrow = 1f;
            modalHeader.Add(_modalTitle);
            modalHeader.Add(new Button(CloseModal) { text = "Закрыть" });
            card.Add(modalHeader);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            card.Add(scroll);
            _modalBody = new VisualElement();
            _modalBody.style.paddingRight = 8f;
            scroll.Add(_modalBody);
        }

        private void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextRefreshAt)
                return;
            _nextRefreshAt = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
            RefreshAll();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                ReleaseBindings();
                CloseModal();
            }
            RefreshAll();
        }

        private void HandleConfigsLoaded() => RefreshAll();
        private void HandleConfigsLoadFailed(string error) => SetNotice(error);
        private void HandleRuntimeUpdated(ActivityRuntimeSnapshot snapshot) => RefreshAll();
        private void HandleRuntimeFailed(string message) => SetNotice(message);
        private void HandleStorageChanged(StorageSnapshot snapshot) => RefreshAll();
        private void HandleProgressionUpdated(ProgressionRuntimeUpdate update) => RefreshAll();

        private bool EnsureBindings()
        {
            if (!Application.isPlaying || !RuntimeConfigs.IsLoaded || !RuntimePlayer.IsLoaded || RuntimePlayer.State == null)
            {
                ReleaseBindings();
                return false;
            }
            if (_boundState != null && ReferenceEquals(_boundState, RuntimePlayer.State))
                return OnlineActivityRuntime.IsReady;

            ReleaseBindings();
            _boundState = RuntimePlayer.State;
            _activityState = new PlayerStateActivityAdapter(_boundState);
            _storage = _boundState.Storage;
            _progression = RuntimePlayer.Progression;
            _storage.Changed += HandleStorageChanged;
            if (_progression != null)
                _progression.Updated += HandleProgressionUpdated;
            return OnlineActivityRuntime.IsReady;
        }

        private void ReleaseBindings()
        {
            if (_storage != null)
                _storage.Changed -= HandleStorageChanged;
            if (_progression != null)
                _progression.Updated -= HandleProgressionUpdated;
            _boundState = null;
            _activityState = null;
            _storage = null;
            _progression = null;
        }

        private void RefreshAll()
        {
            if (_status == null)
                return;
            if (!EnsureBindings())
            {
                _status.text = Application.isPlaying
                    ? "Загрузка Configs и Player..."
                    : "Откройте Play Mode — прототип использует реальный PlayerState.";
                ClearWorld();
                RequestRepaint();
                return;
            }

            _status.text = "Игра запущена · время активностей идёт автоматически";
            RefreshStageAndQuests();
            RefreshBuildings();
            RefreshHeroes();
            RefreshStorageButton();
            RefreshOpenModal();
            RequestRepaint();
        }

        private void RequestRepaint()
        {
            rootVisualElement.MarkDirtyRepaint();
            Repaint();
        }

        private void ClearWorld()
        {
            _stagePanel?.Clear();
            _buildingGrid?.Clear();
            _heroPanel?.Clear();
            if (_storageButton != null)
                _storageButton.text = "СКЛАД\nнедоступен";
        }

        private void RefreshStageAndQuests()
        {
            _stagePanel.Clear();
            _stagePanel.Add(Heading("СТАДИЯ И КВЕСТЫ"));
            var stage = _progression?.GetStageSnapshot();
            if (stage == null || string.IsNullOrWhiteSpace(stage.StageId))
            {
                _stagePanel.Add(Text("Нет активной стадии."));
                return;
            }

            _stagePanel.Add(CardTitle(L(stage.NameId)));
            _stagePanel.Add(Text(L(stage.DescriptionId)));
            var progress = stage.RequiredProgressPercent;
            var bar = new ProgressBar { title = $"Прогресс стадии: {progress}%", value = progress };
            bar.style.marginTop = 7f;
            bar.style.marginBottom = 10f;
            _stagePanel.Add(bar);

            var quests = _progression?.GetQuestSnapshot();
            foreach (var quest in quests?.ActiveInstances ?? Array.Empty<QuestInstanceSnapshot>())
                _stagePanel.Add(CreateQuestCard(quest, stage));
            if ((quests?.ActiveInstances?.Count ?? 0) == 0)
                _stagePanel.Add(Text("Активных квестов нет."));
        }

        private VisualElement CreateQuestCard(QuestInstanceSnapshot quest, StageProgressionSnapshot stage)
        {
            var card = SmallCard();
            var branch = stage.VisibleInstances.FirstOrDefault(value => value.InstanceId == quest.InstanceId);
            card.Add(CardTitle($"{(quest.IsTutorial ? "ОБУЧЕНИЕ · " : string.Empty)}{L(quest.NameId)}"));
            card.Add(Text($"{(branch?.Required == true ? "Обязательный" : "Дополнительный")} · {quest.Status}"));
            var steps = quest.Steps ?? Array.Empty<QuestStepSnapshot>();
            if (steps.Count == 0)
                card.Add(Text(L(quest.ShortDescriptionId)));
            foreach (var step in steps)
            {
                var stepText = Text($"{L(step.DescriptionId)}  {step.CurrentValue}/{step.TargetValue}");
                if (step.Completed)
                    stepText.style.color = new Color(0.18f, 0.63f, 0.26f);
                card.Add(stepText);
            }
            return card;
        }

        private void RefreshBuildings()
        {
            _buildingGrid.Clear();
            foreach (var building in RuntimeConfigs.Buildings.Buildings.OrderBy(value => value.buildingId, StringComparer.Ordinal))
            {
                if (building == null || (!building.visibleAtStart && !RuntimePlayer.IsBuildingUnlocked(building.buildingId)))
                    continue;
                var level = RuntimePlayer.GetBuildingLevel(building.buildingId);
                var choices = GetBuildingChoices(building.buildingId, level);
                var crafts = RuntimeConfigs.Crafts.GetAvailableCrafts(building.buildingId, level);
                var unlocked = RuntimePlayer.IsBuildingUnlocked(building.buildingId);
                var clickable = RuntimePlayer.CanClickBuilding(building.buildingId);
                var button = new Button(() => OpenBuilding(building.buildingId));
                button.text = $"{L(building.nameId)}\nУровень {level}\n{choices.Count + crafts.Count} действий";
                button.tooltip = $"{L(building.descriptionId)}\n{building.buildingId}";
                button.style.width = 190f;
                button.style.height = 110f;
                button.style.marginLeft = 8f;
                button.style.marginRight = 8f;
                button.style.marginTop = 8f;
                button.style.marginBottom = 8f;
                button.style.whiteSpace = WhiteSpace.Normal;
                if (!unlocked || !clickable)
                    button.text += "\n(ограничено)";
                _buildingGrid.Add(button);
            }
        }

        private void RefreshHeroes()
        {
            _heroPanel.Clear();
            var snapshot = OnlineActivityRuntime.GetSnapshot();
            var combats = OnlineActivityRuntime.GetCombatSnapshots();
            var crafts = OnlineActivityRuntime.GetCraftSnapshots();
            foreach (var hero in RuntimeConfigs.Heroes.Heroes.OrderBy(value => value.sortOrder))
            {
                if (hero == null || !RuntimePlayer.HasHero(hero.heroId))
                    continue;
                var execution = FindHeroExecution(snapshot, hero.heroId);
                var combat = FindHeroCombat(combats, hero.heroId);
                var craft = FindHeroCraft(crafts, hero.heroId);
                var progress = ExecutionProgressPercent(execution);
                var button = new Button(() => OpenHero(hero.heroId));
                button.style.height = execution == null && combat == null && craft == null ? 76f : 105f;
                button.style.marginBottom = 6f;
                button.style.whiteSpace = WhiteSpace.Normal;
                button.text = combat != null
                    ? $"{L(hero.nameId)}  ·  ур. {RuntimePlayer.GetHeroLevel(hero.heroId)}\nБой: {ActivityName(combat.activityId)}\n{CombatStatusText(combat)}"
                    : craft != null
                        ? $"{L(hero.nameId)}  ·  ур. {RuntimePlayer.GetHeroLevel(hero.heroId)}\nГотовит: {ItemName(craft.outputItemId)}\n{Mathf.RoundToInt(CraftProgressPercent(craft))}%  ·  осталось {FormatSeconds(CraftRemainingSeconds(craft))}"
                    : execution == null
                    ? $"{L(hero.nameId)}  ·  ур. {RuntimePlayer.GetHeroLevel(hero.heroId)}\nСвободен  ·  энергия {RuntimePlayer.GetHeroFatigue(hero.heroId)}/{RuntimePlayer.GetHeroMaxFatigue(hero.heroId)}"
                    : $"{L(hero.nameId)}  ·  ур. {RuntimePlayer.GetHeroLevel(hero.heroId)}\n{ActivityName(execution.activityId)}\n{Mathf.RoundToInt(progress)}%  ·  осталось {FormatSeconds(ExecutionRemainingSeconds(execution))}";
                _heroPanel.Add(button);
                if (combat?.hero != null)
                {
                    var hp = Percent(combat.hero.currentHp, combat.hero.maxHp);
                    var bar = new ProgressBar { value = hp, title = $"HP {combat.hero.currentHp}/{combat.hero.maxHp}" };
                    bar.style.marginLeft = 4f;
                    bar.style.marginRight = 4f;
                    bar.style.marginTop = -25f;
                    bar.style.marginBottom = 9f;
                    _heroPanel.Add(bar);
                }
                else if (craft != null)
                {
                    var bar = new ProgressBar { value = CraftProgressPercent(craft) };
                    bar.style.marginLeft = 4f;
                    bar.style.marginRight = 4f;
                    bar.style.marginTop = -25f;
                    bar.style.marginBottom = 9f;
                    _heroPanel.Add(bar);
                }
                else if (execution != null)
                {
                    var bar = new ProgressBar { value = progress };
                    bar.style.marginLeft = 4f;
                    bar.style.marginRight = 4f;
                    bar.style.marginTop = -25f;
                    bar.style.marginBottom = 9f;
                    _heroPanel.Add(bar);
                }
            }
        }

        private void RefreshStorageButton()
        {
            var storage = _storage.GetSnapshot();
            _storageButton.text = $"СКЛАД\n{storage.OccupiedSlots}/{storage.Capacity} мест · свободно {storage.FreeSlots}";
        }

        private void OpenBuilding(string buildingId)
        {
            _openModalKind = "building";
            _openModalId = buildingId;
            ShowModalOverlay();
            BuildBuildingModal(buildingId);
        }

        private void BuildBuildingModal(string buildingId)
        {
            if (!RuntimeConfigs.Buildings.TryGet(buildingId, out var building))
                return;
            _modalBody.Clear();
            var level = RuntimePlayer.GetBuildingLevel(buildingId);
            _modalTitle.text = L(building.nameId);
            _modalBody.Add(Text($"Уровень {level} · {building.buildingId}"));
            _modalBody.Add(Text(L(building.descriptionId)));
            _modalBody.Add(Subheading("Доступные действия"));
            var choices = GetBuildingChoices(buildingId, level);
            foreach (var choice in choices)
            {
                var current = choice;
                var button = new Button(() => OpenActivity(current));
                button.text = $"{ChoiceName(choice)}\n{ChoiceSummary(choice)}";
                button.style.height = 64f;
                button.style.marginBottom = 6f;
                button.style.whiteSpace = WhiteSpace.Normal;
                _modalBody.Add(button);
            }
            var crafts = RuntimeConfigs.Crafts.GetAvailableCrafts(buildingId, level);
            if (crafts.Count > 0)
            {
                _modalBody.Add(Subheading("Рецепты"));
                foreach (var craft in crafts)
                {
                    var current = craft;
                    var button = new Button(() => OpenCraft(current));
                    button.text = $"{ItemName(craft.Definition.TargetItemId)}\n{FormatSeconds(craft.Definition.CraftDurationSec)} · энергия {craft.Definition.FatigueCost}";
                    button.style.height = 64f;
                    button.style.marginBottom = 6f;
                    button.style.whiteSpace = WhiteSpace.Normal;
                    _modalBody.Add(button);
                }
            }
            if (choices.Count == 0 && crafts.Count == 0)
                _modalBody.Add(Text("Для текущего уровня здания действия в конфиге не заданы."));
        }

        private void OpenCraft(AvailableCraftDescriptor craft)
        {
            _openModalKind = "craft";
            _openModalId = craft.CraftId;
            _openCraftBuildingId = craft.BuildingId;
            _openCraftBuildingLevel = craft.BuildingLevel;
            ShowModalOverlay();
            BuildCraftModal(craft.CraftId, craft.BuildingId, craft.BuildingLevel);
        }

        private void BuildCraftModal(string craftId, string buildingId = null, int? buildingLevel = null)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
            {
                var available = RuntimeConfigs.Buildings.BuildingCraftables
                    .Where(value => value != null && value.enabled && value.craftId == craftId)
                    .FirstOrDefault(value =>
                        !string.IsNullOrWhiteSpace(value.buildingId) &&
                        RuntimePlayer.GetBuildingLevel(value.buildingId) == value.buildingLevel);
                if (available == null)
                {
                    _modalBody.Clear();
                    _modalTitle.text = "Рецепт недоступен";
                    _modalBody.Add(Text("Не удалось определить доступную станцию для этого рецепта."));
                    return;
                }
                buildingId = available.buildingId;
                buildingLevel = available.buildingLevel;
            }
            var level = buildingLevel ?? RuntimePlayer.GetBuildingLevel(buildingId);
            if (!RuntimeConfigs.Crafts.TryGetDefinition(craftId, out var definition))
                return;

            _modalBody.Clear();
            _modalTitle.text = ItemName(definition.TargetItemId);
            if (RuntimeConfigs.Items.TryGet(definition.TargetItemId, out var output))
                _modalBody.Add(Text(L(output.DescriptionId)));
            _modalBody.Add(Text($"Время: {FormatSeconds(definition.CraftDurationSec)} · результат: {definition.OutputCount} · энергия: {definition.FatigueCost} · EXP навыка: {definition.SkillExp}"));
            _modalBody.Add(Subheading("Материалы"));
            foreach (var material in definition.Materials)
                _modalBody.Add(Text($"• {ItemName(material.ItemId)}: {material.Count}"));

            _modalBody.Add(Subheading("Назначить героя"));
            var heroId = FirstFreeHeroId();
            var heroChoices = RuntimeConfigs.Heroes.Heroes
                .Where(hero => hero != null && RuntimePlayer.HasHero(hero.heroId))
                .Select(hero => hero.heroId)
                .ToList();
            if (heroChoices.Count == 0)
            {
                _modalBody.Add(Text("Нет приобретённых героев."));
                return;
            }

            var heroField = new PopupField<string>(
                "Герой",
                heroChoices,
                Math.Max(0, heroChoices.IndexOf(heroId)),
                FormatHero,
                FormatHero);
            _modalBody.Add(heroField);
            var initialDescriptor = OnlineActivityRuntime.GetCraftDescriptor(
                craftId,
                heroField.value,
                buildingId,
                level,
                1);
            var maxCycles = Math.Max(0, initialDescriptor?.MaxCycles ?? 0);
            var cycles = new IntegerField("Количество циклов")
            {
                value = maxCycles > 0 ? 1 : 0
            };
            cycles.SetEnabled(maxCycles > 0);
            cycles.RegisterValueChangedCallback(change =>
            {
                var clamped = maxCycles > 0 ? Mathf.Clamp(change.newValue, 1, maxCycles) : 0;
                if (clamped != change.newValue)
                    cycles.SetValueWithoutNotify(clamped);
                BuildCraftRuntimePreview(craftId, heroField.value, buildingId, level, clamped);
            });
            _modalBody.Add(cycles);
            _modalBody.Add(Text(maxCycles > 0
                ? $"Доступно циклов по ингредиентам: {maxCycles}"
                : "Недостаточно ингредиентов даже для одного цикла."));
            var preview = new VisualElement { name = "craft-runtime-preview" };
            _modalBody.Add(preview);
            var stationId = buildingId;
            var stationLevel = level;
            heroField.RegisterValueChangedCallback(_ =>
                BuildCraftRuntimePreview(craftId, heroField.value, stationId, stationLevel, cycles.value));
            BuildCraftRuntimePreview(craftId, heroField.value, stationId, stationLevel, cycles.value);

            var start = new Button(() => StartCraft(craftId, heroField.value, stationId, stationLevel, cycles.value))
            {
                text = "ПРИГОТОВИТЬ"
            };
            start.style.height = 42f;
            start.style.marginTop = 12f;
            start.SetEnabled(maxCycles > 0);
            _modalBody.Add(start);
        }

        private void BuildCraftRuntimePreview(
            string craftId,
            string heroId,
            string buildingId,
            int buildingLevel,
            int plannedCycles)
        {
            var preview = _modalBody.Q<VisualElement>("craft-runtime-preview");
            if (preview == null)
                return;
            preview.Clear();
            var descriptor = OnlineActivityRuntime.GetCraftDescriptor(
                craftId,
                heroId,
                buildingId,
                buildingLevel,
                plannedCycles);
            if (descriptor == null)
            {
                preview.Add(Text("Craft runtime пока недоступен."));
                return;
            }

            foreach (var cost in descriptor.PaidCosts)
                preview.Add(Text($"{ItemName(cost.ItemId)}: требуется {cost.Quantity}"));
            preview.Add(Text($"Итого: {FormatSeconds(descriptor.DurationSeconds)} · результат {descriptor.OutputCount} · энергия {descriptor.FatigueCost} · EXP {descriptor.SkillExp}"));
            preview.Add(Text(descriptor.CanStart
                ? "Герой может начать приготовление."
                : $"Сейчас приготовить нельзя: {descriptor.BlockCode} — {descriptor.BlockMessage}"));
        }

        private void StartCraft(
            string craftId,
            string heroId,
            string buildingId,
            int buildingLevel,
            int plannedCycles)
        {
            var result = OnlineActivityRuntime.StartCraft(
                craftId,
                heroId,
                buildingId,
                buildingLevel,
                plannedCycles);
            if (!result.success)
            {
                SetNotice($"Не удалось начать приготовление: {result.code} — {result.message}");
                BuildCraftModal(craftId, buildingId, buildingLevel);
                return;
            }

            SetNotice($"{ItemName(result.snapshot.outputItemId)}: приготовление началось.");
            CloseModal();
            RefreshAll();
        }

        private void OpenActivity(ActivityChoice choice)
        {
            _openModalKind = "activity";
            _openModalId = choice.Id;
            ShowModalOverlay();
            BuildActivityModal(choice);
        }

        private void BuildActivityModal(ActivityChoice choice)
        {
            _modalBody.Clear();
            _modalTitle.text = ChoiceName(choice);
            _modalBody.Add(Text(choice.Activity != null ? L(choice.Activity.descriptionId) : $"Улучшение здания до уровня {choice.Build.targetLevel}."));
            _modalBody.Add(Text(ChoiceSummary(choice)));

            if (choice.Activity != null)
            {
                _modalBody.Add(Subheading("Требования"));
                foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(choice.Id))
                    _modalBody.Add(Text($"• {requirement.reqType}: {requirement.targetId} ≥ {requirement.value}"));
                _modalBody.Add(Subheading("Возможная награда"));
                foreach (var reward in RuntimeConfigs.Activities.GetRewards(choice.Id))
                    _modalBody.Add(Text($"• {reward.targetId}: {reward.min}–{reward.max} ({reward.chance:0.#}%)"));
            }
            else
            {
                _modalBody.Add(Subheading("Материалы"));
                foreach (var material in choice.Build.materials ?? Array.Empty<MaterialCostDto>())
                    _modalBody.Add(Text($"• {ItemName(material.id)}: {material.count}"));
            }

            _modalBody.Add(Subheading("Назначить героя"));
            var heroId = FirstFreeHeroId();
            var heroChoices = RuntimeConfigs.Heroes.Heroes
                .Where(hero => hero != null && RuntimePlayer.HasHero(hero.heroId))
                .Select(hero => hero.heroId)
                .ToList();
            if (heroChoices.Count == 0)
            {
                _modalBody.Add(Text("Нет приобретённых героев."));
                return;
            }
            var heroField = new PopupField<string>("Герой", heroChoices, Math.Max(0, heroChoices.IndexOf(heroId)), FormatHero, FormatHero);
            _modalBody.Add(heroField);

            IntegerField cycles = null;
            PopupField<CombatLoadoutChoice> loadoutField = null;
            IntegerField loadoutQuantity = null;
            if (ActivityRuntimeClassifier.IsCycleWork(choice.Activity))
            {
                cycles = new IntegerField("Количество циклов") { value = 1 };
                cycles.RegisterValueChangedCallback(_ => BuildActivityRuntimePreview(choice, heroField.value, cycles));
                _modalBody.Add(cycles);
            }
            else if (choice.Activity != null && string.Equals(choice.Activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase))
            {
                var loadouts = GetCombatLoadoutChoices();
                loadoutField = new PopupField<CombatLoadoutChoice>(
                    "Расходник",
                    loadouts,
                    0,
                    FormatCombatLoadout,
                    FormatCombatLoadout);
                loadoutQuantity = new IntegerField("Взять в бой") { value = 0 };
                loadoutQuantity.SetEnabled(false);
                var quantityField = loadoutQuantity;
                loadoutField.RegisterValueChangedCallback(change =>
                {
                    var selected = change.newValue;
                    var enabled = selected != null && !string.IsNullOrWhiteSpace(selected.StackId);
                    quantityField.SetEnabled(enabled);
                    quantityField.SetValueWithoutNotify(enabled ? Math.Min(selected.Quantity, selected.MaxStack) : 0);
                });
                loadoutQuantity.RegisterValueChangedCallback(change =>
                {
                    var selected = loadoutField.value;
                    var maximum = selected == null ? 0 : Math.Min(selected.Quantity, selected.MaxStack);
                    var clamped = Mathf.Clamp(change.newValue, selected == null ? 0 : 1, maximum);
                    if (clamped != change.newValue)
                        quantityField.SetValueWithoutNotify(clamped);
                });
                _modalBody.Add(loadoutField);
                _modalBody.Add(loadoutQuantity);
                if (loadouts.Count == 1)
                    _modalBody.Add(Text("На складе нет доступных боевых расходников."));
            }

            var preview = new VisualElement { name = "runtime-preview" };
            _modalBody.Add(preview);
            heroField.RegisterValueChangedCallback(_ => BuildActivityRuntimePreview(choice, heroField.value, cycles));
            BuildActivityRuntimePreview(choice, heroField.value, cycles);

            var start = new Button(() => StartActivity(
                choice,
                heroField.value,
                cycles?.value,
                loadoutField?.value,
                loadoutQuantity?.value))
            {
                text = choice.Activity != null && string.Equals(choice.Activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase)
                    ? "НАЧАТЬ БОЙ"
                    : "НАЧАТЬ"
            };
            start.style.height = 42f;
            start.style.marginTop = 12f;
            start.SetEnabled(heroChoices.Count > 0);
            _modalBody.Add(start);
        }

        private void BuildActivityRuntimePreview(ActivityChoice choice, string heroId, IntegerField cycles)
        {
            var preview = _modalBody.Q<VisualElement>("runtime-preview");
            if (preview == null)
                return;
            preview.Clear();
            if (string.IsNullOrWhiteSpace(heroId))
            {
                preview.Add(Text("Нет героя для назначения."));
                return;
            }

            preview.Add(Text($"Энергия героя: {RuntimePlayer.GetHeroFatigue(heroId)}/{RuntimePlayer.GetHeroMaxFatigue(heroId)}"));
            if (choice.Build != null)
                return;

            if (string.Equals(choice.Activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase))
                preview.Add(Text("Бой будет симулироваться в отдельном окне и остановится при его закрытии."));

            if (ActivityRuntimeClassifier.IsCycleWork(choice.Activity))
            {
                var descriptor = OnlineActivityRuntime.GetWorkDescriptor(choice.Id, heroId, Math.Max(1, cycles?.value ?? 1));
                if (descriptor.success)
                {
                    var value = descriptor.descriptor;
                    preview.Add(Text($"Время: {FormatSeconds(value.plannedDurationSeconds)} · энергия: {value.plannedFatigue} · максимум циклов: {value.maxCycleCount}"));
                    foreach (var reward in value.expectedRewards)
                        preview.Add(Text($"{ItemName(reward.targetId)}: {reward.minAmount}–{reward.maxAmount}"));
                }
                else
                    AddIssues(preview, descriptor.issues);
                return;
            }

            var check = ActivityResolver.CanStart(new ActivityExecutionContext
            {
                activityId = choice.Id,
                heroId = heroId,
                executionId = PreviewExecutionId,
                startedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }, _activityState);
            preview.Add(Text(check.canStart ? "Герой готов к активности." : "Сейчас начать нельзя:"));
            AddIssues(preview, check.issues);
        }

        private void StartActivity(
            ActivityChoice choice,
            string heroId,
            int? cycles,
            CombatLoadoutChoice loadout,
            int? loadoutQuantity)
        {
            if (choice.Activity != null &&
                string.Equals(choice.Activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase))
            {
                var combat = OnlineActivityRuntime.StartCombat(
                    choice.Id,
                    heroId,
                    loadout?.StackId,
                    string.IsNullOrWhiteSpace(loadout?.StackId)
                        ? 0
                        : Math.Max(1, loadoutQuantity ?? 1));
                if (!combat.success)
                {
                    SetNotice($"Не удалось начать бой: {combat.code} — {combat.message}");
                    BuildActivityModal(choice);
                    return;
                }

                SetNotice($"{ChoiceName(choice)}: бой начался.");
                OpenCombat(combat.snapshot.executionId, true);
                RefreshAll();
                return;
            }

            var isWork = ActivityRuntimeClassifier.IsCycleWork(choice.Activity);
            var result = OnlineActivityRuntime.Start(new ActivityStartRequest
            {
                activityId = choice.Id,
                heroId = heroId,
                plannedCycleCount = isWork ? Math.Max(1, cycles ?? 1) : (int?)null
            });
            if (!result.success)
            {
                SetNotice(ResultMessage("Не удалось начать", result.issues));
                BuildActivityModal(choice);
                return;
            }
            SetNotice($"{ChoiceName(choice)}: герой назначен.");
            CloseModal();
            RefreshAll();
        }

        private void OpenHero(string heroId)
        {
            _openModalKind = "hero";
            _openModalId = heroId;
            ShowModalOverlay();
            BuildHeroModal(heroId);
        }

        private void BuildHeroModal(string heroId)
        {
            if (!RuntimeConfigs.Heroes.TryGet(heroId, out var hero))
                return;
            _modalBody.Clear();
            _modalTitle.text = L(hero.nameId);
            _modalBody.Add(Text($"Уровень {RuntimePlayer.GetHeroLevel(heroId)} · энергия {RuntimePlayer.GetHeroFatigue(heroId)}/{RuntimePlayer.GetHeroMaxFatigue(heroId)}"));
            _modalBody.Add(Text(L(hero.descriptionId)));

            _modalBody.Add(Subheading("Характеристики"));
            foreach (var statId in HeroStatsService.PrimaryStatIds)
                _modalBody.Add(Text($"{statId}: {RuntimePlayer.GetHeroStat(heroId, statId)}"));

            _modalBody.Add(Subheading("Навыки"));
            foreach (var skill in RuntimeConfigs.Activities.Skills)
                _modalBody.Add(Text($"{L(skill.skillNameId)}: ур. {RuntimePlayer.GetHeroSkillLevel(heroId, skill.skillId)}, EXP {RuntimePlayer.GetHeroSkillExp(heroId, skill.skillId)}"));

            _modalBody.Add(Subheading("Снаряжение"));
            foreach (var slot in EquipmentSlots())
            {
                var item = RuntimePlayer.GetEquippedItem(heroId, slot);
                _modalBody.Add(Text(item == null
                    ? $"{slot}: пусто"
                    : $"{slot}: {ItemName(item.itemId)} · качество {item.quality}{EquipmentBonus(item.itemId)}"));
            }

            var combat = FindHeroCombat(OnlineActivityRuntime.GetCombatSnapshots(), heroId);
            if (combat != null)
            {
                _modalBody.Add(Subheading("Текущий бой"));
                _modalBody.Add(Text($"{ActivityName(combat.activityId)} · {CombatStatusText(combat)}"));
                var executionId = combat.executionId;
                _modalBody.Add(new Button(() => OpenCombat(executionId, false))
                {
                    text = combat.status == CombatExecutionStatus.Running
                        ? "Продолжить бой"
                        : "Открыть результат боя"
                });
            }

            var craft = FindHeroCraft(OnlineActivityRuntime.GetCraftSnapshots(), heroId);
            if (craft != null)
            {
                var progress = CraftProgressPercent(craft);
                _modalBody.Add(Subheading("Текущее приготовление"));
                _modalBody.Add(Text($"{ItemName(craft.outputItemId)} ×{craft.outputCount} · циклов {craft.plannedCycles} · осталось {FormatSeconds(CraftRemainingSeconds(craft))}"));
                _modalBody.Add(new ProgressBar { title = $"{progress:0}%", value = progress });
                if (!string.IsNullOrWhiteSpace(craft.pendingResultId))
                {
                    _modalBody.Add(Subheading("Приготовлено"));
                    AddPendingRewardEntries(
                        _modalBody,
                        OnlineActivityRuntime.GetPendingReward(craft.pendingResultId));
                    var resultId = craft.pendingResultId;
                    _modalBody.Add(new Button(() => ClaimResult(resultId))
                    {
                        text = "Забрать результат на склад"
                    });
                }
            }

            var execution = FindHeroExecution(OnlineActivityRuntime.GetSnapshot(), heroId);
            if (execution != null)
            {
                var progress = ExecutionProgressPercent(execution);
                _modalBody.Add(Subheading("Текущая работа"));
                _modalBody.Add(Text($"{ActivityName(execution.activityId)} · осталось {FormatSeconds(ExecutionRemainingSeconds(execution))}"));
                if (execution.plannedCycles > 0)
                    _modalBody.Add(Text($"Завершено циклов: {execution.completedCycles}/{execution.plannedCycles}"));
                _modalBody.Add(new ProgressBar { title = $"{progress:0}%", value = progress });
                if (execution.status == ActivityRuntimeStatus.Running)
                    _modalBody.Add(new Button(() => CancelExecution(execution.executionId)) { text = "Отменить работу" });
                if (!string.IsNullOrWhiteSpace(execution.pendingResultId))
                {
                    var resultId = execution.pendingResultId;
                    var reward = OnlineActivityRuntime.GetPendingReward(resultId);
                    var claimable = execution.status == ActivityRuntimeStatus.ResultPending;
                    _modalBody.Add(Subheading(claimable ? "Полученная награда" : "Накопленная награда"));
                    if (reward?.entries != null && reward.entries.Length > 0)
                    {
                        foreach (var group in reward.entries
                                     .GroupBy(entry => new { entry.rewardType, entry.targetId, entry.quality })
                                     .OrderBy(group => group.Key.rewardType, StringComparer.Ordinal)
                                     .ThenBy(group => group.Key.targetId, StringComparer.Ordinal))
                        {
                            var quality = group.Key.quality > 0 ? $" · качество {group.Key.quality}" : string.Empty;
                            _modalBody.Add(Text($"• {RewardName(group.Key.rewardType, group.Key.targetId)}: {group.Sum(entry => entry.quantity)}{quality}"));
                        }
                    }
                    else
                    {
                        _modalBody.Add(Text("Состав награды пуст или ещё не сформирован."));
                    }
                    if (!claimable)
                        _modalBody.Add(Text("Награда станет доступна после завершения всех запланированных циклов."));
                    var claimButton = new Button(() => ClaimResult(resultId))
                    {
                        text = claimable ? "Забрать результат на склад" : "Завершите работу, чтобы забрать награду"
                    };
                    claimButton.SetEnabled(claimable);
                    _modalBody.Add(claimButton);
                }
            }
        }

        private void ClaimResult(string resultId)
        {
            var result = OnlineActivityRuntime.Claim(resultId);
            SetNotice(result.Success ? "Доступные награды перемещены на склад." : $"Не удалось забрать награды: {result.Code} — {result.Message}");
            if (result.Success && string.Equals(_openModalKind, "combat", StringComparison.Ordinal))
                CloseModal();
            RefreshAll();
        }

        private void CancelExecution(string executionId)
        {
            var result = OnlineActivityRuntime.Cancel(executionId);
            SetNotice(result.success ? "Работа отменена." : ResultMessage("Не удалось отменить", result.issues));
            CloseModal();
            RefreshAll();
        }

        private void OpenCombat(string executionId, bool resetLog)
        {
            if (resetLog || !string.Equals(_combatLogExecutionId, executionId, StringComparison.Ordinal))
            {
                _combatLogExecutionId = executionId;
                _combatLog.Clear();
                AppendCombatLine("Бой начался.");
            }

            _openModalKind = "combat";
            _openModalId = executionId;
            ShowModalOverlay();
            BuildCombatModal(executionId);
        }

        private void HandleCombatAdvanced(OnlineCombatAdvanceResult result)
        {
            if (result == null || result.snapshot == null ||
                !string.Equals(_openModalKind, "combat", StringComparison.Ordinal) ||
                !string.Equals(_openModalId, result.snapshot.executionId, StringComparison.Ordinal))
            {
                return;
            }
            if (!result.success)
            {
                SetNotice($"Ошибка симуляции боя: {result.code} — {result.message}");
                return;
            }

            foreach (var combatEvent in result.events ?? Array.Empty<CombatEvent>())
            {
                var line = FormatCombatEvent(combatEvent, result.snapshot);
                if (!string.IsNullOrWhiteSpace(line))
                    AppendCombatLine(line);
            }

            if (result.snapshot != null && result.snapshot.status != CombatExecutionStatus.Running)
            {
                AppendCombatLine(string.Equals(result.snapshot.outcome, CombatTerminalCandidateKinds.Victory, StringComparison.Ordinal)
                    ? "Победа. Награда сформирована."
                    : $"Бой завершён: {result.snapshot.outcome ?? result.snapshot.status.ToString()}.");
            }
        }

        private void BuildCombatModal(string executionId)
        {
            var combat = OnlineActivityRuntime.GetCombatSnapshot(executionId);
            _modalBody.Clear();
            _modalTitle.text = combat == null ? "Бой" : ActivityName(combat.activityId);
            if (combat == null)
            {
                _modalBody.Add(Text("Боевой snapshot больше недоступен."));
                return;
            }

            _modalBody.Add(Text($"Время боя: {FormatSeconds(combat.combatTimeSeconds)} · противник {combat.enemyIndex}/{combat.enemyCount} · {CombatStatusText(combat)}"));
            _modalBody.Add(Text(string.IsNullOrWhiteSpace(combat.consumableItemId)
                ? "Расходник: не взят"
                : $"Расходник: {ItemName(combat.consumableItemId)} · осталось {combat.consumableRemainingQuantity}/{combat.consumableInitialQuantity}"));
            var actors = Row();
            actors.style.marginTop = 8f;
            var heroPanel = CombatActorPanel(
                RuntimeConfigs.Heroes.TryGet(combat.heroId, out var heroConfig) ? L(heroConfig.nameId) : combat.heroId,
                combat.hero);
            heroPanel.style.marginRight = 6f;
            actors.Add(heroPanel);
            actors.Add(CombatActorPanel(EnemyName(combat.enemy?.definitionId), combat.enemy));
            _modalBody.Add(actors);

            _modalBody.Add(Subheading("Лог боя"));
            var log = new ScrollView(ScrollViewMode.Vertical);
            log.style.height = 250f;
            log.style.backgroundColor = new Color(0.06f, 0.075f, 0.06f);
            for (var index = _combatLog.Count - 1; index >= 0; index--)
                log.Add(Text(_combatLog[index]));
            if (_combatLog.Count == 0)
                log.Add(Text("Событий пока нет."));
            _modalBody.Add(log);

            if (combat.status == CombatExecutionStatus.Running)
            {
                _modalBody.Add(Text("Бой идёт только пока открыто это окно. Закрытие окна ставит симуляцию на паузу."));
            }
            else if (!string.IsNullOrWhiteSpace(combat.pendingResultId))
            {
                _modalBody.Add(Subheading("Результат боя"));
                var reward = OnlineActivityRuntime.GetPendingReward(combat.pendingResultId);
                AddPendingRewardEntries(_modalBody, reward);
                var resultId = combat.pendingResultId;
                var claim = new Button(() => ClaimResult(resultId)) { text = "Забрать награду на склад" };
                claim.SetEnabled(combat.status == CombatExecutionStatus.ResultPending);
                _modalBody.Add(claim);
            }
        }

        private static VisualElement CombatActorPanel(string title, OnlineCombatActorSnapshot actor)
        {
            var panel = SmallCard();
            panel.style.flexGrow = 1f;
            panel.style.width = new Length(50f, LengthUnit.Percent);
            panel.Add(CardTitle(title));
            if (actor == null)
            {
                panel.Add(Text("Нет активного бойца."));
                return panel;
            }

            panel.Add(new ProgressBar
            {
                value = Percent(actor.currentHp, actor.maxHp),
                title = $"HP {actor.currentHp}/{actor.maxHp}"
            });
            panel.Add(Text($"Урон: {actor.damageMin}–{actor.damageMax}\nАтак/сек: {actor.attacksPerSecond:0.##}\nКрит: {actor.critChancePercent:0.##}% · уклонение: {actor.dodgeChancePercent:0.##}%\nФиз. защита: {actor.physicalResistancePercent:0.##}% · маг. защита: {actor.magicResistancePercent:0.##}%"));
            return panel;
        }

        private void AppendCombatLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            _combatLog.Add(line);
            if (_combatLog.Count > MaxCombatLogEntries)
                _combatLog.RemoveRange(0, _combatLog.Count - MaxCombatLogEntries);
        }

        private static string FormatCombatEvent(CombatEvent combatEvent, OnlineCombatSnapshot snapshot)
        {
            if (combatEvent is CombatDamageEvent damage)
            {
                var actor = damage.ActorSide == CombatActorSide.Hero
                    ? HeroName(snapshot?.heroId)
                    : EnemyName(snapshot?.enemy?.definitionId);
                var target = damage.ActorSide == CombatActorSide.Hero
                    ? EnemyName(snapshot?.enemy?.definitionId)
                    : HeroName(snapshot?.heroId);
                return $"[{FormatSeconds(damage.TimestampSeconds)}] {actor} наносит {target} {damage.Damage} урона{(damage.Critical ? " (крит)" : string.Empty)} · HP {damage.TargetHpAfter}.";
            }
            if (combatEvent is CombatDodgeEvent dodge)
            {
                var target = dodge.ActorSide == CombatActorSide.Hero
                    ? EnemyName(snapshot?.enemy?.definitionId)
                    : HeroName(snapshot?.heroId);
                return $"[{FormatSeconds(dodge.TimestampSeconds)}] {target} уклоняется.";
            }
            if (combatEvent is CombatConsumableUsedEvent consumable)
                return $"[{FormatSeconds(consumable.TimestampSeconds)}] Использован предмет {ItemName(consumable.ItemId)}.";
            return null;
        }

        private static string HeroName(string heroId)
        {
            return RuntimeConfigs.Heroes.TryGet(heroId, out var hero) ? L(hero.nameId) : heroId ?? "Герой";
        }

        private static string EnemyName(string enemyId)
        {
            return RuntimeConfigs.Enemies.TryGet(enemyId, out var enemy) ? L(enemy.nameId) : enemyId ?? "Противник";
        }

        private static float Percent(int value, int max)
        {
            return max <= 0 ? 0f : Mathf.Clamp01(value / (float)max) * 100f;
        }

        private static string CombatStatusText(OnlineCombatSnapshot combat)
        {
            if (combat == null)
                return "нет данных";
            if (combat.status == CombatExecutionStatus.Running)
                return $"HP {combat.hero?.currentHp ?? 0}/{combat.hero?.maxHp ?? 0}";
            if (!string.IsNullOrWhiteSpace(combat.outcome))
                return combat.outcome;
            return combat.status.ToString();
        }

        private static void AddPendingRewardEntries(
            VisualElement parent,
            ActivityPendingRewardSnapshot reward)
        {
            if (reward?.entries == null || reward.entries.Length == 0)
            {
                parent.Add(Text("Награда пуста."));
                return;
            }

            foreach (var group in reward.entries
                         .GroupBy(entry => new { entry.rewardType, entry.targetId, entry.quality })
                         .OrderBy(group => group.Key.rewardType, StringComparer.Ordinal)
                         .ThenBy(group => group.Key.targetId, StringComparer.Ordinal))
            {
                var quality = group.Key.quality > 0 ? $" · качество {group.Key.quality}" : string.Empty;
                parent.Add(Text($"• {RewardName(group.Key.rewardType, group.Key.targetId)}: {group.Sum(entry => entry.quantity)}{quality}"));
            }
        }

        private void OpenStorage()
        {
            _openModalKind = "storage";
            _openModalId = string.Empty;
            ShowModalOverlay();
            BuildStorageModal();
        }

        private void BuildStorageModal()
        {
            _modalBody.Clear();
            _modalTitle.text = "Склад";
            var storage = _storage.GetSnapshot();
            _modalBody.Add(Text($"Занято {storage.OccupiedSlots}/{storage.Capacity} · свободно {storage.FreeSlots} · ревизия {storage.Revision}"));
            _modalBody.Add(Subheading("Предметы"));
            foreach (var stack in storage.Stacks)
                _modalBody.Add(ItemCard(stack.itemId, $"Количество: {stack.quantity} · {stack.stateId}"));
            foreach (var instance in storage.Instances)
                _modalBody.Add(ItemCard(instance.itemId, $"Экземпляр · качество {instance.quality} · {instance.stateId}"));
            if (storage.Stacks.Length == 0 && storage.Instances.Length == 0)
                _modalBody.Add(Text("Склад пуст."));
        }

        private VisualElement ItemCard(string itemId, string detail)
        {
            var card = SmallCard();
            card.Add(CardTitle(ItemName(itemId)));
            if (RuntimeConfigs.Items.TryGet(itemId, out var item))
                card.Add(Text($"{L(item.DescriptionId)}\n{item.Kind} · {item.Id}"));
            card.Add(Text(detail));
            return card;
        }

        private void RefreshOpenModal()
        {
            if (_modalLayer == null || _modalLayer.resolvedStyle.display == DisplayStyle.None)
                return;
            if (_openModalKind == "building")
                BuildBuildingModal(_openModalId);
            else if (_openModalKind == "hero")
                BuildHeroModal(_openModalId);
            else if (_openModalKind == "storage")
                BuildStorageModal();
            else if (_openModalKind == "combat")
                BuildCombatModal(_openModalId);
        }

        private void ShowModalOverlay() => _modalLayer.style.display = DisplayStyle.Flex;

        private void CloseModal()
        {
            if (_modalLayer != null)
                _modalLayer.style.display = DisplayStyle.None;
            _openModalKind = null;
            _openModalId = null;
            _openCraftBuildingId = null;
            _openCraftBuildingLevel = 0;
        }

        private List<ActivityChoice> GetBuildingChoices(string buildingId, int level)
        {
            var result = new List<ActivityChoice>();
            foreach (var mapping in RuntimeConfigs.Buildings.BuildingActivities
                         .Where(value => value != null && value.buildingId == buildingId && value.buildingLevel == level)
                         .OrderBy(value => value.sortOrder))
            {
                if (!ActivityAvailabilityResolver.IsExposedByBuilding(mapping, _boundState))
                    continue;
                if (RuntimeConfigs.Activities.TryGet(mapping.activityId, out var activity))
                    result.Add(new ActivityChoice(activity));
                else if (RuntimeConfigs.Buildings.TryGetBuildAction(mapping.activityId, out var build))
                    result.Add(new ActivityChoice(build));
            }
            return result;
        }

        private static ActivityExecutionSnapshot FindHeroExecution(ActivityRuntimeSnapshot snapshot, string heroId)
        {
            return snapshot?.executions?.FirstOrDefault(value => value != null && value.heroId == heroId);
        }

        private static OnlineCombatSnapshot FindHeroCombat(
            IEnumerable<OnlineCombatSnapshot> combats,
            string heroId)
        {
            return combats?.FirstOrDefault(value =>
                value != null &&
                string.Equals(value.heroId, heroId, StringComparison.Ordinal) &&
                value.status != CombatExecutionStatus.Completed);
        }

        private static OnlineCraftSnapshot FindHeroCraft(
            IEnumerable<OnlineCraftSnapshot> crafts,
            string heroId)
        {
            return crafts?.FirstOrDefault(value =>
                value != null &&
                string.Equals(value.heroId, heroId, StringComparison.Ordinal));
        }

        private static float CraftProgressPercent(OnlineCraftSnapshot craft)
        {
            if (craft == null || craft.durationSeconds <= 0)
                return 0f;
            return Mathf.Clamp01(craft.progressSeconds / craft.durationSeconds) * 100f;
        }

        private static double CraftRemainingSeconds(OnlineCraftSnapshot craft)
        {
            return craft == null
                ? 0d
                : Math.Max(0d, craft.durationSeconds - craft.progressSeconds);
        }

        private static float ExecutionProgressPercent(ActivityExecutionSnapshot execution)
        {
            if (execution == null)
                return 0f;

            var cycleProgress = Mathf.Clamp01(execution.progress);
            if (execution.plannedCycles <= 0)
                return cycleProgress * 100f;

            var completed = Mathf.Clamp(execution.completedCycles, 0, execution.plannedCycles);
            var overall = (completed + (completed < execution.plannedCycles ? cycleProgress : 0f)) /
                execution.plannedCycles;
            return Mathf.Clamp01(overall) * 100f;
        }

        private static double ExecutionRemainingSeconds(ActivityExecutionSnapshot execution)
        {
            if (execution == null || execution.plannedCycles <= 0)
                return execution?.remainingSeconds ?? 0d;
            if (execution.completedCycles >= execution.plannedCycles)
                return 0d;

            var cyclesAfterCurrent = Math.Max(
                0,
                execution.plannedCycles - execution.completedCycles - 1);
            return execution.remainingSeconds + cyclesAfterCurrent * execution.durationSeconds;
        }

        private static IEnumerable<string> EquipmentSlots()
        {
            return RuntimeConfigs.Items.EquipmentWeapons.Select(value => value.equipmentSlot)
                .Concat(RuntimeConfigs.Items.EquipmentArmor.Select(value => value.equipmentSlot))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        private List<CombatLoadoutChoice> GetCombatLoadoutChoices()
        {
            var choices = new List<CombatLoadoutChoice>
            {
                new CombatLoadoutChoice(null, null, 0, 0)
            };
            foreach (var stack in _storage.GetSnapshot().Stacks
                         .Where(value => value != null)
                         .OrderBy(value => value.itemId, StringComparer.Ordinal)
                         .ThenBy(value => value.stackId, StringComparer.Ordinal))
            {
                if (!RuntimeConfigs.Storage.TryGetItemState(stack.stateId, out var state) ||
                    !string.Equals(state.availabilityMode, ItemAvailabilityMode.Available, StringComparison.Ordinal) ||
                    !RuntimeConfigs.CombatConsumables.TryGet(stack.itemId, out var descriptor) ||
                    descriptor == null ||
                    descriptor.UsePlace != CombatConsumableUsePlace.Combat)
                {
                    continue;
                }

                choices.Add(new CombatLoadoutChoice(
                    stack.stackId,
                    stack.itemId,
                    stack.quantity,
                    descriptor.MaxStack));
            }
            return choices;
        }

        private static string FormatCombatLoadout(CombatLoadoutChoice choice)
        {
            if (choice == null || string.IsNullOrWhiteSpace(choice.StackId))
                return "Без расходника";
            RuntimeConfigs.CombatConsumables.TryGet(choice.ItemId, out var descriptor);
            var condition = descriptor?.Condition == null
                ? string.Empty
                : $" · авто при HP ≤ {descriptor.Condition.Value:0.#}%";
            return $"{ItemName(choice.ItemId)} · на складе {choice.Quantity} · максимум {choice.MaxStack}{condition}";
        }

        private static string FirstFreeHeroId()
        {
            var hero = RuntimeConfigs.Heroes.Heroes
                .OrderBy(value => value.sortOrder)
                .FirstOrDefault(value => value != null && RuntimePlayer.HasHero(value.heroId) && !RuntimePlayer.IsHeroBusy(value.heroId));
            return hero?.heroId;
        }

        private static string FormatHero(string heroId)
        {
            if (RuntimeConfigs.Heroes.TryGet(heroId, out var hero))
                return $"{L(hero.nameId)}{(RuntimePlayer.IsHeroBusy(heroId) ? " (занят)" : string.Empty)}";
            return heroId ?? string.Empty;
        }

        private static string ChoiceName(ActivityChoice choice)
        {
            return choice.Activity != null ? L(choice.Activity.nameId) : $"Строительство: {choice.Build.targetBuildingId}";
        }

        private static string ChoiceSummary(ActivityChoice choice)
        {
            if (choice.Activity != null)
            {
                if (string.Equals(choice.Activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase))
                    return $"Бой до победы · энергия {choice.Activity.fatigueCost}";
                var seconds = ActivityRuntimeService.TryGetRuntimeInfo(choice.Id, out var runtimeInfo)
                    ? runtimeInfo.durationSeconds
                    : choice.Activity.cycleSec > 0 ? choice.Activity.cycleSec : choice.Activity.durationSec;
                return $"{choice.Activity.type} · {FormatSeconds(seconds)} · энергия {choice.Activity.fatigueCost}";
            }
            return $"Уровень {choice.Build.targetLevel} · {choice.Build.buildPointsRequired} очков · энергия {choice.Build.fatigueCost}";
        }

        private static string ActivityName(string activityId)
        {
            if (RuntimeConfigs.Activities.TryGet(activityId, out var activity))
                return L(activity.nameId);
            if (RuntimeConfigs.Buildings.TryGetBuildAction(activityId, out var build))
                return $"Строительство {build.targetBuildingId}";
            return activityId;
        }

        private static string ItemName(string itemId)
        {
            return RuntimeConfigs.Items.TryGet(itemId, out var item) ? L(item.NameId) : itemId;
        }

        private static string RewardName(string rewardType, string targetId)
        {
            if (RuntimeConfigs.Items.TryGet(targetId, out var item))
                return L(item.NameId);
            if (RuntimeConfigs.Items.TryGetCurrency(targetId, out var currency))
                return L(currency.nameId);
            var skill = RuntimeConfigs.Activities.Skills.FirstOrDefault(value => value.skillId == targetId);
            if (skill != null)
                return L(skill.skillNameId);
            return string.IsNullOrWhiteSpace(targetId) ? rewardType : targetId;
        }

        private static string EquipmentBonus(string itemId)
        {
            if (RuntimeConfigs.Items.TryGetEquipmentWeapon(itemId, out var weapon))
                return $" · урон {weapon.weaponDamageMin}–{weapon.weaponDamageMax} · интервал {weapon.weaponAttackInterval:0.##}";
            if (RuntimeConfigs.Items.TryGetEquipmentArmor(itemId, out var armor))
                return $" · физ. защита +{armor.physicalResistBonus} · маг. защита +{armor.magicResistBonus} · HP +{armor.maxHpBonus}";
            return string.Empty;
        }

        private static string L(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? "—" : RuntimeConfigs.Localisation.Get(id);
        }

        private static string FormatSeconds(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return value.TotalHours >= 1d ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
        }

        private static void AddIssues(VisualElement parent, ActivityRequirementIssue[] issues)
        {
            foreach (var issue in issues ?? Array.Empty<ActivityRequirementIssue>())
                parent.Add(Text($"• {issue.issueType}: {issue.message}"));
        }

        private static string ResultMessage(string prefix, ActivityRequirementIssue[] issues)
        {
            var issue = issues?.FirstOrDefault();
            return issue == null ? prefix : $"{prefix}: {issue.issueType} — {issue.message}";
        }

        private void SetNotice(string message)
        {
            if (_notice != null)
                _notice.text = message ?? string.Empty;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        private static VisualElement Panel(float width = 0f)
        {
            var panel = new VisualElement();
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 10f;
            panel.style.paddingBottom = 10f;
            panel.style.backgroundColor = new Color(0.10f, 0.13f, 0.10f);
            if (width > 0f)
                panel.style.width = width;
            return panel;
        }

        private static VisualElement SmallCard()
        {
            var card = Panel();
            card.style.backgroundColor = new Color(0.15f, 0.18f, 0.13f);
            card.style.marginTop = 4f;
            card.style.marginBottom = 4f;
            return card;
        }

        private static Label Text(string value, bool bold = false)
        {
            var label = new Label(value ?? string.Empty);
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold)
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label Heading(string value)
        {
            var label = Text(value, true);
            label.style.fontSize = 15f;
            label.style.color = Accent();
            label.style.marginBottom = 6f;
            return label;
        }

        private static Label Subheading(string value)
        {
            var label = Text(value, true);
            label.style.marginTop = 10f;
            label.style.marginBottom = 4f;
            return label;
        }

        private static Label CardTitle(string value)
        {
            var label = Text(value, true);
            label.style.fontSize = 13f;
            return label;
        }

        private static Color Accent() => new Color(0.95f, 0.65f, 0.16f);

        private sealed class ActivityChoice
        {
            public ActivityChoice(ActivityConfigDto activity) { Activity = activity; }
            public ActivityChoice(BuildActionConfigDto build) { Build = build; }
            public ActivityConfigDto Activity { get; }
            public BuildActionConfigDto Build { get; }
            public string Id => Activity?.id ?? Build?.id;
        }

        private sealed class CombatLoadoutChoice
        {
            public CombatLoadoutChoice(string stackId, string itemId, int quantity, int maxStack)
            {
                StackId = stackId;
                ItemId = itemId;
                Quantity = quantity;
                MaxStack = maxStack;
            }

            public string StackId { get; }
            public string ItemId { get; }
            public int Quantity { get; }
            public int MaxStack { get; }
        }
    }
}
