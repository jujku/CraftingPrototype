using System;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>
    /// 手持物取景（右手为准，左手镜像）：物品包围盒中心放在屏幕坐标 center（-1..1），物品高度占屏 screenHeight，
    /// 距离夹在 minDistance..maxDistance，顶端不高于 topLimit，至少露出 minVisible 的高度，内侧边不越过 innerLimit。
    /// euler 是握点坐标系相对相机的朝向（度，右手；左手 yaw、roll 取反）。按 match 前缀取第一条。人为设定、未标定。
    /// </summary>
    [Serializable]public sealed class HoldProfile
    {
        public string match="";public Vector2 center=new Vector2(.45f,-.45f);public float screenHeight=.3f,minDistance=.3f,maxDistance=.5f,topLimit=.05f,minVisible=.8f,innerLimit=.1f;public Vector3 euler;
        /// <summary>朝向特征：名字含其中任一词（逗号分隔）的子网格，例如标签、刻度。求解时绕物品竖轴转，让它正对玩家，再向画面中央多转 faceYaw 度；faceUpper 只取位置偏上的那一半（双头量酒器朝上的杯）。</summary>
        public string face="";public float faceYaw;public bool faceUpper;
    }
    [Serializable]public sealed class HoldSwaySettings
    {
        /// <summary>转头时手的滞后：角速度（度/秒）× lagSeconds，限幅 maxDegrees；roll 为偏航滞后的倾斜比例。</summary>
        public float lagSeconds=.022f,maxDegrees=4,rollFactor=.35f,frequency=2.6f,zeta=.55f,response=0;
    }
    [Serializable]public sealed class HoldBobSettings
    {
        /// <summary>走路起伏：步频 stepHz，左右 sideMetres、上下 dropMetres 的 8 字；按水平速度 / 移动速度缩放。</summary>
        public float stepHz=1.7f,sideMetres=.004f,dropMetres=.006f,frequency=2,zeta=1;
    }
    [Serializable]public sealed class HoldTransitionSettings
    {
        /// <summary>拿起或换手时从原位置到持握位的过渡：略欠阻尼，接住时有一点回弹。</summary>
        public float frequency=3.2f,zeta=.72f,response=0;
        /// <summary>过渡中段向上的弧线高度（米），东西从台面拿起来而不是滑过来。</summary>
        public float arcMetres=.025f;
    }
    [Serializable]public sealed class HoldObserveSettings
    {
        /// <summary>
        /// 观察镜头：往手里的容器里倒、视角锁定时，镜头低头，让视线以 sightDegrees（相对水平向下）看进接收物，便于看杯内刻度和液面。
        /// 低头量 = sightDegrees − 玩家俯仰 − 接收物在相机下方的角度，夹在 0..maxDegrees；临界阻尼，不回弹。动作结束时低头并入玩家俯仰，不自动抬回。
        /// </summary>
        public bool enabled=true;public float sightDegrees=68,maxDegrees=34,frequency=1.4f,zeta=1;
    }
    [Serializable]public sealed class HoldAccentSettings
    {
        /// <summary>V2 展示动作（人为设定、未标定）。往下一顿：目标压低 tapSeconds 秒，弹簧 tapFrequency/tapZeta 回弹。</summary>
        public float capTapMetres=.025f,strainerTapMetres=.012f,bottleCapTapMetres=.01f,wipeTapMetres=.008f,shakeStopMetres=.03f,tapSeconds=.05f,tapFrequency=14,tapZeta=.4f;
        /// <summary>摇完收势：端近 admireSeconds 秒，取景中心挪 admireShift、放大 admireScale。</summary>
        public float admireSeconds=1.6f,admireScale=1.18f,admireFrequency=2.2f,admireZeta=.85f;public Vector2 admireShift=new Vector2(-.16f,.1f);
        /// <summary>擦干：壶绕自身轴转 wipeDegrees 度，用时 wipeSeconds。</summary>
        public float wipeDegrees=360,wipeSeconds=.6f;
    }
    [Serializable]public sealed class HoldInspectSettings
    {
        /// <summary>V3 空量酒器检视（人为设定、未标定）。三段结束时刻（秒）：端详两端、指间滚转、上抛接住。</summary>
        public float lookEnd=1.45f,twirlEnd=2.35f,tossEnd=3.2f,liftSeconds=.3f,returnSeconds=.3f;
        /// <summary>检视时物品中心移到的相机局部位置（右手；左手 x 取反）。</summary>
        public Vector3 center=new Vector3(.03f,-.02f,.18f);
        /// <summary>第一段：大杯口与视线成 viewDegrees、刻度在对面内壁；flipStart 秒起用 flipSeconds 翻 flipDegrees 看小杯口。</summary>
        public float viewDegrees=35,flipStart=.7f,flipSeconds=.35f,flipDegrees=180;
        /// <summary>第二段：绕镜头前向转 twirlDegrees（快-慢-快-停），转轴向外偏 twirlPivotMetres。</summary>
        public float twirlDegrees=720,twirlPivotMetres=.018f;
        /// <summary>第三段：上抛 tossMetres，空中翻 tossFlipDegrees（与第一段合起来回到原朝向），用时 tossAirSeconds，接住时往下一顿 catchMetres。</summary>
        public float tossMetres=.05f,tossFlipDegrees=540,tossAirSeconds=.5f,catchMetres=.022f;
        /// <summary>量酒器检视的声音（指间转动、抛起、接住），格式同 HoldInspectionSound。</summary>
        public HoldInspectionSound[] sounds=new HoldInspectionSound[0];
    }
    /// <summary>
    /// 2026-10-28 摇壶动作（ShakePresentation，人为设定、未标定；参考用户给的 bartending_action_and_camera_rig_prompt.md 的动作设计）。
    /// 位置是相机局部坐标（米，右手为准，左手 x 取反）。起壶：windup 秒内后仰 windupTiltDegrees、手先往下沉再往上送（下沉约 4/27 × windup × 出手速度，出手点再低 windupDropMetres），出手时的速度与抛物线起点一致；toss 秒内沿过 apex 的抛物线
    /// 飞到 catchPoint，绕斜 spinTiltDegrees 的横轴转 spinDegrees（出手时转速从零加起，接住时剩平均转速的 spinEndRate 倍）；接住时保留来时的速度和转速，
    /// 由阻尼振子（catchFrequency、catchZeta）顺势缓冲回 catchPoint（原来接住瞬间速度清零再被 14 Hz 弹簧踢一下，60 fps 下来回乱颤）。
    /// 上一次摇壶结束 tossCooldownSeconds 内再摇不再起壶。摇壶：沿 stroke 方向冲程 strokeMetres（含 strokeHarmonic 二倍频），垂直方向弧线
    /// arcMetres/arcHarmonicMetres；壶盖朝 capDirection；手腕甩动 wristDegrees（滞后 wristLag 弧度）、前臂扭转 twistDegrees、摆动 swayDegrees。
    /// 频率取鼠标折返间隔（minHz–maxHz，默认 defaultHz）；每次折返把相位拉向端点的 lockGain，在 lockSeconds 内补完；鼠标先按 smoothingSeconds 低通去掉手抖，回头超过 reversalPixels、且距上次折返至少 minHalfSeconds 才算折返；
    /// 停手 idleSeconds 后幅度降到零；幅度 = strokeMetres × Lerp(minAmplitude, 1, 核心摇壶强度)。冰块在折返后 slamLag 弧度撞壁，壶沿冲程方向
    /// 反冲 slamMetres、slamHz 振动 slamSeconds。不起壶时 blendSeconds 内从持握位过渡；松手 returnSeconds 回到持握位。
    /// </summary>
    [Serializable]public sealed class HoldShakeSettings
    {
        public bool toss=true;public float tossCooldownSeconds=3,blendSeconds=.2f,returnSeconds=.3f;
        public float windupSeconds=.14f,windupDropMetres=0,windupTiltDegrees=18,tossSeconds=.48f,spinDegrees=720,spinEndRate=.3f,spinTiltDegrees=12;
        public Vector3 apex=new Vector3(.06f,.09f,.4f),catchPoint=new Vector3(.11f,-.03f,.36f);
        public float catchFrequency=4,catchZeta=.7f,chamberSeconds=.18f;
        public Vector3 stroke=new Vector3(-.55f,.48f,.68f),capDirection=new Vector3(-.6f,.6f,.5f);
        public float strokeMetres=.075f,strokeHarmonic=.2f,arcMetres=.028f,arcHarmonicMetres=.014f;
        public float wristDegrees=16,wristLag=.35f,wristHarmonicDegrees=5,twistDegrees=15,twistLag=.25f,swayDegrees=10;
        public float minHz=1.2f,maxHz=4.5f,defaultHz=3.2f,lockGain=.7f,lockSeconds=.12f,reversalPixels=40,smoothingSeconds=.03f,minHalfSeconds=.08f,idleSeconds=.4f,minAmplitude=.35f,amplitudeFrequency=3,amplitudeZeta=1;
        public float slamLag=.3f,slamMetres=.012f,slamHz=28,slamSeconds=.06f;
    }
    /// <summary>V4单类物品检视时间轴，米/秒/度。人为设定、未标定；整圈转动在回位前完成。</summary>
    [Serializable]public sealed class HoldInspectionProfile
    {
        public string match="";public float lookSeconds=1.2f,playSeconds=1.3f,returnSeconds=.35f,liftSeconds=.35f;
        public Vector3 center=new Vector3(.035f,-.025f,.3f),euler=new Vector3(-60,-20,20);
        public float openBodyViewDegrees,openBodyLookSeconds,openBodyLiftSeconds;
        public float tiltDegrees=20,lookTurnDegrees=45,turnDegrees=360,windupSeconds=.12f,airSeconds=.65f,tossMetres=.05f,windupMetres=.012f,catchMetres=.008f;
        public float circleMetres=.004f,circleCount=2,raiseMetres=.025f,reboundDegrees=.8f,reboundHz=7;
        public float settleSeconds,diagonalDegrees=35,depthDegrees=25,drawMetres=.018f,glintDegrees=8;
        public float lookRollDegrees,windupDegrees=8,windupBackMetres=.02f,spinPlaneDegrees=35,spinBias=1.5f;
        public float pivotFraction=.4f,pivotRecenterSeconds=.13f,catchDipSeconds=.22f,catchFrequency=6,catchZeta=.75f,catchTiltDegrees=2;
        public float ringDegrees=4.5f,ringHz=14,ringDecay=15.35f,ringSeconds=.3f;
        public float lightPinchDegrees=7,deepPinchDegrees=13,pinchStart=.16f,firstPinchSeconds=.17f,pinchPauseSeconds=.12f,secondPinchSeconds=.22f;
        public float firstRecoilDegrees=1.5f,secondRecoilDegrees=3,recoilSeconds=.14f;
        public float sealedTossMetres=.035f,sealedAirSeconds=.46f,sealedCatchMetres=.025f,axialRollDegrees=180;
        public float spinSeconds=.9f,reverseDegrees=20,neckDegrees=8,swirlMinimumTilt=6,lightRollDegrees=45,breathDegrees=3;
        public float firstClapSeconds=.18f,halfOpenSeconds=.2f,secondClapSeconds=.12f,wristDegrees=25;
        public float peekSeconds=1.8f,peekDegrees=28,peekOpenStart=.12f,peekOpenSeconds=.35f,peekCloseStart=1.05f,peekCloseSeconds=.35f;
        public HoldInspectionSound[] sounds=new HoldInspectionSound[0];
        public float Duration=>lookSeconds+playSeconds+settleSeconds+returnSeconds;
    }
    /// <summary>
    /// 按可见动作时刻触发 CC0 采样；只写 presentation_sound。
    /// when 可用 "+" 连接多个条件（全部满足才放）：any、empty、loaded、sealed、open、ice、dry（无冰）、liquid、peek、toolEmpty、swirl、lightRoll，
    /// 以及旧写法 sealedIce、sealedLiquid、openIce、openLiquid。scale 按内容缩放音量：ice（冰块数，4 块为满）、liquid（瓶子按液面上方的空气，满瓶仍留 0.3；杯/壶按装了多少）。
    /// </summary>
    [Serializable]public sealed class HoldInspectionSound
    {public float time,gain=.04f,pitch=1,degrees;public string bank="metal",when="any",scale="",phase="";public int variant;}
    /// <summary>独立的检视快回位弹簧；不改原3.2Hz拿起/换手手感。</summary>
    [Serializable]public sealed class HoldInspectionSettings
    {
        public float interruptFrequency=12,interruptZeta=1;public HoldInspectionProfile[] profiles=new HoldInspectionProfile[0];
        public HoldInspectionProfile For(string id){foreach(var p in profiles)if(id.StartsWith(p.match,StringComparison.Ordinal))return p;return new HoldInspectionProfile();}
    }
    [Serializable]public sealed class HoldViewSettings
    {
        public string note;public Vector3 emptyHand=new Vector3(.2f,-.27f,.55f);
        public HoldInspectionSettings inspectAll=new HoldInspectionSettings();
        public HoldProfile[] profiles=new HoldProfile[0];public HoldSwaySettings sway=new HoldSwaySettings();public HoldBobSettings bob=new HoldBobSettings();public HoldTransitionSettings transition=new HoldTransitionSettings();public HoldObserveSettings observe=new HoldObserveSettings();public HoldAccentSettings accent=new HoldAccentSettings();public HoldInspectSettings inspect=new HoldInspectSettings();public HoldShakeSettings shake=new HoldShakeSettings();
        public HoldProfile For(string definitionId)
        {
            foreach(var profile in profiles)if(definitionId!=null&&definitionId.StartsWith(profile.match,StringComparison.Ordinal))return profile;
            return profiles.Length>0?profiles[profiles.Length-1]:new HoldProfile();
        }
        public static TextAsset Source=>Resources.Load<TextAsset>("BarHoldView");
        private static HoldViewSettings cached;
        public static HoldViewSettings Load()=>cached??=JsonUtility.FromJson<HoldViewSettings>(Source.text);
    }
}
