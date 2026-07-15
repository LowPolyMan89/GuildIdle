using System;
using GuildIdle.Configs;
using GuildIdle.Player;
using NUnit.Framework;

namespace GuildIdle.Editor.Player
{
    public sealed class HeroStatsServiceTests
    {
        [TestCase(-1, 1)]
        [TestCase(0, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]
        [TestCase(249, 2)]
        [TestCase(250, 3)]
        [TestCase(999, 3)]
        public void ResolveSkillLevel_UsesProgressionBoundaries(long exp, int expected)
        {
            var service = CreateService(progressions: new[]
            {
                new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 },
                new SkillProgressionConfigDto { level = 2, totalExpRequired = 100 },
                new SkillProgressionConfigDto { level = 3, totalExpRequired = 250 }
            });

            Assert.That(service.ResolveSkillLevel(exp), Is.EqualTo(expected));
        }

        [Test]
        public void ResolveSkillLevel_WithoutProgressionFallsBackToOne()
        {
            Assert.That(CreateService().ResolveSkillLevel(1000), Is.EqualTo(1));
        }

        [Test]
        public void CalculateHeroStat_UsesBaseAndAllGrowthRowsAtOrBelowLevel()
        {
            var service = CreateService(growth: new[]
            {
                Growth("ren", 2, strength: 1, endurance: 1),
                Growth("ren", 3, strength: 2, endurance: 2),
                Growth("aska", 2, strength: 100, endurance: 100)
            });

            Assert.That(service.CalculateHeroStat("ren", "Strength", 1), Is.EqualTo(4));
            Assert.That(service.CalculateHeroStat("ren", "Strength", 2), Is.EqualTo(5));
            Assert.That(service.CalculateHeroStat("ren", "Strength", 3), Is.EqualTo(7));
            Assert.That(service.CalculateHeroStat("ren", "Endurance", 3), Is.EqualTo(8));
            Assert.That(service.CalculateHeroStat("missing", "Strength", 3), Is.Zero);
            Assert.That(service.CalculateHeroStat("ren", "Unknown", 3), Is.Zero);
        }

        [Test]
        public void CalculateMaxFatigue_UsesOnlyExistingStageOneFormulaFields()
        {
            var formula = Formula(baseValue: 100f, primaryMultiplier: 4f, levelMultiplier: 1f);
            formula.secondaryStat = "Strength";
            formula.secondaryStatMultiplier = 999f;
            formula.minValue = 1000f;
            formula.maxValue = 1001f;
            formula.capValue = 1002f;
            var service = CreateService(
                growth: new[]
                {
                    Growth("ren", 2, endurance: 1),
                    Growth("ren", 3, endurance: 2)
                },
                formula: formula);

            Assert.That(service.CalculateMaxFatigue("ren", 1), Is.EqualTo(121));
            Assert.That(service.CalculateMaxFatigue("ren", 3), Is.EqualTo(135));
        }

        [Test]
        public void CalculateMaxFatigue_DisabledOrMissingFormulaFallsBackToOneHundred()
        {
            var disabled = Formula(baseValue: 999f);
            disabled.enabled = false;

            Assert.That(CreateService(formula: disabled).CalculateMaxFatigue("ren", 1), Is.EqualTo(100));
            Assert.That(CreateService().CalculateMaxFatigue("ren", 1), Is.EqualTo(100));
        }

        [TestCase("Floor", 10)]
        [TestCase("Ceil", 11)]
        [TestCase("Ceiling", 11)]
        [TestCase("Round", 11)]
        [TestCase("Unknown", 11)]
        public void CalculateMaxFatigue_AppliesExistingRoundingRules(string rounding, int expected)
        {
            var formula = Formula(baseValue: 10.5f, primaryMultiplier: 0f, levelMultiplier: 0f);
            formula.rounding = rounding;

            Assert.That(CreateService(formula: formula).CalculateMaxFatigue("ren", 1), Is.EqualTo(expected));
        }

        [Test]
        public void CalculateMaxFatigue_HasMinimumOne()
        {
            var formula = Formula(baseValue: -100f, primaryMultiplier: 0f, levelMultiplier: 0f);

            Assert.That(CreateService(formula: formula).CalculateMaxFatigue("ren", 1), Is.EqualTo(1));
        }

        private static HeroStatsService CreateService(
            HeroGrowthConfigDto[] growth = null,
            SkillProgressionConfigDto[] progressions = null,
            FormulaConfigDto formula = null)
        {
            return new HeroStatsService(new FakeHeroStatsConfigProvider(
                growth ?? Array.Empty<HeroGrowthConfigDto>(),
                progressions ?? Array.Empty<SkillProgressionConfigDto>(),
                formula));
        }

        private static HeroGrowthConfigDto Growth(
            string heroId,
            int level,
            int strength = 0,
            int endurance = 0)
        {
            return new HeroGrowthConfigDto
            {
                heroId = heroId,
                level = level,
                addStrength = strength,
                addEndurance = endurance
            };
        }

        private static FormulaConfigDto Formula(
            float baseValue,
            float primaryMultiplier = 0f,
            float levelMultiplier = 0f)
        {
            return new FormulaConfigDto
            {
                formulaId = HeroStatsService.MaxFatigueFormulaId,
                baseValue = baseValue,
                primaryStat = "Endurance",
                primaryStatMultiplier = primaryMultiplier,
                levelMultiplier = levelMultiplier,
                rounding = "Round",
                enabled = true
            };
        }

        private sealed class FakeHeroStatsConfigProvider : IHeroStatsConfigProvider
        {
            private readonly FormulaConfigDto _formula;

            public FakeHeroStatsConfigProvider(
                HeroGrowthConfigDto[] growth,
                SkillProgressionConfigDto[] progressions,
                FormulaConfigDto formula)
            {
                HeroGrowth = growth;
                SkillProgression = progressions;
                _formula = formula;
            }

            public HeroGrowthConfigDto[] HeroGrowth { get; }
            public SkillProgressionConfigDto[] SkillProgression { get; }

            public bool TryGetHero(string heroId, out HeroConfigDto hero)
            {
                if (string.Equals(heroId, "ren", StringComparison.Ordinal))
                {
                    hero = new HeroConfigDto
                    {
                        heroId = "ren",
                        enabled = true,
                        baseStats = new HeroBaseStatsDto
                        {
                            strength = 4,
                            agility = 3,
                            intelligence = 2,
                            luck = 1,
                            endurance = 5
                        }
                    };
                    return true;
                }

                hero = null;
                return false;
            }

            public bool TryGetFormula(string formulaId, out FormulaConfigDto formula)
            {
                formula = _formula;
                return formula != null &&
                    string.Equals(formulaId, formula.formulaId, StringComparison.Ordinal);
            }
        }
    }
}
