using UnityEngine;

namespace Matoi.CameraSystem
{
    /// <summary>
    /// メインカメラに付ける。カメラからプレイヤーへの視線をさえぎる CameraOccluder を一時的に見えなくする。
    /// </summary>
    public class OcclusionHider : MonoBehaviour
    {
        [SerializeField] Transform target;
        [Tooltip("視線を何本か横にずらして調べる幅（m）。キャラの体の太さ分")]
        [SerializeField] float sideOffset = 0.45f;
        [Tooltip("見るべき高さ（足元からの m）")]
        [SerializeField] float[] targetHeights = { 0.4f, 1.0f, 1.7f };
        [Tooltip("さえぎらなくなってから元に戻すまでの時間（ちらつき防止）")]
        [SerializeField] float restoreDelay = 0.25f;
        [Tooltip("木の当たり判定用：大きな物の箱を少し縮めて誤判定を減らす")]
        [SerializeField, Range(0.5f, 1f)] float boundsShrink = 0.8f;

        public Transform Target { get => target; set => target = value; }

        // 無効にされた時（三人称視点に切り替えた時など）は、隠していた物を全て戻す
        void OnDisable()
        {
            foreach (var occ in CameraOccluder.All) occ.SetHidden(false);
        }

        void LateUpdate()
        {
            if (!target) return;
            Vector3 cam = transform.position;
            Vector3 right = transform.right;
            float now = Time.time;
            var list = CameraOccluder.All;

            for (int i = 0; i < list.Count; i++)
            {
                var occ = list[i];
                var b = occ.Bounds;
                b.extents *= boundsShrink;
                bool blocks = false;
                foreach (float h in targetHeights)
                {
                    for (int s = -1; s <= 1 && !blocks; s++)
                    {
                        Vector3 p = target.position + Vector3.up * h + right * (s * sideOffset);
                        Vector3 d = p - cam;
                        float dist = d.magnitude;
                        if (b.IntersectRay(new Ray(cam, d / dist), out float hit) && hit < dist - 0.3f
                            && !b.Contains(p)) // プレイヤーがその物の中（屋根の下など）にいる場合は除く
                            blocks = true;
                    }
                    if (blocks) break;
                }
                if (blocks) occ.LastOccludingTime = now;
                occ.SetHidden(now - occ.LastOccludingTime < restoreDelay);
            }
        }
    }
}
