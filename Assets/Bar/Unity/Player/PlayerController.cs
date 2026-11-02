using UnityEngine;

namespace Bar.Unity
{
    /// <summary>控制第一人称位置和视角；左右手锚点挂在相机下，其相机局部姿态由 HoldViewRig 按手持物取景（2026-10-28 起不再固定）。</summary>
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>第一人称相机。</summary>
        public Camera ViewCamera;
        /// <summary>仅保留水平朝向的躯干节点，不再作为手锚点父节点。</summary>
        public Transform Torso;
        /// <summary>固定在相机左下方的持物锚点。</summary>
        public Transform LeftAnchor;
        /// <summary>固定在相机右下方的持物锚点。</summary>
        public Transform RightAnchor;
        [Header("移动与场景碰撞")]
        [Min(0)] public float MoveSpeed = .85f;
        public BartenderLocomotion Locomotion;
        private Vector3 baseCameraLocal,locomotionOffset;private float locomotionRoll;
        public Vector3 BaseEyeWorld=>ViewCamera.transform.parent.TransformPoint(baseCameraLocal);
        public void SetLocomotionPose(Vector3 offset,float roll){locomotionOffset=offset;locomotionRoll=roll;ApplyLook();}
        private float yaw, pitch = 25;
        private float leanPitch;
        /// <summary>玩家自己的视角（不含观察镜头的低头）。</summary>
        public Quaternion LookRotation=>Quaternion.Euler(pitch,yaw,0);
        /// <summary>玩家自己的俯仰角（度，向下为正）。</summary>
        public float Pitch=>pitch;
        /// <summary>观察镜头：在玩家视角上叠加的低头角度（度），由 HoldViewRig 在视角锁定的倒入中设置；不改玩家自己的 yaw/pitch。</summary>
        public void Lean(float degrees){leanPitch=degrees;ApplyLook();}
        /// <summary>动作结束时把低头并入玩家自己的俯仰：镜头停在原处，此后只随鼠标动，不自动回弹。</summary>
        public void KeepLean(){pitch=Mathf.Clamp(pitch+leanPitch,-55,75);leanPitch=0;ApplyLook();}
        [SerializeField]private CharacterController body;private float verticalSpeed;
        public CharacterController Body=>body?body:EnsureBody();
        private CharacterController EnsureBody()
        {
            if(body)return body;
            var settings=PlayerEnvironmentSettings.Load();body=GetComponent<CharacterController>();if(!body)body=gameObject.AddComponent<CharacterController>();
            float eye=ViewCamera?transform.InverseTransformPoint(ViewCamera.transform.position).y:settings.minimumHeight-settings.headMargin;
            body.height=Mathf.Max(settings.minimumHeight,eye+settings.headMargin);body.radius=settings.radius;body.center=Vector3.up*body.height*.5f;
            body.skinWidth=settings.skin;body.stepOffset=settings.stepOffset;body.minMoveDistance=0;return body;
        }
        /// <summary>根据鼠标位移改变视角。</summary>
        /// <precondition>未处于连续动作的视角锁定；菜单显示不阻止调用。</precondition>
        /// <effects>改变相机偏航和俯仰；左右手保持相机局部位置与旋转不变。</effects>
        public void Look(Vector2 delta) { yaw += delta.x*.085f; pitch = Mathf.Clamp(pitch-delta.y*.085f,-55,75); ApplyLook(); }
        /// <summary>根据 WASD 输入在吧台后移动。</summary>
        /// <precondition>dt 为当前帧非负秒数，输入向量通常来自 Input System 二维组合。</precondition>
        /// <effects>按视角水平朝向经胶囊控制器扫掠移动，阻挡/沿墙滑动并受重力落地；没有 X/Z 硬边界。</effects>
        public void Move(Vector2 axis, float dt)
        {
            if(Locomotion){Locomotion.Step(axis,dt);return;}
            if(dt<=0)return;var controller=EnsureBody();var settings=PlayerEnvironmentSettings.Load();
            if(controller.isGrounded&&verticalSpeed<0)verticalSpeed=-.5f;verticalSpeed=Mathf.Max(-15,verticalSpeed-settings.gravity*dt);
            var direction=Quaternion.Euler(0,yaw,0)*new Vector3(axis.x,0,axis.y);
            controller.Move((Vector3.ClampMagnitude(direction,1)*MoveSpeed+Vector3.up*verticalSpeed)*dt);
        }
        /// <summary>将视角对准可见目标，供场景冒烟测试使用。</summary>
        /// <precondition>目标点与相机位置不同，且位于允许的头部转动范围。</precondition>
        /// <effects>改变相机偏航与俯仰，手锚点同步跟随，不自动拿取物品。</effects>
        public void AimAt(Vector3 point) { var q=Quaternion.LookRotation(point-ViewCamera.transform.position);yaw=q.eulerAngles.y;pitch=Mathf.DeltaAngle(0,q.eulerAngles.x);ApplyLook(); }
        private void Start()
        {
            baseCameraLocal=ViewCamera.transform.localPosition;
            // Respect the saved camera heading when the bartender station is moved or rotated in the scene.
            if(ViewCamera)
            {
                yaw=ViewCamera.transform.eulerAngles.y;
                pitch=Mathf.Clamp(Mathf.DeltaAngle(0,ViewCamera.transform.eulerAngles.x),-55,75);
            }
            ApplyLook();
            EnsureBody();SceneCollisionSetup.Refresh(this);
            var session=GetComponent<BarSession>();if(session){var held=GetComponent<HeldItemRendering>();if(!held)held=gameObject.AddComponent<HeldItemRendering>();held.Initialize(session);var hold=GetComponent<HoldViewRig>();if(!hold)hold=gameObject.AddComponent<HoldViewRig>();hold.Initialize(session);}
            if(Locomotion)Locomotion.Initialize(session,body);
        }
        private void ApplyLook()
        {
            if(Torso)Torso.rotation=Quaternion.Euler(0,yaw,0);
            if(!ViewCamera)return;
            ViewCamera.transform.rotation=Quaternion.Euler(Mathf.Clamp(pitch+leanPitch,-55,75),yaw,0)*Quaternion.AngleAxis(locomotionRoll,Vector3.forward);
            if(Locomotion)ViewCamera.transform.localPosition=baseCameraLocal+ViewCamera.transform.parent.InverseTransformVector(Quaternion.Euler(0,yaw,0)*locomotionOffset);
            BindAnchor(LeftAnchor,new Vector3(-.19f,-.27f,.55f));
            BindAnchor(RightAnchor,new Vector3(.20f,-.27f,.55f));
        }
        private void BindAnchor(Transform anchor,Vector3 position)
        {
            if(!anchor)return;
            // Migrate the saved U3 scene too; the user need not rebuild or replace it.
            if(anchor.parent!=ViewCamera.transform){anchor.SetParent(ViewCamera.transform,false);anchor.localPosition=position;anchor.localRotation=Quaternion.identity;}
        }
    }
}
