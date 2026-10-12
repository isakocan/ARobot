using System;
using ARaki.Theming;
using UnityEngine;

namespace ARaki.Game
{
    /// <summary>Her tema için ayrı kayıt (PlayerPrefs, JSON): bölüm, öz, yükseltmeler, teslim sayıları.</summary>
    [Serializable]
    public class SaveData
    {
        public int chapter, progress, essence, total, codex;
        public bool finished;
        public int[] byKind;
        public int[] upgrades;

        static string Key(ThemeId id) => "ARaki.Save." + id;

        public static SaveData Load(ThemeId id)
        {
            string json = PlayerPrefs.GetString(Key(id), "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<SaveData>(json); }
            catch (Exception) { return null; }
        }

        public static void Store(ThemeId id, SaveData data)
        {
            PlayerPrefs.SetString(Key(id), JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void Delete(ThemeId id)
        {
            PlayerPrefs.DeleteKey(Key(id));
            PlayerPrefs.Save();
        }
    }
}
