using System;
using System.Collections.Generic;
using UnityEngine;

namespace Matoi.SaveSystem
{
    /// <summary>
    /// セーブファイルの中身。アイテムや武器は実物ではなく名前で保存する。
    /// 項目を増やす時はここに足す（古いセーブデータでは初期値のまま読み込まれる）。
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAt;

        public Vector3 position;
        public float yaw;
        public float health;
        public Vector3 respawnPosition;
        public float respawnYaw;

        public List<SavedCount> inventory = new List<SavedCount>();
        public List<SavedCount> storage = new List<SavedCount>();
        public List<SavedAmmo> ammo = new List<SavedAmmo>();
        public List<SavedUpgrade> upgrades = new List<SavedUpgrade>();

        public List<string> discovered = new List<string>();
        public List<string> searched = new List<string>();
        public List<string> takenPickups = new List<string>();
        public List<SavedCount> partialPickups = new List<SavedCount>();
        public List<SavedDrop> drops = new List<SavedDrop>();
        public List<SavedTimer> gatherTimers = new List<SavedTimer>();

        public bool missionActive;
        public bool missionComplete;
        public int missionStep;
        public int missionProgress;
    }

    [Serializable]
    public class SavedCount
    {
        public string id;
        public int count;
    }

    [Serializable]
    public class SavedAmmo
    {
        public string weapon;
        public int magazine;
        public int reserve;
    }

    [Serializable]
    public class SavedUpgrade
    {
        public string weapon;
        public string upgrade;
        public int level;
    }

    /// <summary>捨てた物や、敵が落とした物。</summary>
    [Serializable]
    public class SavedDrop
    {
        public string item;
        public int count;
        public Vector3 position;
    }

    [Serializable]
    public class SavedTimer
    {
        public string id;
        public float seconds;
    }
}
