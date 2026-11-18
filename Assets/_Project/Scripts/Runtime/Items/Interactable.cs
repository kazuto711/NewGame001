using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>
    /// 「調べる」ボタン（E / 十字キー上）で反応する物の共通の土台。
    /// 拾う・採集する・物色するは、すべてこれを継承して作る。
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        static readonly List<Interactable> all = new List<Interactable>();
        public static IReadOnlyList<Interactable> All => all;

        /// <summary>今、調べられるか。</summary>
        public virtual bool CanInteract => true;
        /// <summary>操作の名前（拾う、調べる など）。</summary>
        public abstract string Verb { get; }
        /// <summary>対象の名前。</summary>
        public abstract string Label { get; }
        /// <summary>長押しする秒数（0 ならすぐ終わる）。</summary>
        public virtual float HoldTime => 0f;
        /// <summary>届いたかを判定する位置。</summary>
        public virtual Vector3 Point => transform.position + Vector3.up * 0.3f;
        /// <summary>見通しの判定で、さえぎる物として数えない範囲（廃車のように本体が大きい物）。</summary>
        public virtual Transform SightRoot => transform;

        protected virtual void OnEnable() => all.Add(this);
        protected virtual void OnDisable() => all.Remove(this);

        /// <summary>調べ終わった時に呼ばれる。</summary>
        public abstract void Interact(PlayerInteractor who);
    }
}
