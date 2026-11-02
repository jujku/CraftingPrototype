using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>
    /// 吧勺搅拌的画面动作（只是表现；核心的搅拌强度仍由 GestureAnalyzer 从鼠标圈速算出）。
    /// 参考 bartending_action_and_camera_rig_prompt.md 的搅拌设计：
    /// 1. 鼠标只给转速和方向：CircleTracker（与领域测量同一规则）给出鼠标绕滑动圆心的转角，平滑求导得到目标角速度，
    ///    勺子的角速度带惯性跟随（inertiaSeconds），停手后滑行一小段再停；相位由角速度积分，勺子总是绕杯轴贴壁转。
    /// 2. 入杯：先移到杯口正上方，再竖直落进杯里；松手：先竖直提出杯口，再回到持握位。全程不穿杯壁。
    /// 3. 轨迹：勺背贴着内壁绕圈（StirPresentation 保证整块勺头不穿壁不穿底），勺杆上端向杯轴内倾（倒锥），
    ///    转得快时勺头随相位上下起伏 liftMetres。勺背始终朝杯壁，所以勺子随绕圈绕自身轴转，扭纹的高光沿杆流动。
    /// 4. 每转 clinkTurns 圈记一次轻碰（声音按 ClinkSequence 排期）；冰块漩涡方向跟随 Spin。
    /// 相机不动。参数在 BarStirSettings.json。
    /// </summary>
    public sealed class StirMotion
    {
        private enum Stage{Idle,Enter,Loop,Return}
        private StirPresentation.Settings p=>StirPresentation.Settings.Load();
        private Stage stage=Stage.Idle;private ItemView current,receiver;private Container target;private ContainerVisual binding;
        private CircleTracker tracker;private double mouseAngle,laggedAngle;private float omega,theta,clinkTheta,clock;
        private Vector3 startPosition,lastPosition,returnLift;private Quaternion startRotation,lastRotation;private bool fromHold;
        /// <summary>勺子绕杯轴的相位（弧度；从上往下看逆时针为正）。</summary>
        public float Phase=>theta;
        /// <summary>勺子的角速度（弧度/秒；从上往下看逆时针为正）。</summary>
        public float Spin=>omega;
        /// <summary>转速相对 fullSpeedTurnsPerSecond 的比例（0–1），声音和起伏用。</summary>
        public float Speed01=>Mathf.Clamp01(Mathf.Abs(omega)/(2*Mathf.PI*p.fullSpeedTurnsPerSecond));
        /// <summary>勺子轻碰冰块/杯壁的次数（每 clinkTurns 圈一次）。</summary>
        public int ClinkSequence{get;private set;}
        /// <summary>当前搅拌的容器（入杯、搅拌、出杯阶段）。</summary>
        public Container Target=>stage==Stage.Idle?null:target;
        /// <summary>当前阶段名，供验收记录。</summary>
        public string StageName=>stage.ToString();
        public void Apply(BarSession session,ItemView spoon,ItemView into,Container vessel,bool stirring,double gestureX,double gestureY,float dt)
        {
            if(stirring&&spoon&&into&&vessel!=null&&(spoon!=current||into!=receiver||stage==Stage.Idle||stage==Stage.Return))Begin(session,spoon,into,vessel);
            if(!current)return;
            if(!session.Hands.SideOf(current.Item).HasValue||!binding){current=null;stage=Stage.Idle;omega=0;return;}
            // The held pose this frame: BarSession has just put the spoon back on its grip.
            var heldPosition=current.transform.position;var heldRotation=current.transform.rotation;
            if(!stirring&&stage!=Stage.Return){stage=Stage.Return;clock=0;returnLift=RiseOut();}
            if(stirring)Follow(gestureX,gestureY,dt);else omega*=Mathf.Exp(-dt/Mathf.Max(.01f,p.inertiaSeconds));
            theta+=omega*dt;clock+=dt;
            if(stage==Stage.Loop&&Speed01>=p.clinkMinimumSpeed&&Mathf.Abs(theta-clinkTheta)>=2*Mathf.PI*p.clinkTurns){ClinkSequence++;clinkTheta=theta;}
            else if(Speed01<p.clinkMinimumSpeed)clinkTheta=theta;
            Vector3 position;Quaternion rotation;
            if(stage==Stage.Return)
            {
                // Straight up out of the glass first, then back to the hand.
                float k=clock/Mathf.Max(.01f,p.returnSeconds);if(k>=1){current=null;stage=Stage.Idle;return;}
                if(k<.45f){position=lastPosition+returnLift*Mathf.SmoothStep(0,1,k/.45f);rotation=lastRotation;}
                else{float u=Mathf.SmoothStep(0,1,(k-.45f)/.55f);position=Vector3.Lerp(lastPosition+returnLift,heldPosition,u);rotation=Quaternion.Slerp(lastRotation,heldRotation,u);}
                current.transform.SetPositionAndRotation(position,rotation);return;
            }
            Solve(session);var stirPosition=current.transform.position;var stirRotation=current.transform.rotation;
            if(stage==Stage.Enter)
            {
                // Over the mouth first, then down along the wall.
                float k=clock/Mathf.Max(.01f,p.enterSeconds);var above=AboveRim();
                var from=fromHold?heldPosition:startPosition;var fromRotation=fromHold?heldRotation:startRotation;
                if(k>=1){stage=Stage.Loop;position=stirPosition;rotation=stirRotation;}
                else if(k<.5f){float u=Mathf.SmoothStep(0,1,k/.5f);position=Vector3.Lerp(from,stirPosition+above,u);rotation=Quaternion.Slerp(fromRotation,stirRotation,u);}
                else{float u=Mathf.SmoothStep(0,1,(k-.5f)/.5f);position=stirPosition+above*(1-u);rotation=stirRotation;}
            }
            else{position=stirPosition;rotation=stirRotation;}
            lastPosition=position;lastRotation=rotation;current.transform.SetPositionAndRotation(position,rotation);
        }
        private void Begin(BarSession session,ItemView spoon,ItemView into,Container vessel)
        {
            fromHold=stage==Stage.Idle||spoon!=current;startPosition=lastPosition;startRotation=lastRotation;
            bool keepPhase=!fromHold&&into==receiver;
            current=spoon;receiver=into;target=vessel;binding=ItemView.StirBinding(into,vessel);stage=Stage.Enter;clock=0;
            var c=session.GestureControls;tracker=new CircleTracker(c.CircleMinRadius,c.CircleCentreSeconds,c.CircleSmoothingSeconds,c.CircleSampleTravel);mouseAngle=laggedAngle=0;
            if(keepPhase||!binding)return;
            // Start on the side of the glass the spoon comes from, so it does not swing across the mouth.
            omega=0;var tip=spoon.Anchor("Tip");var camera=session.Player.ViewCamera.transform;Right(camera,out var right,out var forward);
            var axis=binding.transform.TransformPoint(new Vector3(binding.Shape.Axis.x,binding.Shape.Top,binding.Shape.Axis.y));
            var offset=Vector3.ProjectOnPlane((tip?tip.position:spoon.transform.position)-axis,Vector3.up);
            theta=offset.sqrMagnitude>1e-8f?Mathf.Atan2(Vector3.Dot(offset,forward),Vector3.Dot(offset,right)):-.5f*Mathf.PI;clinkTheta=theta;
        }
        /// <summary>鼠标绕滑动圆心的转角 → 平滑求导得到目标角速度 → 勺子角速度带惯性跟随。</summary>
        private void Follow(double x,double y,float dt)
        {
            tracker.Update(x,y,dt);mouseAngle+=tracker.LastDelta;
            float d=Mathf.Max(.01f,p.derivativeSeconds);laggedAngle+=(mouseAngle-laggedAngle)*(1-System.Math.Exp(-dt/d));
            float max=2*Mathf.PI*p.maxTurnsPerSecond,goal=Mathf.Clamp((float)((mouseAngle-laggedAngle)/d),-max,max);
            omega+=(goal-omega)*(1-Mathf.Exp(-dt/Mathf.Max(.01f,p.inertiaSeconds)));
        }
        private void Solve(BarSession session)
        {
            var camera=session.Player.ViewCamera.transform;Right(camera,out var right,out var forward);
            // Mouse right/up map to camera right/away, so a counter-clockwise circle on screen turns the spoon counter-clockwise seen from above.
            var direction=right*Mathf.Cos(theta)+forward*Mathf.Sin(theta);var local=binding.transform.InverseTransformDirection(direction);
            var input=new Vector2(local.x,local.z).normalized*p.inputRadiusPixels;
            var tip=current.Anchor("Tip");var grip=current.Anchor("Grip_R");if(!grip)grip=current.Anchor("Grip_L");
            float length=tip&&grip?Vector3.Distance(tip.position,grip.position):.2f,radius=current.StirSolution?.Radius??0;
            float tilt=Mathf.Min(p.maxTiltDegrees,Mathf.Atan2(radius*(1-p.topOrbitFraction),Mathf.Max(.05f,length))*Mathf.Rad2Deg);
            float lift=p.liftMetres*Speed01*(.5f+.5f*Mathf.Sin(theta+p.liftPhase));
            current.PoseStirring(receiver,target,input,tilt,lift);
        }
        private static void Right(Transform camera,out Vector3 right,out Vector3 forward)
        {
            right=Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized;forward=Vector3.Cross(right,Vector3.up);
            if(Vector3.Dot(forward,camera.forward)<0)forward=-forward;
        }
        /// <summary>把整块勺头抬到杯口上方 rimClearance 所需的位移（沿杯轴向上），按最近一次求解的勺头最低点算。</summary>
        private Vector3 AboveRim()
        {
            var solution=current.StirSolution;if(solution==null)return Vector3.zero;
            float lowest=binding.Shape.Bottom+solution.BottomGap;
            return binding.transform.TransformVector(Vector3.up*Mathf.Max(0,binding.Shape.Top+p.rimClearance-lowest));
        }
        private Vector3 RiseOut()=>binding?AboveRim():Vector3.zero;
    }
}
