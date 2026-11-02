using UnityEngine;
namespace Bar.Unity
{
    /// <summary>真实出液的唯一输入；没有指定落点或反求速度。单位米、秒、毫升。</summary>
    public struct PourStreamInput
    {
        public bool Guided;public Vector3 GuideContact,GuideEnd;
        public Pose Outlet;
        public Vector3 ExitVelocity;
        public float ConfirmedMlPerSecond;
        public float LiquidPlaneY;
        public Vector3 MouthCenter;
        public float MouthRadius;
        /// <summary>当前水平液面内的保守圆边界，供涟漪/飞溅裁切；不是杯口半径。</summary>
        public bool HasImpactBoundary;
        public Vector3 ImpactCenter;
        public float ImpactRadius;
    }
    /// <summary>可替换的水柱模块。颜色在实现外单独设置，几何输入不含强制终点。</summary>
    public interface IPourStream
    {
        Color Tint {get;set;}
        bool Visible {get;}
        void Draw(PourStreamInput input);
        void Hide();
    }
    /// <summary>唯一选择水柱实现的装配点；会话和站位不依赖管线网格实现。</summary>
    public static class PourStreamFactory
    {
        public static IPourStream Create(GameObject owner)=>owner.AddComponent<PourRibbon>();
    }
    /// <summary>仅受重力的解析轨迹，用于站位预测、绘制和验收；不访问 Domain 或核心。</summary>
    public static class PourTrajectory
    {
        public static bool PlaneTime(Vector3 start,Vector3 velocity,float planeY,float gravity,out float time)
        {
            float discriminant=velocity.y*velocity.y+2*gravity*(start.y-planeY);
            time=discriminant<0?-1:(velocity.y+Mathf.Sqrt(discriminant))/gravity;
            return time>0;
        }
        public static Vector3 Point(Vector3 start,Vector3 velocity,float t,float gravity)=>start+velocity*t+Vector3.down*(gravity*t*t*.5f);
        public static Vector3 Velocity(Vector3 initial,float t,float gravity)=>initial+Vector3.down*(gravity*t);
        public static float Radius(float mlPerSecond,float speed,float minimum)=>Mathf.Max(minimum,Mathf.Sqrt(mlPerSecond*1e-6f/(Mathf.PI*Mathf.Max(1e-5f,speed))));
    }
}
