using System;
using Matoi.AI;
using Matoi.Crafting;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Missions
{
    public enum ObjectiveKind
    {
        HaveItem,        // 指定の物を持っている
        DefeatEnemies,   // 敵を倒す
        ReachMarker,     // 目的地へ行く
        Craft,           // 作業台で作る
        Signal,          // 合図を待つ（寝床で休む、ボスを倒す など）
    }

    /// <summary>無線の字幕 1 行。</summary>
    [Serializable]
    public class RadioLine
    {
        public string speaker;
        [TextArea] public string text;
        public RadioLine(string speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    /// <summary>目的が始まった時に出す敵。</summary>
    [Serializable]
    public class EncounterSpawn
    {
        [Tooltip("Infected / Fast / Heavy / Ranged")]
        public string enemyId;
        public EnemyBrain prefab;
        [Tooltip("出す場所の目印")]
        public string marker;
    }

    /// <summary>ミッションの目的 1 つ分。</summary>
    [Serializable]
    public class ObjectiveStep
    {
        public string text;
        public ObjectiveKind kind;
        [Tooltip("コンパスに出す目印のグループ")]
        public string markerGroup;
        public int count = 1;
        public ItemDefinition item;
        public RecipeDefinition recipe;
        [Tooltip("倒す敵の名前（空ならどの敵でもよい）")]
        public string enemyName;
        public string signal;
        [Tooltip("目的地に着いたと判定する距離 (m)")]
        public float radius = 6f;
        public RadioLine[] radio;
        public EncounterSpawn[] spawns;
    }

    /// <summary>
    /// ミッション 1 話分のデータ。話を増やす時はこのファイルを複製して目的を書き換える。
    /// Project ウィンドウで右クリック → Create → Matoi → Mission。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Mission", fileName = "Mission")]
    public class MissionDefinition : ScriptableObject
    {
        public string title = "第一話";
        public ObjectiveStep[] steps;
        [Tooltip("すべて終わった時の無線")]
        public RadioLine[] endingRadio;
        [TextArea] public string endingText;
    }
}
