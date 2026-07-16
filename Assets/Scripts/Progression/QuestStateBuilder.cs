using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public static class QuestInstanceIds
    {
        public static string Story(string questId) => string.IsNullOrWhiteSpace(questId) ? throw new ArgumentException("quest_id is required.", nameof(questId)) : $"story:{questId}";
        public static string Daily(string cycleId, string questId) =>
            string.IsNullOrWhiteSpace(cycleId) || string.IsNullOrWhiteSpace(questId)
                ? throw new ArgumentException("cycle_id and quest_id are required for daily instances.")
                : $"daily:{cycleId}:{questId}";
    }

    public static class QuestStateBuilder
    {
        public static QuestInstanceSaveData Create(string instanceId, string questId, string cycleId, QuestStepConfigDto[] configuredSteps)
        {
            var steps = Ordered(configuredSteps);
            var state = new QuestStepSaveData[steps.Length];
            for (var index = 0; index < steps.Length; index++)
                state[index] = new QuestStepSaveData { stepId = steps[index].stepId };
            return new QuestInstanceSaveData
            {
                instanceId = instanceId,
                questId = questId,
                cycleId = cycleId,
                status = QuestInstanceStatus.Active,
                rewardsGranted = false,
                steps = state
            };
        }

        public static QuestInstanceSaveData Reconcile(QuestInstanceSaveData existing, QuestStepConfigDto[] configuredSteps, out bool changed)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            var byId = new Dictionary<string, QuestStepSaveData>(StringComparer.Ordinal);
            foreach (var step in existing.steps ?? Array.Empty<QuestStepSaveData>())
                if (step != null && !string.IsNullOrWhiteSpace(step.stepId) && !byId.ContainsKey(step.stepId)) byId.Add(step.stepId, Clone(step));

            var output = new List<QuestStepSaveData>();
            foreach (var configured in Ordered(configuredSteps))
            {
                if (byId.TryGetValue(configured.stepId, out var saved))
                {
                    output.Add(saved); byId.Remove(configured.stepId);
                }
                else output.Add(new QuestStepSaveData { stepId = configured.stepId });
            }
            var unknownSteps = new List<QuestStepSaveData>(byId.Values);
            unknownSteps.Sort((left, right) => string.CompareOrdinal(left.stepId, right.stepId));
            output.AddRange(unknownSteps);

            changed = output.Count != (existing.steps?.Length ?? 0);
            if (!changed)
            {
                for (var index = 0; index < output.Count; index++)
                {
                    var source = existing.steps[index];
                    var normalized = output[index];
                    if (source == null || source.stepId != normalized.stepId || source.currentValue != normalized.currentValue || source.completed != normalized.completed)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            var result = new QuestInstanceSaveData
            {
                instanceId = existing.instanceId,
                questId = existing.questId,
                cycleId = existing.cycleId,
                status = existing.status,
                rewardsGranted = existing.rewardsGranted,
                steps = output.ToArray()
            };
            return result;
        }

        private static QuestStepConfigDto[] Ordered(QuestStepConfigDto[] source)
        {
            source ??= Array.Empty<QuestStepConfigDto>();
            var result = (QuestStepConfigDto[])source.Clone();
            Array.Sort(result, (left, right) =>
            {
                var order = (left?.stepOrder ?? int.MaxValue).CompareTo(right?.stepOrder ?? int.MaxValue);
                return order != 0 ? order : string.CompareOrdinal(left?.stepId, right?.stepId);
            });
            return result;
        }

        private static QuestStepSaveData Clone(QuestStepSaveData value) => new QuestStepSaveData
        {
            stepId = value.stepId,
            currentValue = Math.Max(0, value.currentValue),
            completed = value.completed
        };
    }
}
