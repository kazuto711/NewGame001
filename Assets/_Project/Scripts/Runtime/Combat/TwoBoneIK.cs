using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 腕（上腕・前腕・手首）を、手首が目標の位置に届くよう曲げる計算（2ボーン IK）。
    /// 骨の向き（軸）に依存しないワールド空間の回転で行うので、どのモデルでも使える。
    /// </summary>
    public static class TwoBoneIK
    {
        /// <param name="hint">肘を向けたい方向の点（ワールド）。曲がる向きの決め手になる</param>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight = 1f)
        {
            if (!upper || !lower || !end || weight <= 0f) return;
            Vector3 a = upper.position, b = lower.position, c = end.position;
            target = Vector3.Lerp(c, target, weight);

            float lab = Vector3.Distance(a, b);
            float lcb = Vector3.Distance(b, c);
            float lat = Mathf.Clamp(Vector3.Distance(a, target), 0.01f, lab + lcb - 0.001f);

            // 1) 肘の曲げ角度を、目標までの距離に合わせる
            float acAb0 = Angle(c - a, b - a);
            float baBc0 = Angle(a - b, c - b);
            float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            // 曲げる軸：肘のヒント方向で決める（無ければ今の曲がり方を保つ）
            Vector3 axis = Vector3.Cross(c - a, hint - a);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            axis.Normalize();

            // 今の肘が、ヒント側に曲がるよう、まず肘をヒント方向の平面へ回す
            Vector3 bendDir = Vector3.ProjectOnPlane(b - a, (c - a).normalized);
            Vector3 hintDir = Vector3.ProjectOnPlane(hint - a, (c - a).normalized);
            if (bendDir.sqrMagnitude > 1e-6f && hintDir.sqrMagnitude > 1e-6f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(bendDir, hintDir, (c - a).normalized), (c - a).normalized) * upper.rotation;
            a = upper.position; b = lower.position; c = end.position;
            axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(c - a, hint - a);
            axis.Normalize();

            upper.rotation = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis) * lower.rotation;

            // 2) 腕全体を目標の方向へ向ける
            Vector3 ac = end.position - upper.position;
            Vector3 at = target - upper.position;
            upper.rotation = Quaternion.FromToRotation(ac, at) * upper.rotation;
        }

        static float Angle(Vector3 x, Vector3 y) =>
            Mathf.Acos(Mathf.Clamp(Vector3.Dot(x.normalized, y.normalized), -1f, 1f));
    }
}
