using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>显示冰盒在水平切面下的体积。旋转盒的高度分布为最多三个均匀分布的卷积。</summary>
    public readonly struct IceVolumeSlice
    {
        private readonly double a,b,c,bottom;public readonly double VolumeMl;
        public IceVolumeSlice(Vector3 position,Quaternion rotation,Vector3 center,Vector3 size,double maximumMl)
        {
            double x=Math.Abs((rotation*Vector3.right).y*size.x),y=Math.Abs((rotation*Vector3.up).y*size.y),z=Math.Abs((rotation*Vector3.forward).y*size.z);
            bottom=(position+rotation*center).y-(x+y+z)/2;
            if(x<1e-6){x=y;y=z;z=0;}if(x<1e-6){x=y;y=z;z=0;}if(y<1e-6){y=z;z=0;}
            a=x;b=y;c=z;VolumeMl=Math.Min(maximumMl,(double)size.x*size.y*size.z*1e6);
        }
        private static double Positive(double x,int power)=>x<=0?0:power==3?x*x*x:power==2?x*x:x;
        public double FractionBelow(float height)
        {
            double h=height-bottom,total=a+b+c;if(h<=0)return 0;if(h>=total)return 1;
            int n=c>=1e-6?3:b>=1e-6?2:1;double value=0;
            for(int mask=0;mask<(1<<n);mask++)
            {
                double shift=0;int parity=0;if((mask&1)!=0){shift+=a;parity++;}if((mask&2)!=0){shift+=b;parity++;}if((mask&4)!=0){shift+=c;parity++;}
                value+=(parity%2==0?1:-1)*Positive(h-shift,n);
            }
            double denominator=n==3?6*a*b*c:n==2?2*a*b:a;return Math.Max(0,Math.Min(1,value/denominator));
        }
    }
    /// <summary>求解内腔体积减去浸没冰体积等于实际液量；不把露出水面的冰当成水。</summary>
    public static class IceDisplacement
    {
        public static double SubmergedMl(float height,IceVolumeSlice[] ice,int count)
        {double volume=0;for(int i=0;i<count;i++)volume+=ice[i].VolumeMl*ice[i].FractionBelow(height);return volume;}
        public static float Height(VolumeProfile profile,double liquidMl,IceVolumeSlice[] ice,int count)
        {
            float low=profile.HeightForMl(liquidMl);if(liquidMl<=0||count==0)return low;
            double total=0;for(int i=0;i<count;i++)total+=ice[i].VolumeMl;
            float high=profile.HeightForMl(liquidMl+total);
            for(int i=0;i<24;i++){float h=(low+high)*.5f;if(profile.VolumeBelowHeight(h)-SubmergedMl(h,ice,count)<liquidMl)low=h;else high=h;}
            return (low+high)*.5f;
        }
    }
}
