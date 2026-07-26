using System;
using GuildIdle.Combat;
using GuildIdle.Configs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuildIdle.Editor.Combat
{
    public sealed class CombatConsumableDescriptorRepositoryTests
    {
        [Test]
        public void Repository_BuildsTypedDescriptorAndResolvesMaxStack()
        {
            var repository = CreateRepository(
                new[]
                {
                    Consumable(
                        "consumable_roasted_rabbit_meat",
                        "hp_percent<=40",
                        "RestoreHealthFlat:25",
                        5d,
                        1d)
                },
                StorageRule("stack", 20));

            Assert.That(
                repository.TryGet("consumable_roasted_rabbit_meat", out var descriptor),
                Is.True);
            Assert.That(descriptor.ItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
            Assert.That(descriptor.UsePlace, Is.EqualTo(CombatConsumableUsePlace.Combat));
            Assert.That(descriptor.Condition.Kind, Is.EqualTo(CombatConsumableConditionKind.HpPercent));
            Assert.That(
                descriptor.Condition.Operator,
                Is.EqualTo(CombatConsumableComparisonOperator.LessOrEqual));
            Assert.That(descriptor.Condition.Value, Is.EqualTo(40d));
            Assert.That(descriptor.Effect.Kind, Is.EqualTo(CombatEffectKind.Heal));
            Assert.That(descriptor.Effect.Value, Is.EqualTo(25d));
            Assert.That(descriptor.CheckIntervalSeconds, Is.EqualTo(1d));
            Assert.That(descriptor.CooldownSeconds, Is.EqualTo(5d));
            Assert.That(descriptor.MaxStack, Is.EqualTo(20));
        }

        [Test]
        public void Repository_HandlesAdditionalRegisteredConsumableWithoutItemIdBranch()
        {
            var repository = CreateRepository(
                new[]
                {
                    Consumable("consumable_first", "hp_percent<=40", "RestoreHealthFlat:25", 5d, 1d),
                    Consumable("consumable_second", "hp_percent<=12.5", "RestoreHealthFlat:7.5", 0.5d, 0.25d)
                },
                StorageRule("stack", 20));

            Assert.That(repository.Count, Is.EqualTo(2));
            Assert.That(repository.TryGet("consumable_second", out var descriptor), Is.True);
            Assert.That(descriptor.Condition.Value, Is.EqualTo(12.5d));
            Assert.That(descriptor.Effect.Value, Is.EqualTo(7.5d));
            Assert.That(descriptor.CooldownSeconds, Is.EqualTo(0.5d));
            Assert.That(descriptor.CheckIntervalSeconds, Is.EqualTo(0.25d));
        }

        [TestCase("hp_percent<=40", "RestoreHealthFlat:25", "single", 20)]
        [TestCase("hp_percent<=40", "RestoreHealthFlat:25", "stack", 0)]
        [TestCase("hp_percent<=40;hp_percent<=20", "RestoreHealthFlat:25", "stack", 20)]
        [TestCase("hp_percent<=40", "RestoreHealthFlat:0", "stack", 20)]
        public void Repository_FailsFastForInvalidDescriptorOrStorageRule(
            string condition,
            string effect,
            string mode,
            int maxStack)
        {
            var items = new ItemsConfigRepository(
                new ItemsRuntimeConfigDto
                {
                    consumables = new[]
                    {
                        Consumable("consumable_invalid", condition, effect, 5d, 1d)
                    }
                });
            var storage = new StorageConfigRepository(
                new StorageRuntimeConfigDto
                {
                    storageRules = new[] { StorageRule(mode, maxStack) }
                });

            Assert.Throws<InvalidOperationException>(
                () => new CombatConsumableDescriptorRepository(items, storage));
        }

        [Test]
        public void Repository_RequiresExactlyOneMatchingStorageRule()
        {
            var items = new ItemsConfigRepository(
                new ItemsRuntimeConfigDto
                {
                    consumables = new[]
                    {
                        Consumable(
                            "consumable_invalid",
                            "hp_percent<=40",
                            "RestoreHealthFlat:25",
                            5d,
                            1d)
                    }
                });

            Assert.Throws<InvalidOperationException>(
                () => new CombatConsumableDescriptorRepository(
                    items,
                    new StorageConfigRepository(new StorageRuntimeConfigDto())));

            LogAssert.Expect(
                LogType.Error,
                "[Configs] Duplicate id 'consumable' in Storage/storageRules/itemKind. Keeping the first entry.");
            var duplicateRules = new StorageConfigRepository(
                new StorageRuntimeConfigDto
                {
                    storageRules = new[]
                    {
                        StorageRule("stack", 20),
                        StorageRule("stack", 30, "storage_consumable_duplicate")
                    }
                });
            Assert.Throws<InvalidOperationException>(
                () => new CombatConsumableDescriptorRepository(items, duplicateRules));
        }

        [Test]
        public void DescriptorContracts_AreImmutable()
        {
            Assert.That(
                typeof(CombatConsumableDescriptor).GetProperty("ItemId")?.CanWrite,
                Is.False);
            Assert.That(
                typeof(CombatConsumableConditionDescriptor).GetProperty("Value")?.CanWrite,
                Is.False);
        }

        private static CombatConsumableDescriptorRepository CreateRepository(
            ConsumableConfigDto[] consumables,
            params StorageRuleConfigDto[] storageRules)
        {
            return new CombatConsumableDescriptorRepository(
                new ItemsConfigRepository(
                    new ItemsRuntimeConfigDto { consumables = consumables }),
                new StorageConfigRepository(
                    new StorageRuntimeConfigDto { storageRules = storageRules }));
        }

        private static ConsumableConfigDto Consumable(
            string id,
            string condition,
            string effect,
            double cooldownSeconds,
            double checkIntervalSeconds)
        {
            return new ConsumableConfigDto
            {
                id = id,
                kind = "consumable",
                usePlace = "combat",
                useCondition = condition,
                effects = new[] { effect },
                cooldownSeconds = cooldownSeconds,
                checkIntervalSeconds = checkIntervalSeconds
            };
        }

        private static StorageRuleConfigDto StorageRule(
            string mode,
            int maxStack,
            string id = "storage_consumable")
        {
            return new StorageRuleConfigDto
            {
                storageRuleId = id,
                itemKind = "consumable",
                mode = mode,
                maxStack = maxStack,
                occupiesSlot = true
            };
        }
    }
}
