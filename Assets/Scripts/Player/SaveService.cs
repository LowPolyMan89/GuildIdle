using System;
using UnityEngine;

namespace GuildIdle.Player
{
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

        public static PlayerState Load()
        {
            return Load(DefaultStorage);
        }

        public static PlayerState Load(ISaveStorage storage)
        {
            storage ??= DefaultStorage;

            if (!storage.HasKey(SaveKey))
                return CreateAndPersistDefault(storage);

            var json = storage.GetString(SaveKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return CreateAndPersistDefault(storage);

            try
            {
                var saveData = JsonUtility.FromJson<SaveData>(json);
                if (saveData == null)
                    throw new InvalidOperationException("JsonUtility returned null SaveData.");

                return new PlayerState(saveData);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveService] Failed to load player save JSON. Creating default save. {exception.Message}");
                return CreateAndPersistDefault(storage);
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

        public static PlayerState ResetSave()
        {
            return ResetSave(DefaultStorage);
        }

        public static PlayerState ResetSave(ISaveStorage storage)
        {
            storage ??= DefaultStorage;
            storage.DeleteKey(SaveKey);
            storage.Save();
            return CreateAndPersistDefault(storage);
        }

        private static PlayerState CreateAndPersistDefault(ISaveStorage storage)
        {
            var state = PlayerState.CreateDefault();
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
