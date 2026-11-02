using Bar.Domain;
using Bar.Data;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>
    /// 摇壶的画面动作（只是表现，不修改领域里的摇壶强度；强度仍由鼠标往复在 GestureAnalyzer 里算，核心照旧收到）。
    /// 2026-10-28 用户要求重做（参考 bartending_action_and_camera_rig_prompt.md 的动作设计）：
    /// 1. 起壶：下压蓄力 → 抛物线上抛、空中斜向转两圈 → 接住，落到摇壶起点。出手和接住都保持速度连续，
    ///    接住时手顺着壶来时的速度和转向让一下再收回（阻尼振子，catchFrequency/catchZeta），不再一帧内停住、也不再被高频弹簧来回弹；
    /// 2. 摇壶：画面按一条设计好的轨迹走（斜向椭圆主冲程 + 相位滞后的手腕甩动 + 前臂扭转 + 两端冰块撞壁的反冲），
    ///    鼠标不直接决定壶的位置，只决定节奏：每次鼠标折返把轨迹相位轻轻拉向对应的端点（锁相），折返间隔决定频率，
    ///    核心正在收到的强度决定冲程幅度，停手 idleSeconds 后幅度平滑降到零；
    /// 3. 松手：从当前姿态平滑回到持握位（HoldViewRig 随后做收势赏霜）。
    /// 全部在相机局部空间里算，右手为准，左手 x 取反。参数在 BarHoldView.json 的 shake 段。相机本身不动、不改 FOV。
    /// </summary>
    public sealed class ShakePresentation
    {
        private enum Stage{Idle,Windup,Toss,Chamber,Loop,Return}
        private readonly HoldShakeSettings p=HoldViewSettings.Load().shake;
        private ItemView current;private Stage stage=Stage.Idle;private float clock,sign=1,lastEnded=-100,now;
        private Vector3 axisLocal,faceLocal,pivotLocal,windupPosition,lastPosition;private Quaternion windupRotation,lastRotation=Quaternion.identity;
        private double gesture,extreme;private int direction;private float lastReversal=-100,correction,theta=1.5f*Mathf.PI;
        private SecondOrderFloat amplitude;private Vector3 catchVelocity,spinAxis=Vector3.right;private float catchSpin,catchAge=100;
        private int slamIndex;private float slamAge=1;private bool slamAtCap;
        /// <summary>轨迹相位（弧度，单调增加）。</summary>
        public float Phase=>theta;
        /// <summary>当前摇壶频率（Hz），由鼠标折返间隔得到，限制在 minHz–maxHz。</summary>
        public float Frequency{get;private set;}
        /// <summary>当前冲程幅度（米）。</summary>
        public float Amplitude=>amplitude!=null?amplitude.Value:0;
        /// <summary>鼠标往复的折返次数（超过 reversalPixels 的回头才算）。</summary>
        public int ReversalSequence{get;private set;}
        /// <summary>冰块撞到壶盖或壶底的次数；声音按它排期。</summary>
        public int SlamSequence{get;private set;}
        /// <summary>最近一次撞击是在远端（冰撞壶盖）还是近端（冰撞壶底）。</summary>
        public bool SlamAtCap=>slamAtCap;
        /// <summary>最近一次撞击的力度（0–1）：幅度和频率都满时为 1。</summary>
        public float SlamStrength{get;private set;}
        /// <summary>起壶接住的次数；接住时有一下金属闷响和冰块砸底。</summary>
        public int CatchSequence{get;private set;}
        /// <summary>兼容旧的声音接口：冲程速度（米/秒）。</summary>
        public float GestureSpeed=>2*Mathf.PI*Frequency*Amplitude;
        /// <summary>当前阶段名，供验收截图记录。</summary>
        public string StageName=>stage.ToString();
        public void Apply(BarSession session,ItemView source,bool shaking,double input,float dt)
        {
            now+=dt;
            if(shaking&&source&&(source!=current||stage==Stage.Idle||stage==Stage.Return))Begin(session,source);
            if(!current)return;
            var side=session.Hands.SideOf(current.Item);if(!side.HasValue){current=null;stage=Stage.Idle;return;}
            sign=side==HandSide.Left?-1:1;
            var camera=session.Player.ViewCamera.transform;
            // The held pose this frame (BarSession has just put the item back on its grip), as the pivot point and rotation in camera space.
            var heldPosition=camera.InverseTransformPoint(current.transform.TransformPoint(pivotLocal));var heldRotation=Quaternion.Inverse(camera.rotation)*current.transform.rotation;
            if(!shaking&&stage!=Stage.Return){stage=Stage.Return;clock=0;lastEnded=now;}
            if(shaking)Follow(input,dt);
            clock+=dt;Vector3 position;Quaternion rotation;
            if(stage==Stage.Return)
            {
                float w=Mathf.SmoothStep(0,1,clock/Mathf.Max(.01f,p.returnSeconds));if(w>=1){current=null;stage=Stage.Idle;return;}
                position=Vector3.Lerp(lastPosition,heldPosition,w);rotation=Quaternion.Slerp(lastRotation,heldRotation,w);
            }
            else Pose(session,heldPosition,heldRotation,dt,out position,out rotation);
            lastPosition=position;lastRotation=rotation;
            current.transform.rotation=camera.rotation*rotation;current.transform.position=camera.TransformPoint(position)-current.transform.rotation*pivotLocal;
        }
        private void Begin(BarSession session,ItemView source)
        {
            bool fresh=source!=current||stage==Stage.Idle;current=source;
            // The tin's own frame: long axis toward the cap, a face to show the camera, and the middle of the tin as the point that follows the path.
            axisLocal=Quaternion.Inverse(source.transform.rotation)*PourPose.Axis(source);
            faceLocal=Vector3.ProjectOnPlane(Vector3.forward,axisLocal);if(faceLocal.sqrMagnitude<1e-4f)faceLocal=Vector3.ProjectOnPlane(Vector3.right,axisLocal);faceLocal.Normalize();
            Bounds bounds=default;bool any=false;foreach(var r in source.GetComponentsInChildren<MeshRenderer>())if(r.enabled){if(!any){bounds=r.bounds;any=true;}else bounds.Encapsulate(r.bounds);}
            pivotLocal=any?source.transform.InverseTransformPoint(bounds.center):Vector3.zero;
            amplitude??=new SecondOrderFloat(p.amplitudeFrequency,p.amplitudeZeta,0,0);
            gesture=extreme=0;direction=0;lastReversal=-100;correction=0;Frequency=p.defaultHz;slamAge=1;
            // A toss only from a still tin; restarting right after a shake (or catching it mid-return) goes straight back into the stroke.
            bool toss=p.toss&&fresh&&now-lastEnded>p.tossCooldownSeconds;
            if(stage==Stage.Return||!fresh){stage=Stage.Loop;clock=p.blendSeconds*.5f;}
            else{stage=toss?Stage.Windup:Stage.Loop;clock=0;amplitude.Reset(0);catchAge=100;theta=1.5f*Mathf.PI;slamIndex=SlamIndex(theta);}
        }
        /// <summary>鼠标只给节奏：折返间隔 → 频率，折返方向 → 把相位拉向对应端点。</summary>
        private void Follow(double y,float dt)
        {
            // Hand tremor is filtered out first (a short low-pass), so only real turns of the stroke count.
            gesture=Mathf.Approximately(p.smoothingSeconds,0)?y:gesture+(y-gesture)*(1-System.Math.Exp(-dt/p.smoothingSeconds));y=gesture;
            if(direction==0){if(System.Math.Abs(y-extreme)>=p.reversalPixels){direction=y>extreme?1:-1;extreme=y;}}
            else if(direction>0){if(y>extreme)extreme=y;else if(extreme-y>=p.reversalPixels)Reverse(true,-1,y);}
            else{if(y<extreme)extreme=y;else if(y-extreme>=p.reversalPixels)Reverse(false,1,y);}
        }
        private void Reverse(bool top,int next,double y)
        {
            direction=next;extreme=y;float half=now-lastReversal;if(half<p.minHalfSeconds)return;ReversalSequence++;lastReversal=now;
            if(half<.8f)Frequency=Mathf.Lerp(Frequency,Mathf.Clamp(.5f/half,p.minHz,p.maxHz),.5f);
            // Mouse pushed up and turned: the tin should be at the far end (phase π/2); turned at the bottom: near end (3π/2).
            if(stage==Stage.Loop){float target=top?.5f*Mathf.PI:1.5f*Mathf.PI;correction+=p.lockGain*Mathf.DeltaAngle(theta*Mathf.Rad2Deg,target*Mathf.Rad2Deg)*Mathf.Deg2Rad;}
        }
        private Vector3 Control(Vector3 from,Vector3 to)=>2*M(p.apex)-.5f*(from+to);
        /// <summary>阻尼振子的单位初速度响应：x(0)=0、x'(0)=1，顺势让开后回到零（zeta 接近 1 时几乎不回弹）。</summary>
        private float Give(float t)
        {
            if(t<=0||t>3)return 0;float w=2*Mathf.PI*Mathf.Max(.1f,p.catchFrequency),z=Mathf.Clamp(p.catchZeta,0,.99f),wd=w*Mathf.Sqrt(1-z*z);
            return Mathf.Exp(-z*w*t)*Mathf.Sin(wd*t)/wd;
        }
        private int SlamIndex(float phase)=>Mathf.FloorToInt((phase-.5f*Mathf.PI-p.slamLag)/Mathf.PI);
        private Vector3 M(Vector3 v)=>new Vector3(v.x*sign,v.y,v.z);
        private void Pose(BarSession session,Vector3 heldPosition,Quaternion heldRotation,float dt,out Vector3 position,out Quaternion rotation)
        {
            var stroke=M(p.stroke).normalized;var cap=M(p.capDirection).normalized;var face=Vector3.ProjectOnPlane(Vector3.back,cap).normalized;
            var baseRotation=Quaternion.LookRotation(face,cap)*Quaternion.Inverse(Quaternion.LookRotation(faceLocal,axisLocal));
            var catchPoint=M(p.catchPoint);
            // After the catch the hand gives with the tin (its incoming velocity and spin) and brings it back.
            catchAge+=dt;float give=Give(catchAge);var spring=catchVelocity*give;var absorb=Quaternion.AngleAxis(-catchSpin*give,spinAxis);
            if(stage==Stage.Windup)
            {
                // Wrist drops and the top tips back before the throw; the dip turns into the throw, leaving the release point at the toss's starting velocity.
                float k=Mathf.Clamp01(clock/Mathf.Max(.01f,p.windupSeconds)),u=Mathf.SmoothStep(0,1,k);var release=heldPosition+Vector3.down*p.windupDropMetres;
                var throwVelocity=2*(Control(release,catchPoint)-release)/Mathf.Max(.01f,p.tossSeconds);
                position=heldPosition+Vector3.down*p.windupDropMetres*(k*k*(3-2*k))+throwVelocity*(p.windupSeconds*k*k*(k-1));rotation=Quaternion.AngleAxis(-p.windupTiltDegrees*u,Vector3.right)*heldRotation;
                windupPosition=position;windupRotation=rotation;if(clock<p.windupSeconds)return;
                // Carry the time past the release into the toss, so the frame at the switch does not stall and the next one jump.
                stage=Stage.Toss;clock-=p.windupSeconds;
            }
            if(stage==Stage.Toss)
            {
                // A parabola through the apex, two turns about a slightly tilted axis, fast off the hand and a little slower into the catch.
                float u=Mathf.Clamp01(clock/Mathf.Max(.01f,p.tossSeconds)),toss=clock;var control=Control(windupPosition,catchPoint);
                position=(1-u)*(1-u)*windupPosition+2*u*(1-u)*control+u*u*catchPoint;
                // The spin picks up from zero off the hand (no jolt at the release) and is down to spinEndRate of its average speed at the catch.
                float spin=p.spinDegrees*(u*u*(3-2*u)+p.spinEndRate*u*u*(u-1));var axis=Quaternion.AngleAxis(p.spinTiltDegrees*sign,Vector3.forward)*Vector3.right;
                rotation=Quaternion.AngleAxis(-spin,axis)*Quaternion.Slerp(windupRotation,baseRotation,Mathf.SmoothStep(0,1,u));
                if(toss>=p.tossSeconds)
                {
                    stage=Stage.Chamber;clock-=p.tossSeconds;CatchSequence++;
                    // The tin arrives moving and still turning (the end of the parabola and of the spin curve); the catch keeps both and absorbs them.
                    catchVelocity=2*(catchPoint-control)/Mathf.Max(.01f,p.tossSeconds);spinAxis=axis;catchSpin=p.spinDegrees*p.spinEndRate/Mathf.Max(.01f,p.tossSeconds);
                    // Likewise the time past the catch is already spent in the hand.
                    catchAge=clock;give=Give(catchAge);spring=catchVelocity*give;absorb=Quaternion.AngleAxis(-catchSpin*give,spinAxis);
                }
                else return;
            }
            if(stage==Stage.Chamber&&clock>=p.chamberSeconds){stage=Stage.Loop;clock=p.blendSeconds;}
            // The loop (and the chamber, which is the loop held at its near end with no stroke yet).
            float coreIntensity=current.Item is CobblerShaker shaker?(float)session.Adapter.Agitation(shaker.Body.CoreVesselId,.15):0;
            bool stroking=stage==Stage.Loop&&now-lastReversal<p.idleSeconds&&coreIntensity>0;
            float amp=Mathf.Max(0,amplitude.Update(dt,stroking?p.strokeMetres*Mathf.Lerp(p.minAmplitude,1,coreIntensity):0));
            if(stage==Stage.Loop)
            {
                float step=2*Mathf.PI*Frequency*dt,pull=correction*Mathf.Clamp01(dt/Mathf.Max(.01f,p.lockSeconds));correction-=pull;
                theta+=Mathf.Max(.25f*step,step+pull);
            }
            int index=SlamIndex(theta);
            if(index>slamIndex&&stage==Stage.Loop&&amp>.1f*p.strokeMetres)
            {
                // The ice arrives a moment after the turn: it hits the cap at the far end and the bottom at the near end; the tin jolts along the stroke.
                slamAtCap=(index&1)==0;SlamStrength=Mathf.Clamp01(amp/p.strokeMetres)*Mathf.Clamp01(Frequency/p.defaultHz);SlamSequence++;slamAge=0;
            }
            slamIndex=index;slamAge+=dt;
            float a=amp/Mathf.Max(.001f,p.strokeMetres);
            var perp=Vector3.ProjectOnPlane(new Vector3(0,1,-.4f),stroke).normalized;
            float along=amp*(Mathf.Sin(theta)+p.strokeHarmonic*Mathf.Sin(2*theta)),across=a*(p.arcMetres*Mathf.Cos(theta)+p.arcHarmonicMetres*Mathf.Sin(2*theta));
            var jolt=slamAge<p.slamSeconds?(slamAtCap?stroke:-stroke)*p.slamMetres*SlamStrength*Mathf.Exp(-slamAge/(p.slamSeconds/3))*Mathf.Cos(2*Mathf.PI*p.slamHz*slamAge):Vector3.zero;
            // The near end of the stroke stays at the catch point: the centre moves out with the amplitude, so the loop starts where the catch ended.
            var loopPosition=catchPoint+stroke*(amp+along)+perp*across+jolt+spring;
            // Wrist whip lags the stroke, the forearm turns the tin about its own axis, and the whole tin sways a little.
            var wristAxis=Vector3.Cross(Vector3.up,cap).normalized;if(wristAxis.sqrMagnitude<1e-4f)wristAxis=Vector3.right;
            float wrist=a*(p.wristDegrees*Mathf.Sin(theta-p.wristLag)-p.wristHarmonicDegrees*Mathf.Cos(2*theta)),twist=a*p.twistDegrees*-sign*Mathf.Sin(theta-p.twistLag),sway=a*p.swayDegrees*-sign*Mathf.Sin(theta);
            var loopRotation=absorb*Quaternion.AngleAxis(sway,Vector3.up)*Quaternion.AngleAxis(wrist,wristAxis)*Quaternion.AngleAxis(twist,cap)*baseRotation;
            if(stage==Stage.Chamber){position=catchPoint+spring;rotation=absorb*baseRotation;return;}
            // Starting without a toss: ease from the hand into the stroke.
            float blend=Mathf.SmoothStep(0,1,clock/Mathf.Max(.01f,p.blendSeconds));
            position=Vector3.Lerp(heldPosition,loopPosition,blend);rotation=Quaternion.Slerp(heldRotation,loopRotation,blend);
        }
    }
}
