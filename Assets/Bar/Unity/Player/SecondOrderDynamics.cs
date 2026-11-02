using UnityEngine;
namespace Bar.Unity
{
    /// <summary>
    /// 二阶动态跟随（频率 f、阻尼比 zeta、初始响应 r）：f 决定跟上的快慢，zeta &lt; 1 会过冲回弹，r &gt; 1 先冲过头、r &lt; 0 先反向。
    /// 半隐式积分，按步长自动取稳定的 k2，所以低帧率不会发散。只用于表现层，不进入物理核心。
    /// </summary>
    public sealed class SecondOrderVector
    {
        private float k1,k2,k3;private Vector3 previous,y,velocity;
        public Vector3 Value=>y;
        public SecondOrderVector(float frequency,float zeta,float response,Vector3 start)
        {float w=2*Mathf.PI*Mathf.Max(.01f,frequency);k1=zeta/(Mathf.PI*Mathf.Max(.01f,frequency));k2=1/(w*w);k3=response*zeta/w;Reset(start);}
        public void Reset(Vector3 value){previous=y=value;velocity=Vector3.zero;}
        public void Reconfigure(float frequency,float zeta,float response)
        {if(!float.IsFinite(frequency)||!float.IsFinite(zeta)||!float.IsFinite(response)||frequency<=0||zeta<=0)throw new System.ArgumentException("Invalid spring coefficients");float w=2*Mathf.PI*frequency;k1=zeta/(Mathf.PI*frequency);k2=1/(w*w);k3=response*zeta/w;}
        public Vector3 Update(float dt,Vector3 target)
        {
            if(dt<=0)return y;var targetVelocity=(target-previous)/dt;previous=target;
            float stable=Mathf.Max(k2,dt*dt/2+dt*k1/2,dt*k1);y+=dt*velocity;velocity+=dt*(target+k3*targetVelocity-y-k1*velocity)/stable;return y;
        }
    }
    /// <summary>标量版本，含义同 <see cref="SecondOrderVector"/>。</summary>
    public sealed class SecondOrderFloat
    {
        private readonly float k1,k2,k3;private float previous,y,velocity;
        public float Value=>y;
        public SecondOrderFloat(float frequency,float zeta,float response,float start)
        {float w=2*Mathf.PI*Mathf.Max(.01f,frequency);k1=zeta/(Mathf.PI*Mathf.Max(.01f,frequency));k2=1/(w*w);k3=response*zeta/w;Reset(start);}
        public void Reset(float value){previous=y=value;velocity=0;}
        public float Update(float dt,float target)
        {
            if(dt<=0)return y;float targetVelocity=(target-previous)/dt;previous=target;
            float stable=Mathf.Max(k2,dt*dt/2+dt*k1/2,dt*k1);y+=dt*velocity;velocity+=dt*(target+k3*targetVelocity-y-k1*velocity)/stable;return y;
        }
    }
}
