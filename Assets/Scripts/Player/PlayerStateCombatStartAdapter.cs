using System;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    public sealed class PlayerStateCombatStartAdapter : ICombatStartPlayerState
    {
        private const string MaxHpFormulaId = "hero_max_hp";
        private const string MaxHpFormulaType = "linear_stat_with_level";

        private readonly PlayerState _state;
        private readonly PlayerStateActivityAdapter _activityAdapter;
        private readonly StorageService _storage;
        private readonly FormulasConfigRepository _formulas;
        private readonly ItemsConfigRepository _items;
        private readonly BuildingsConfigRepository _buildings;
        private bool _storageChanged;

        public PlayerStateCombatStartAdapter(
            PlayerState state,
            FormulasConfigRepository formulas,
            ItemsConfigRepository items,
            BuildingsConfigRepository buildings)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _activityAdapter = new PlayerStateActivityAdapter(state);
            _storage = state.Storage as StorageService ??
                       throw new InvalidOperationException(
                           "PlayerState must use StorageService for combat start transactions.");
            _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        }

        public SaveData CaptureCheckpoint()
        {
            _storageChanged = false;
            return _state.ToSaveData();
        }

        public void RestoreCheckpoint(SaveData checkpoint)
        {
            _state.RestoreTransactional(checkpoint);
            _storageChanged = false;
        }

        public bool TryGetOperationReceipt(
            string aggregateId,
            string operationId,
            out OperationReceiptSaveData receipt) =>
            _state.TryGetOperationReceipt(aggregateId, operationId, out receipt);

        public void RecordOperationReceipt(OperationReceiptSaveData receipt) =>
            _state.RecordOperationReceipt(receipt);

        public bool HasHero(string heroId) => _state.HasHero(heroId);
        public bool HasHeroState(string heroId) => _state.HasHeroState(heroId);
        public bool IsKnownSkill(string skillId)
        {
            foreach (var skill in _state.ConfigProvider.Skills ??
                                  Array.Empty<SkillConfigDto>())
            {
                if (skill != null &&
                    string.Equals(
                        skill.skillId,
                        skillId,
                        StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        public int GetHeroFatigue(string heroId) => _state.GetHeroFatigue(heroId);
        public bool SpendHeroFatigue(string heroId, int amount) =>
            _state.SpendHeroFatigue(heroId, amount);
        public bool IsHeroBusy(string heroId) => _state.IsHeroBusy(heroId);
        public string GetHeroOccupationOwnerId(string heroId) =>
            _state.GetHeroCurrentActivityExecutionId(heroId);
        public int GetActiveHeroCount() => _state.GetActiveHeroCount();
        public int GetActiveHeroLimit() =>
            ActiveHeroLimitResolver.GetCurrentLimit(_activityAdapter);
        public bool IsActivityAvailable(string activityId) =>
            ActivityAvailabilityResolver.IsAvailableForDirectStart(
                activityId,
                _state,
                _buildings);
        public ActivityCheckResult CanStartActivity(
            ActivityExecutionContext context) =>
            ActivityResolver.CanStart(context, _activityAdapter);
        public bool IsActivityCompleted(string activityId) =>
            _state.IsActivityCompleted(activityId);
        public bool HasUnfinishedActivityExecution(string activityId)
        {
            foreach (var execution in _state.GetActivityExecutions())
            {
                if (execution != null &&
                    string.Equals(
                        execution.activityId,
                        activityId,
                        StringComparison.Ordinal) &&
                    execution.status != ActivityRuntimeStatus.Completed &&
                    execution.status != ActivityRuntimeStatus.Cancelled)
                {
                    return true;
                }
            }

            return false;
        }
        public ActivityExecutionSaveData GetActivityExecution(string executionId) =>
            _state.GetActivityExecution(executionId);

        public bool BindLinkedCombatExecution(
            string sourceExecutionId,
            string sourceRequestId,
            string combatExecutionId)
        {
            var source = _state.GetActivityExecution(sourceExecutionId);
            if (source?.linkedCombat == null ||
                !string.Equals(
                    source.linkedCombat.requestId,
                    sourceRequestId,
                    StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(source.linkedCombat.combatExecutionId))
            {
                return false;
            }

            source.linkedCombat.combatExecutionId = combatExecutionId;
            return _state.UpdateActivityExecution(source);
        }

        public long GetStorageRevision() => _state.StorageRevision;

        public bool TryGetCombatSourceStack(
            string stackId,
            StorageActionContext actionContext,
            out ItemStackSaveData stack,
            out string code,
            out string error) =>
            _storage.TryGetCombatSourceStack(
                stackId,
                actionContext,
                out stack,
                out code,
                out error);

        public bool TryExtractCombatSourceStack(
            string stackId,
            int quantity,
            StorageActionContext actionContext,
            out string itemId,
            out string error)
        {
            var success = _storage.TryExtractCombatSourceStack(
                stackId,
                quantity,
                actionContext,
                out itemId,
                out error);
            _storageChanged |= success;
            return success;
        }

        public bool TryCreateHeroCombatant(
            string heroId,
            string sessionId,
            out CombatantStateSaveData hero,
            out string error)
        {
            hero = null;
            error = null;
            if (string.IsNullOrWhiteSpace(sessionId) ||
                !TryCalculateMaxHp(heroId, out var maxHp, out error))
            {
                error ??= "Combat session id is required for the hero snapshot.";
                return false;
            }

            hero = new CombatantStateSaveData
            {
                combatantId = $"{sessionId}:hero",
                definitionId = heroId,
                currentHp = maxHp,
                maxHp = maxHp
            };
            error = null;
            return true;
        }

        private bool TryCalculateMaxHp(
            string heroId,
            out int maxHp,
            out string error)
        {
            maxHp = 0;
            error = null;
            var hero = _state.GetHeroState(heroId);
            if (hero == null ||
                !_formulas.TryGetFormula(MaxHpFormulaId, out var formula) ||
                formula == null ||
                !formula.enabled ||
                !string.Equals(
                    formula.formulaType,
                    MaxHpFormulaType,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(formula.primaryStat))
            {
                error = $"Hero '{heroId}' does not have a valid '{MaxHpFormulaId}' descriptor.";
                return false;
            }

            var primary = _state.CalculateHeroStat(heroId, formula.primaryStat);
            var secondary = string.IsNullOrWhiteSpace(formula.secondaryStat)
                ? 0
                : _state.CalculateHeroStat(heroId, formula.secondaryStat);
            var value = formula.baseValue +
                        primary * (double)formula.primaryStatMultiplier +
                        secondary * (double)formula.secondaryStatMultiplier +
                        Math.Max(1, hero.level) * (double)formula.levelMultiplier;
            value = Math.Max(formula.minValue, value);
            if (formula.maxValue > 0f)
                value = Math.Min(formula.maxValue, value);
            if (formula.capValue > 0f)
                value = Math.Min(formula.capValue, value);
            if (!TryRound(value, formula.rounding, out var rounded))
            {
                error = $"Hero max HP formula uses unsupported rounding '{formula.rounding}'.";
                return false;
            }

            long armorBonus = 0;
            foreach (var slot in _state.GetEquipmentSlots())
            {
                if (slot == null ||
                    !string.Equals(slot.heroId, heroId, StringComparison.Ordinal))
                {
                    continue;
                }

                var item = _state.GetEquippedItem(heroId, slot.equipmentSlot);
                if (item != null &&
                    _items.TryGetEquipmentArmor(item.itemId, out var armor) &&
                    armor != null)
                {
                    armorBonus += Math.Max(0, armor.maxHpBonus);
                }
            }

            var total = rounded + armorBonus;
            if (total <= 0 || total > int.MaxValue)
            {
                error = $"Hero '{heroId}' produces max HP outside Int32.";
                return false;
            }

            maxHp = (int)total;
            return true;
        }

        private static bool TryRound(
            double value,
            string rounding,
            out long result)
        {
            result = 0;
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value > long.MaxValue ||
                value < long.MinValue)
            {
                return false;
            }

            if (string.Equals(rounding, "floor", StringComparison.OrdinalIgnoreCase))
                result = (long)Math.Floor(value);
            else if (string.Equals(rounding, "ceil", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rounding, "ceiling", StringComparison.OrdinalIgnoreCase))
                result = (long)Math.Ceiling(value);
            else if (string.Equals(rounding, "round", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rounding, "round_2", StringComparison.OrdinalIgnoreCase))
                result = (long)Math.Round(value, MidpointRounding.AwayFromZero);
            else
                return false;
            return true;
        }

        public CombatRuntimeAggregate[] GetCombatAggregates() =>
            _state.GetCombatAggregates();
        public CombatRuntimeAggregate GetCombatAggregate(string executionId) =>
            _state.GetCombatAggregate(executionId);
        public bool AddCombatAggregate(CombatRuntimeAggregate aggregate) =>
            _state.AddCombatAggregate(aggregate);

        public void PublishCombatStartCommit()
        {
            if (_storageChanged)
                _storage.NotifyExternalMutation();
            _storageChanged = false;
        }

        public bool Save() => _state.Save();
    }
}
