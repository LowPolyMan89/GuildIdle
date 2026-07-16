using System;
using UnityEngine;

namespace GuildIdle.Player
{
    public enum SaveLoadOrigin
    {
        Fresh,
        ExistingV8
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

            try
            {
                var saveData = JsonUtility.FromJson<SaveData>(json);
                if (saveData == null)
                    throw new InvalidOperationException("JsonUtility returned null SaveData.");

                if (saveData.saveVersion < SaveData.CurrentSaveVersion)
                {
                    Debug.LogWarning($"[SaveService] Player save version '{saveData.saveVersion}' is older than supported version '{SaveData.CurrentSaveVersion}'. Creating default save.");
                    return CreateAndPersistDefault(factory, storage);
                }

                var state = factory.Create(saveData);
                state.BindSaveStorage(storage);
                if (state.WasNormalized)
                    Save(state, storage);

                origin = SaveLoadOrigin.ExistingV8;
                return state;
            }
            catch (SaveCompatibilityException exception)
            {
                Debug.LogError($"[SaveService] Player save is incompatible and was not modified. {exception.Message}");
                return null;
            }
            catch (Exception exception)
            {
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
            try
            {
                var json = JsonUtility.ToJson(state.ToSaveData());
                storage.SetString(SaveKey, json);
                storage.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveService] Failed to save player state. {exception.Message}");
                return false;
            }
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
            state.BindSaveStorage(storage);
            Save(state, storage);
            return state;
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
