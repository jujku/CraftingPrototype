using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读几何的吧勺轨迹：勺头立在杯底上方，薄面朝杯壁，整块勺头留在真实内腔内。</summary>
    public sealed class StirPresentation
    {
        [Serializable] public sealed class Settings
        {
            public string note;
            public float bottomClearance=.003f,wallClearance=.0015f,orbitFraction=.94f,inputRadiusPixels=36,shaftRadius=.0015f;
            public float minimumOrbit=.004f,minimumSubmergence=.004f;
            public int fitSamples=129;
            // Stir motion: mouse -> angular speed with inertia -> phase; see BarStirSettings.json "motionNote".
            public float derivativeSeconds=.08f,inertiaSeconds=.16f,maxTurnsPerSecond=3.5f,fullSpeedTurnsPerSecond=1.5f;
            public float maxTiltDegrees=10,topOrbitFraction=.25f,liftMetres=.008f,liftPhase=0;
            public float enterSeconds=.4f,returnSeconds=.36f,rimClearance=.012f,clinkTurns=.5f,clinkMinimumSpeed=.15f;
            private static Settings cached;
            public static Settings Load()=>cached??=JsonUtility.FromJson<Settings>(Resources.Load<TextAsset>("BarStirSettings").text);
        }
        private MeshFilter bowl;private Vector3[] vertices,offsets;private Vector3 thinAxis;
        public float Radius {get;private set;}
        public float BottomGap {get;private set;}
        public float LiquidHeight {get;private set;}
        /// <summary>本帧实际用上的抬升（米）：为保持勺头在液面下、不穿壁，可能小于请求值。</summary>
        public float Lift {get;private set;}
        /// <summary>勺头最低处所在的内腔局部高度（不含抬升）。</summary>
        public float BaseHeight {get;private set;}
        private bool Cache(ItemView tool)
        {
            if(bowl)return true;
            foreach(var mesh in tool.GetComponentsInChildren<MeshFilter>())if(mesh.name=="SpoonBowl"){bowl=mesh;break;}
            if(!bowl||!bowl.sharedMesh||!bowl.sharedMesh.isReadable)return false;
            vertices=bowl.sharedMesh.vertices;offsets=new Vector3[vertices.Length];var size=bowl.sharedMesh.bounds.size;
            thinAxis=size.x<=size.y&&size.x<=size.z?Vector3.right:size.y<=size.z?Vector3.up:Vector3.forward;return true;
        }
        /// <summary>
        /// 按给定方向（input 的方向，长度/inputRadiusPixels 为幅度）把勺头放到内腔里贴壁的位置；不自动播放、不更改核心圈速/强度或容器内容。
        /// tiltDegrees：勺杆上端向杯轴内倾（StirMotion 用，静态调用为 0）；lift：在最低可行高度上再抬起的米数，自动缩小到勺头仍在液面下、仍不穿壁。
        /// </summary>
        public void Apply(ItemView tool,ContainerVisual receiver,Transform tip,Transform grip,Vector2 input,float liquidHeight,float tiltDegrees=0,float lift=0)
        {
            if(!Cache(tool))return;var config=Settings.Load();var frame=receiver.transform;var shape=receiver.Shape;LiquidHeight=liquidHeight;
            var shaft=tip.position-grip.position;if(shaft.sqrMagnitude<1e-10f)return;
            var align=Quaternion.FromToRotation(shaft,-frame.up);var normal=Vector3.ProjectOnPlane(align*bowl.transform.TransformDirection(thinAxis),frame.up);
            var radial=input.sqrMagnitude>.001f?input.normalized:Vector2.right;
            var direction=frame.TransformDirection(new Vector3(radial.x,0,radial.y));
            float yaw=normal.sqrMagnitude>1e-10f?Vector3.SignedAngle(normal,direction,frame.up):0;
            tool.transform.rotation=Quaternion.AngleAxis(yaw,frame.up)*align*tool.transform.rotation;
            // The top of the shaft leans in toward the axis, so the hand traces a smaller circle than the bowl (an inverted cone).
            if(tiltDegrees!=0)tool.transform.rotation=Quaternion.AngleAxis(tiltDegrees,Vector3.Cross(frame.up,-direction))*tool.transform.rotation;
            var matrix=frame.worldToLocalMatrix*bowl.transform.localToWorldMatrix;var origin=frame.InverseTransformPoint(tip.position);
            float bottom=float.MaxValue,top=float.MinValue;
            for(int i=0;i<vertices.Length;i++){var v=matrix.MultiplyPoint3x4(vertices[i])-origin;offsets[i]=v;bottom=Mathf.Min(bottom,v.y);top=Mathf.Max(top,v.y);}
            float start=shape.Bottom+config.bottomClearance-bottom,end=shape.Top-config.wallClearance-top;
            float y=start,limit=0;bool found=false;float fitY=start,fitLimit=0;bool anyFit=false;
            float usefulOrbit=Mathf.Min(config.minimumOrbit,shape.RadiusAt(liquidHeight)*.2f);
            // A tapered cup may need the spoon raised slightly so its full bowl, not just Tip, fits.
            for(int step=0;step<config.fitSamples;step++)
            {
                y=Mathf.Lerp(start,Mathf.Max(start,end),step/(float)(config.fitSamples-1));limit=float.MaxValue;bool fits=true;
                foreach(var v in offsets)
                {
                    float r=Mathf.Max(0,shape.RadiusAt(y+v.y)-config.wallClearance);
                    float across=v.x*radial.x+v.z*radial.y,tangent=-v.x*radial.y+v.z*radial.x;
                    if(v.x*v.x+v.z*v.z>r*r){fits=false;break;}
                    limit=Mathf.Min(limit,Mathf.Sqrt(Mathf.Max(0,r*r-tangent*tangent))-across);
                }
                if(fits)
                {
                    fitY=y;fitLimit=limit;anyFit=true;
                    if(limit*config.orbitFraction>=usefulOrbit||y>=liquidHeight-config.minimumSubmergence){found=true;break;}
                }
            }
            if(!found){y=anyFit?fitY:Mathf.Clamp(start,shape.Bottom,shape.Top);limit=anyFit?fitLimit:0;}
            limit=Mathf.Min(limit,shape.MinRadius(y,shape.Top)-config.shaftRadius-config.wallClearance);
            Radius=Mathf.Max(0,limit)*config.orbitFraction*Mathf.Clamp01(input.magnitude/config.inputRadiusPixels);BaseHeight=y;
            // Lift the bowl a little (StirMotion's wave): keep Tip under the surface and every bowl vertex inside the cavity at the raised height.
            float raised=y;
            if(lift>0)
            {
                float goal=Mathf.Min(y+lift,liquidHeight-config.minimumSubmergence);
                for(int k=0;k<5&&goal>y+1e-5f;k++){if(Fits(shape,goal,radial,Radius,config.wallClearance)){raised=goal;break;}goal=y+(goal-y)*.5f;}
            }
            Lift=raised-y;y=raised;
            var point=frame.TransformPoint(new Vector3(shape.Axis.x+radial.x*Radius,y,shape.Axis.y+radial.y*Radius));
            tool.transform.position+=point-tip.position;BottomGap=y+bottom-shape.Bottom;
        }
        private bool Fits(CavityShape shape,float y,Vector2 radial,float radius,float wall)
        {
            foreach(var v in offsets)
            {
                float r=Mathf.Max(0,shape.RadiusAt(y+v.y)-wall),x=v.x+radial.x*radius,z=v.z+radial.y*radius;
                if(x*x+z*z>r*r||y+v.y>shape.Top)return false;
            }
            return true;
        }
    }
}
