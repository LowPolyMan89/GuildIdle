using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public static class QuestStateBuilder
    {
        public static QuestSaveData Create(string questId, QuestStepConfigDto[] configuredSteps)
        {
            configuredSteps ??= Array.Empty<QuestStepConfigDto>();
            var ordered = Ordered(configuredSteps);
            var steps = new QuestStepSaveData[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
            {
                steps[i] = new QuestStepSaveData
                {
                    stepId = ordered[i].stepId,
                    currentValue = 0,
                    completed = false
                };
            }

            return new QuestSaveData
            {
                questId = questId,
                completed = false,
                rewardsGranted = false,
                steps = steps
            };
        }

        public static QuestSaveData Reconcile(
            QuestSaveData existing,
            QuestStepConfigDto[] configuredSteps,
            out bool changed)
        {
            if (existing == null)
                throw new ArgumentNullException(nameof(existing));

            var byId = new Dictionary<string, QuestStepSaveData>(StringComparer.Ordinal);
            foreach (var step in existing.steps ?? Array.Empty<QuestStepSaveData>())
            {
                if (step != null && !string.IsNullOrWhiteSpace(step.stepId) && !byId.ContainsKey(step.stepId))
                    byId.Add(step.stepId, step);
            }

            var ordered = Ordered(configuredSteps ?? Array.Empty<QuestStepConfigDto>());
            var reconciled = new QuestStepSaveData[ordered.Length];
            changed = ordered.Length != byId.Count;
            for (var i = 0; i < ordered.Length; i++)
            {
                var config = ordered[i];
                if (!byId.TryGetValue(config.stepId, out var saved))
                {
                    reconciled[i] = new QuestStepSaveData { stepId = config.stepId };
                    changed = true;
                    continue;
                }

                reconciled[i] = new QuestStepSaveData
                {
                    stepId = saved.stepId,
                    currentValue = Math.Max(0, saved.currentValue),
                    completed = saved.completed
                };
                var original = existing.steps != null && i < existing.steps.Length ? existing.steps[i] : null;
                if (original == null || !string.Equals(original.stepId, saved.stepId, StringComparison.Ordinal))
                    changed = true;
            }

            return new QuestSaveData
            {
                questId = existing.questId,
                completed = existing.completed,
                rewardsGranted = existing.rewardsGranted,
                steps = reconciled
            };
        }

        private static QuestStepConfigDto[] Ordered(QuestStepConfigDto[] configuredSteps)
        {
            var list = new List<QuestStepConfigDto>();
            foreach (var step in configuredSteps)
            {
                if (step != null && !string.IsNullOrWhiteSpace(step.stepId))
                    list.Add(step);
            }

            list.Sort((left, right) =>
            {
                var order = left.stepOrder.CompareTo(right.stepOrder);
                return order != 0 ? order : string.CompareOrdinal(left.stepId, right.stepId);
            });
            return list.ToArray();
        }
    }
}
