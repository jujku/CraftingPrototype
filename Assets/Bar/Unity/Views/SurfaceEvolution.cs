using UnityEngine;
namespace Bar.Unity
{
    /// <summary>显示层的时间积累：壶先起雾再结霜，霜消退为湿痕；冷凝依次经过雾/珠/流；不修改温度。
    /// 2026-10-28 改（见 Tools/WallFrost）：壶壁低于 0 °C 才结霜，霜按速率一层层积累（越冷越快、壶在空气里动得越快越快），高于 0 °C 才按速率化成水。
    /// 同日试玩后再改：壶里没有冰时不再长新的雾和霜（倒掉冰、擦干后保持干净），满霜时间 8 秒改为 5 秒。
    /// 第三次（用户给的设计思路）：长霜速度 = (静置速度 + 摇壶速度 × 核心摇壶强度) × 冷度，全力摇也要约 9 秒才满；
    /// 擦干后锁住，只有重新用力摇壶才解锁，锁住时只极慢地起一点雾。</summary>
    public sealed class SurfaceEvolution
    {
        private readonly SurfaceDisplaySettings p;private float coldAge;
        public float Frost {get;private set;}public float Wet {get;private set;}public float Mist {get;private set;}public float Drops {get;private set;}public float Runs {get;private set;}public float Wipe {get;private set;}
        public SurfaceEvolution(SurfaceDisplaySettings settings){p=settings;}
        private float wiping;
        /// <summary>擦干后为真：雾和霜不再长回，直到玩家重新用力摇壶（强度超过 metalWipeUnlockIntensity）。</summary>
        public bool WipedClean {get;private set;}
        /// <summary>V2 擦干（只对金属壶）：在 metalWipeSeconds 内把雾、霜、水降到 0，并锁住不让它们长回。</summary>
        public void WipeDry(){wiping=1;WipedClean=true;}
        /// <param name="shakeIntensity">核心正在收到的摇壶强度（0–1，来自玩家手势）；摇得越用力霜长得越快。</param>
        /// <param name="iceInside">壶里还有冰。没有冰又没在摇时壶壁只会回温，不再长新的雾和霜。</param>
        public void Step(float wallTemperature,bool glass,bool held,float dt,float shakeIntensity=0,bool iceInside=true)
        {
            if(!glass)
            {
                if(wiping>0)
                {
                    // A towel takes everything off; afterwards fog and frost come back only while there is still ice in the tin.
                    float k=dt/Mathf.Max(.01f,p.metalWipeSeconds);wiping=Mathf.MoveTowards(wiping,0,k);
                    Mist=Mathf.MoveTowards(Mist,0,k);Frost=Mathf.MoveTowards(Frost,0,k);Wet=Mathf.MoveTowards(Wet,0,k);Wipe=held?1:Wipe;return;
                }
                // Below the dew point the steel fogs quickly and dries slowly.
                // New fog and frost need the tin to be shaken or to still hold ice, and not to have been wiped since the last hard shake.
                if(WipedClean&&shakeIntensity>p.metalWipeUnlockIntensity)WipedClean=false;
                bool deposit=!WipedClean&&(shakeIntensity>p.metalShakeThreshold||iceInside);
                float fogTarget=Mathf.Clamp01((p.metalDewPoint-wallTemperature)/Mathf.Max(.01f,p.metalFogRange));
                float fogRate=fogTarget<=Mist?p.metalFogDry:deposit?p.metalFogGrowth:WipedClean&&iceInside?p.metalWipedFogGrowth:0;
                Mist=Mathf.MoveTowards(Mist,fogTarget,dt*fogRate);
                // Frost only below 0 °C and only by accumulation, never by chasing a target set by the wall temperature: the wall sets how fast
                // it grows, a hard shake makes it grow faster. Above 0 °C it melts back, again at a rate, and the melt becomes water on the steel.
                float before=Frost;
                // Without ice and without shaking the emptied wall is only warming back up; nothing new deposits, what is there stays until 0 °C.
                if(wallTemperature<p.metalFrostOnset)
                {
                    float cold=Mathf.Clamp01((p.metalFrostOnset-wallTemperature)/Mathf.Max(.01f,p.metalFrostOnset-p.metalFrostFull));
                    float rate=(p.metalFrostRestRate+p.metalFrostShakeRate*Mathf.Clamp01(shakeIntensity))*cold;
                    if(deposit)Frost=Mathf.Min(p.frostCap,Frost+rate*dt);
                }
                else Frost=Mathf.Max(0,Frost-dt/Mathf.Max(.01f,p.metalFrostMeltSeconds)*Mathf.Clamp01((wallTemperature-p.metalFrostOnset)/Mathf.Max(.01f,p.metalFrostMeltRange)));
                Wet=Mathf.Clamp01(Wet+Mathf.Max(0,before-Frost)-p.wetDry*dt*Wet);
                Wipe=held?1:Mathf.MoveTowards(Wipe,0,dt/p.frostWipeRecovery);return;
            }
            float target=Mathf.Clamp01((p.mistDewPoint-wallTemperature)/p.mistRange);coldAge=target>0?coldAge+dt:0;
            Mist=Mathf.MoveTowards(Mist,target,dt/p.mistSeconds);
            float drops=coldAge>=p.mistSeconds?Mathf.Clamp01((p.dropOnset-wallTemperature)/p.dropRange):0;Drops=Mathf.MoveTowards(Drops,drops,dt/p.dropSeconds);
            float runs=coldAge>=p.runSeconds?Drops:0;Runs=Mathf.MoveTowards(Runs,runs,dt/p.runSeconds);
            Wipe=held?1:Mathf.MoveTowards(Wipe,0,dt/p.wipeRecovery);
        }
    }
}
