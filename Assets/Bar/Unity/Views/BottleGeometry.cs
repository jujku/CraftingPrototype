using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>离线切片的旋转内腔，坐标为物品根的米制局部空间；容量从物品数据读取，禁止缩放凑量。</summary>
    [Serializable]public sealed class BottleGeometryData
    {
        public string id,method;public float wallMetres;public Vector2 axis;
        public float[] heights,radii;public double[] volumesMl,fractions,startAngles;public double capacityMl;
        public static BottleGeometryData Load(string id){var a=Resources.Load<TextAsset>("BottleGeometry/"+id);return a?JsonUtility.FromJson<BottleGeometryData>(a.text):null;}
        public Mesh CreateMesh(int sides)
        {
            int n=heights.Length;var vs=new Vector3[n*sides+2];var ts=new int[((n-1)*sides*2+sides*2)*3];int k=0;
            for(int i=0;i<n;i++)for(int j=0;j<sides;j++){float a=j*2*Mathf.PI/sides;vs[i*sides+j]=new Vector3(axis.x+radii[i]*Mathf.Cos(a),heights[i],axis.y+radii[i]*Mathf.Sin(a));}
            vs[n*sides]=new Vector3(axis.x,heights[0],axis.y);vs[n*sides+1]=new Vector3(axis.x,heights[n-1],axis.y);
            for(int i=0;i<n-1;i++)for(int j=0;j<sides;j++){int a=i*sides+j,b=i*sides+(j+1)%sides,c=a+sides,d=b+sides;ts[k++]=a;ts[k++]=c;ts[k++]=b;ts[k++]=b;ts[k++]=c;ts[k++]=d;}
            for(int j=0;j<sides;j++){int b=(j+1)%sides;ts[k++]=n*sides;ts[k++]=j;ts[k++]=b;ts[k++]=n*sides+1;ts[k++]=(n-1)*sides+b;ts[k++]=(n-1)*sides+j;}
            var mesh=new Mesh{name=id+" offline closed cavity"};mesh.vertices=vs;mesh.triangles=ts;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public VolumeProfile CreateProfile(){var p=ScriptableObject.CreateInstance<VolumeProfile>();p.Heights=heights;p.VolumesMl=volumesMl;return p;}
    }
    /// <summary>旋转内腔与水平面相交的几何积分；显示和离线起流表共用，不扣库存。</summary>
    public static class BottleGeometryMath
    {
        public static double VolumeBelow(BottleGeometryData d,double tiltRadians,double height)
        {
            double sin=Math.Sin(tiltRadians),cos=Math.Cos(tiltRadians),sum=0;
            if(Math.Abs(sin)<1e-8&&cos>0)
            {
                for(int i=1;i<d.heights.Length;i++)
                {
                    double low=d.heights[i-1],high=d.heights[i],cut=Math.Min(high,height/cos);if(cut<=low)break;
                    double a=d.radii[i-1],b=a+(d.radii[i]-a)*(cut-low)/(high-low);
                    sum+=Math.PI*(cut-low)*(a*a+a*b+b*b)/3*1e6;
                }
                return sum;
            }
            for(int i=1;i<d.heights.Length;i++)
            {
                double dy=d.heights[i]-d.heights[i-1];
                // Midpoint sub-slices keep the circular segment integral smooth at the contact line.
                for(int j=0;j<4;j++)
                {
                    double t=(j+.5)/4,y=d.heights[i-1]+dy*t,r=d.radii[i-1]+(d.radii[i]-d.radii[i-1])*t;
                    double z=sin<1e-8?(y*cos<=height?1:-1):(height-y*cos)/(r*sin);
                    double area=z<=-1?0:z>=1?Math.PI*r*r:r*r*(Math.Acos(-z)+z*Math.Sqrt(1-z*z));
                    sum+=area*dy/4*1e6;
                }
            }
            return sum;
        }
        public static float PlaneForMl(BottleGeometryData d,Transform root,double ml)
        {
            ml=Math.Min(ml,d.volumesMl[d.volumesMl.Length-1]);
            double cos=Vector3.Dot(root.up,Vector3.up),tilt=Math.Acos(Math.Max(-1,Math.Min(1,cos))),sin=Math.Sin(tilt);
            if(tilt<1e-5){var profile=d.CreateProfile();float h=profile.HeightForMl(Math.Min(ml,profile.CapacityMl));UnityEngine.Object.Destroy(profile);return root.TransformPoint(new Vector3(d.axis.x,h,d.axis.y)).y;}
            double lo=double.MaxValue,hi=double.MinValue;
            for(int i=0;i<d.heights.Length;i++){double center=d.heights[i]*cos,r=d.radii[i]*sin;lo=Math.Min(lo,center-r);hi=Math.Max(hi,center+r);}
            for(int j=0;j<26;j++){double mid=(lo+hi)*.5;if(VolumeBelow(d,tilt,mid)<ml)lo=mid;else hi=mid;}
            return root.TransformPoint(new Vector3(d.axis.x,0,d.axis.y)).y+(float)((lo+hi)*.5);
        }
    }
}
