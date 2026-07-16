using System;
using UnityEngine;

namespace GuildIdle.Player
{
    public enum SaveLoadOrigin
    {
        Fresh,
        MigratedV5,
        ExistingV6
    }

    public interface ISaveStorage
    {
        bool HasKey(string key);
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    public static class SaveService
    {
        public const string SaveKey = "GuildIdle.Player.SaveData";

        private static readonly ISaveStorage DefaultStorage = new PlayerPrefsSaveStorage();

        public static PlayerState Load(PlayerStateFactory factory)
        {
            return Load(factory, DefaultStorage, out _);
        }

        public static PlayerState Load(PlayerStateFactory factory, ISaveStorage storage)
        {
            return Load(factory, storage, out _);
        }

        public static PlayerState Load(PlayerStateFactory factory, ISaveStorage storage, out SaveLoadOrigin origin)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            storage ??= DefaultStorage;
            origin = SaveLoadOrigin.Fresh;

            if (!storage.HasKey(SaveKey))
                return CreateAndPersistDefault(factory, storage);

            var json = storage.GetString(SaveKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return CreateAndPersistDefault(factory, storage);

            var parsedVersion = int.MinValue;
            try
            {
                var saveData = JsonUtility.FromJson<SaveData>(json);
                if (saveData == null)
                    throw new InvalidOperationException("JsonUtility returned null SaveData.");
                parsedVersion = saveData.saveVersion;

                if (saveData.saveVersion <= 4)
                {
                    Debug.LogWarning($"[SaveService] Player save version '{saveData.saveVersion}' is older than supported version '{SaveData.CurrentSaveVersion}'. Creating default save.");
                    return CreateAndPersistDefault(factory, storage);
                }

                if (saveData.saveVersion == 5)
                {
                    if (!TryConvertV5ToV6(json, saveData, out var migrated, out var migrationError))
                    {
                        Debug.LogError($"[SaveService] Player save v5 migration failed and the original save was not modified. {migrationError}");
                        return null;
                    }

                    var migratedState = factory.Create(migrated);
                    if (!Save(migratedState, storage))
                        return null;
                    origin = SaveLoadOrigin.MigratedV5;
                    return migratedState;
                }

                var state = factory.Create(saveData);
                if (state.WasNormalized)
                    Save(state, storage);

                origin = SaveLoadOrigin.ExistingV6;
                return state;
            }
            catch (SaveCompatibilityException exception)
            {
                Debug.LogError($"[SaveService] Player save is incompatible and was not modified. {exception.Message}");
                return null;
            }
            catch (Exception exception)
            {
                if (parsedVersion == 5)
                {
                    Debug.LogError($"[SaveService] Player save v5 migration failed and the original save was not modified. {exception.Message}");
                    return null;
                }
                Debug.LogError($"[SaveService] Failed to load player save JSON. Creating default save. {exception.Message}");
                return CreateAndPersistDefault(factory, storage);
            }
        }

        public static bool Save(PlayerState state)
        {
            return Save(state, DefaultStorage);
        }

        public static bool Save(PlayerState state, ISaveStorage storage)
        {
            if (state == null)
            {
                Debug.LogError("[SaveService] Cannot save null PlayerState.");
                return false;
            }

            storage ??= DefaultStorage;

            var json = JsonUtility.ToJson(state.ToSaveData());
            storage.SetString(SaveKey, json);
            storage.Save();
            return true;
        }

        public static PlayerState ResetSave(PlayerStateFactory factory)
        {
            return ResetSave(factory, DefaultStorage);
        }

        public static PlayerState ResetSave(PlayerStateFactory factory, ISaveStorage storage)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            storage ??= DefaultStorage;
            storage.DeleteKey(SaveKey);
            storage.Save();
            return CreateAndPersistDefault(factory, storage);
        }

        private static PlayerState CreateAndPersistDefault(PlayerStateFactory factory, ISaveStorage storage)
        {
            var state = factory.CreateDefault();
            Save(state, storage);
            return state;
        }

        private static bool TryConvertV5ToV6(string json, SaveData commonData, out SaveData migrated, out string error)
        {
            migrated = null;
            error = null;
            try
            {
                var legacy = JsonUtility.FromJson<LegacyQuestEnvelopeV5>(json) ?? new LegacyQuestEnvelopeV5();
                var instances = new QuestInstanceSaveData[legacy.quests?.Length ?? 0];
                var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < instances.Length; index++)
                {
                    var quest = legacy.quests[index];
                    if (quest == null || string.IsNullOrWhiteSpace(quest.questId))
                    {
                        error = $"Legacy quest at index {index} has no quest_id.";
                        return false;
                    }

                    var instanceId = $"story:{quest.questId}";
                    if (!ids.Add(instanceId))
                    {
                        error = $"Legacy save contains duplicate quest state '{quest.questId}'.";
                        return false;
                    }

                    instances[index] = new QuestInstanceSaveData
                    {
                        instanceId = instanceId,
                        questId = quest.questId,
                        cycleId = null,
                        status = quest.completed ? QuestInstanceStatus.Completed : QuestInstanceStatus.Active,
                        rewardsGranted = quest.rewardsGranted,
                        steps = CloneLegacySteps(quest.steps)
                    };
                }

                commonData.saveVersion = SaveData.CurrentSaveVersion;
                commonData.questInstances = instances;
                migrated = commonData;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static QuestStepSaveData[] CloneLegacySteps(QuestStepSaveData[] source)
        {
            source ??= Array.Empty<QuestStepSaveData>();
            var result = new QuestStepSaveData[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                var step = source[index];
                result[index] = step == null ? null : new QuestStepSaveData
                {
                    stepId = step.stepId,
                    currentValue = step.currentValue,
                    completed = step.completed
                };
            }
            return result;
        }

        [Serializable]
        private sealed class LegacyQuestEnvelopeV5
        {
            public LegacyQuestSaveDataV5[] quests = Array.Empty<LegacyQuestSaveDataV5>();
        }

        [Serializable]
        private sealed class LegacyQuestSaveDataV5
        {
            public string questId;
            public bool completed;
            public bool rewardsGranted;
            public QuestStepSaveData[] steps = Array.Empty<QuestStepSaveData>();
        }

        private sealed class PlayerPrefsSaveStorage : ISaveStorage
        {
            public bool HasKey(string key)
            {
                return PlayerPrefs.HasKey(key);
            }

            public string GetString(string key, string defaultValue)
            {
                return PlayerPrefs.GetString(key, defaultValue);
            }

            public void SetString(string key, string value)
            {
                PlayerPrefs.SetString(key, value);
            }

            public void DeleteKey(string key)
            {
                PlayerPrefs.DeleteKey(key);
            }

            public void Save()
            {
                PlayerPrefs.Save();
            }
        }
    }
}
