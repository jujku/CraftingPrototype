using UnityEngine;
namespace Bar.Unity
{
    /// <summary>吧台可放置表面与开口禁区；检查物品水平占地而不只检查中心点。</summary>
    public sealed class PlacementSurface : MonoBehaviour
    {
        public Rect LocalBounds;
        public Rect[] Excluded = new Rect[0];
        /// <summary>让开口处射线继续命中嵌入的设施，而不是吧台整块碰撞盒。</summary>
        public bool IsOpening(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            foreach (var opening in Excluded)
                if (opening.Contains(new Vector2(local.x, local.z))) return true;
            return false;
        }
        public bool CanPlace(Vector3 worldPoint, Vector3 size)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            var footprint = new Rect(local.x - size.x * .5f, local.z - size.z * .5f, size.x, size.z);
            if (footprint.xMin < LocalBounds.xMin || footprint.xMax > LocalBounds.xMax ||
                footprint.yMin < LocalBounds.yMin || footprint.yMax > LocalBounds.yMax) return false;
            foreach (var opening in Excluded)
            {
                if (footprint.Overlaps(opening)) return false;
            }
            return true;
        }
    }
}
