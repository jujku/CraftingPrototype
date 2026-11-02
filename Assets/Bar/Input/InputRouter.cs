using Bar.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bar.Unity.Input
{
    /// <summary>把 Input System 的带时间输入传给场景会话，移动采样与领域规则保持分离。</summary>
    [DefaultExecutionOrder(-100)]public sealed class InputRouter : MonoBehaviour
    {
        /// <summary>接收输入意图的灰盒会话。</summary>
        public BarSession Session;
        private InputActionMap map;
        private InputAction move;
        private void OnEnable()
        {
            map=new InputActionMap("Bar");move=map.AddAction("Move",InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
            var look=map.AddAction("Look",InputActionType.PassThrough,"<Mouse>/delta");look.performed+=ctx=>{if(!Session.OrderInputBlocked)Session.PointerDelta(ctx.ReadValue<Vector2>(),Session.EventTime(ctx.time));};
            var left=map.AddAction("Left",InputActionType.Button,"<Mouse>/leftButton");left.started+=ctx=>{if(!Session.OrderInputBlocked&&!Session.Settings.CapturesPointer(Mouse.current.position.ReadValue()))Session.LeftButton(true,Session.EventTime(ctx.time));};left.canceled+=ctx=>{if(!Session.OrderInputBlocked&&!Session.Settings.CapturesPointer(Mouse.current.position.ReadValue()))Session.LeftButton(false,Session.EventTime(ctx.time));};
            var right=map.AddAction("Right",InputActionType.Button,"<Mouse>/rightButton");right.performed+=ctx=>{if(!Session.OrderInputBlocked&&!Session.Settings.CapturesPointer(Mouse.current.position.ReadValue()))Session.RightButton(Session.EventTime(ctx.time));};
            var scroll=map.AddAction("MenuScroll",InputActionType.PassThrough,"<Mouse>/scroll/y");scroll.performed+=ctx=>{float y=ctx.ReadValue<float>();if(!Session.OrderInputBlocked&&!Session.Settings.CapturesPointer(Mouse.current.position.ReadValue())&&Mathf.Abs(y)>.01f)Session.SelectMenu(y>0?-1:1,false);};
            for(int i=1;i<=9;i++){int selection=i-1;var number=map.AddAction("Menu"+i,InputActionType.Button,"<Keyboard>/"+i);number.performed+=_=>{if(!Session.OrderInputBlocked)Session.SelectMenu(selection,true);};}
            var tab=map.AddAction("MainHand",InputActionType.Button,"<Keyboard>/tab");tab.performed+=ctx=>{if(!Session.OrderInputBlocked)Session.ToggleMain(Session.EventTime(ctx.time));};
            var cancel=map.AddAction("Cancel",InputActionType.Button,"<Keyboard>/escape");cancel.performed+=ctx=>{if(Session.Settings.IsOpen){Session.Settings.Toggle();return;}if(!Session.OrderInputBlocked)Session.Cancel(Session.EventTime(ctx.time));};
            // V3 (2026-10-28): F plays with an empty jigger (display only), H mirrors left/right-handed.
            var inspect=map.AddAction("Inspect",InputActionType.Button,"<Keyboard>/f");inspect.performed+=ctx=>{if(!Session.OrderInputBlocked)Session.Inspect(Session.EventTime(ctx.time));};
            var handed=map.AddAction("Handedness",InputActionType.Button,"<Keyboard>/h");handed.performed+=ctx=>{if(!Session.OrderInputBlocked)Session.ToggleHandedness(Session.EventTime(ctx.time));};
            var settings=map.AddAction("Settings",InputActionType.Button,"<Keyboard>/f10");settings.performed+=_=>Session.Settings.Toggle();
            var debug=map.AddAction("Debug",InputActionType.Button,"<Keyboard>/f3");debug.performed+=_=>Session.ToggleDebug();map.Enable();
        }
        private void Update(){if(Session&&Session.Player.Locomotion)Session.Player.Move(move.ReadValue<Vector2>(),Time.unscaledDeltaTime);else if(Session&&!Session.OrderInputBlocked&&!Session.LookLocked)Session.Player.Move(move.ReadValue<Vector2>(),Time.unscaledDeltaTime);}
        private void OnDisable(){map?.Disable();map?.Dispose();map=null;}
    }
}
