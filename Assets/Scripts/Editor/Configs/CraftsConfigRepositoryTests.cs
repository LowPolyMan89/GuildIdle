using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuildIdle.Editor.Configs
{
    public sealed class CraftsConfigRepositoryTests
    {
        [TearDown]
        public void TearDown()
        {
            global::GuildIdle.Configs.Configs.SetDatabaseForTests(null);
        }

        [Test]
        public void RepositoryBuildsDefensiveImmutableDescriptorsAndStableAvailabilityOrder()
        {
            var sourceMaterial = new MaterialCostDto { id = "resource_b", count = 1 };
            var sourceRequirement = new RequiredBuildingDto { buildingId = "building_station", level = 1 };
            var definitionZ = Definition(
                "craft_z",
                new[]
                {
                    sourceMaterial,
                    new MaterialCostDto { id = "resource_a", count = 2 },
                    new MaterialCostDto { id = "resource_b", count = 3 }
                },
                new[] { sourceRequirement });
            var definitionA = Definition("craft_a", new[] { new MaterialCostDto { id = "resource_a", count = 1 } });
            var relationZ = Craftable("craft_z", 10, true, 1);
            var relationA = Craftable("craft_a", 10, true, 1);
            var disabled = Craftable("craft_a", 0, false, 1);
            var otherLevel = Craftable("craft_a", 0, true, 2);
            var items = new ItemsConfigRepository(new ItemsRuntimeConfigDto
            {
                craftDefinitions = new[] { definitionZ, definitionA }
            });
            var buildings = new BuildingsConfigRepository(new BuildingsRuntimeConfigDto
            {
                buildingCraftables = new[] { relationZ, disabled, relationA, otherLevel }
            });

            var repository = new CraftsConfigRepository(items, buildings);
            items.CraftDefinitions[0] = definitionA;
            buildings.BuildingCraftables[0] = relationA;
            sourceMaterial.id = "mutated";
            sourceMaterial.count = 99;
            sourceRequirement.buildingId = "mutated";
            definitionZ.materials = Array.Empty<MaterialCostDto>();
            definitionZ.requiredBuildings = Array.Empty<RequiredBuildingDto>();
            relationZ.buildingId = "mutated";
            relationZ.sortOrder = -1;

            Assert.That(repository.TryGetDefinition("craft_z", out var descriptor), Is.True);
            Assert.That(descriptor.Materials, Has.Count.EqualTo(2));
            Assert.That(descriptor.Materials[0].ItemId, Is.EqualTo("resource_a"));
            Assert.That(descriptor.Materials[0].Count, Is.EqualTo(2));
            Assert.That(descriptor.Materials[1].ItemId, Is.EqualTo("resource_b"));
            Assert.That(descriptor.Materials[1].Count, Is.EqualTo(4));
            Assert.That(descriptor.RequiredBuildings[0].BuildingId, Is.EqualTo("building_station"));
            Assert.Throws<NotSupportedException>(() => ((IList<CraftMaterialDescriptor>)descriptor.Materials).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<CraftRequiredBuildingDescriptor>)descriptor.RequiredBuildings).Clear());

            var available = repository.GetAvailableCrafts("building_station", 1);
            Assert.That(available, Has.Count.EqualTo(2));
            Assert.That(available[0].CraftId, Is.EqualTo("craft_a"));
            Assert.That(available[1].CraftId, Is.EqualTo("craft_z"));
            Assert.That(available[1].BuildingId, Is.EqualTo("building_station"));
            Assert.That(repository.GetAvailableCrafts("building_station", 2), Has.Count.EqualTo(1));
            Assert.That(repository.GetAvailableCrafts("building_station", 3), Is.Empty);
            Assert.Throws<NotSupportedException>(() => ((IList<AvailableCraftDescriptor>)available).Clear());
        }

        [Test]
        public void GetDefinitionUsesExistingMissingConfigConvention()
        {
            var repository = new CraftsConfigRepository(
                new ItemsConfigRepository(null),
                new BuildingsConfigRepository(null));
            LogAssert.Expect(LogType.Error, "[Configs] Missing id 'missing' in Crafts/definitions.");

            Assert.That(repository.GetDefinition("missing"), Is.Null);
            Assert.That(repository.TryGetDefinition("missing", out _), Is.False);
        }

        [Test]
        public void DescriptorRejectsAggregatedMaterialOverflow()
        {
            var items = new ItemsConfigRepository(new ItemsRuntimeConfigDto
            {
                craftDefinitions = new[]
                {
                    Definition("craft_overflow", new[]
                    {
                        new MaterialCostDto { id = "resource_a", count = int.MaxValue },
                        new MaterialCostDto { id = "resource_a", count = 1 }
                    })
                }
            });

            Assert.Throws<OverflowException>(() =>
                new CraftsConfigRepository(items, new BuildingsConfigRepository(null)));
        }

        [Test]
        public void ConfigsCraftsIsReplacedWithItsConfigDatabase()
        {
            var firstDatabase = DatabaseWithCraft("craft_first");
            global::GuildIdle.Configs.Configs.SetDatabaseForTests(firstDatabase);
            var firstRepository = global::GuildIdle.Configs.Configs.Crafts;

            var secondDatabase = DatabaseWithCraft("craft_second");
            global::GuildIdle.Configs.Configs.SetDatabaseForTests(secondDatabase);
            var secondRepository = global::GuildIdle.Configs.Configs.Crafts;

            Assert.That(secondRepository, Is.Not.SameAs(firstRepository));
            Assert.That(secondRepository.TryGetDefinition("craft_first", out _), Is.False);
            Assert.That(secondRepository.TryGetDefinition("craft_second", out _), Is.True);
        }

        private static ConfigDatabase DatabaseWithCraft(string craftId)
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto { craftDefinitions = new[] { Definition(craftId, Array.Empty<MaterialCostDto>()) } },
                null,
                null,
                new BuildingsRuntimeConfigDto(),
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        private static CraftDefinitionConfigDto Definition(
            string craftId,
            MaterialCostDto[] materials,
            RequiredBuildingDto[] requiredBuildings = null)
        {
            return new CraftDefinitionConfigDto
            {
                craftId = craftId,
                targetItemId = "item_output",
                craftStationId = "building_station",
                craftDurationSec = 10,
                craftSkillId = "skill_crafting",
                requiredBuildings = requiredBuildings ?? Array.Empty<RequiredBuildingDto>(),
                materials = materials,
                outputCount = 1
            };
        }

        private static BuildingCraftableConfigDto Craftable(string craftId, int sortOrder, bool enabled, int level)
        {
            return new BuildingCraftableConfigDto
            {
                buildingId = "building_station",
                buildingLevel = level,
                craftId = craftId,
                sortOrder = sortOrder,
                uiCategory = "Test",
                enabled = enabled
            };
        }
    }
}
