using System;

namespace GuildIdle.Core
{
    public enum RewardMutationKind
    {
        Item,
        Currency,
        Hero,
        HeroSkillExp,
        UnlockBuilding,
        UnlockLocation
    }

    public sealed class RewardMutation
    {
        public RewardMutation(
            RewardMutationKind kind,
            string targetId,
            long amount,
            string ownerId = null)
        {
            Kind = kind;
            TargetId = targetId;
            Amount = amount;
            OwnerId = ownerId;
        }

        public RewardMutationKind Kind { get; }
        public string TargetId { get; }
        public long Amount { get; }
        public string OwnerId { get; }
    }

    public sealed class RewardMutationResult
    {
        public RewardMutationResult(RewardMutation mutation, bool applied)
        {
            Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
            Applied = applied;
        }

        public RewardMutation Mutation { get; }
        public bool Applied { get; }
    }

    public interface IRewardBatchStore
    {
        bool TryApplyRewardBatch(
            RewardMutation[] mutations,
            out RewardMutationResult[] results,
            out string error);
    }
}
