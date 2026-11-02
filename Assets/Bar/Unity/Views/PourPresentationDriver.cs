using UnityEngine;
namespace Bar.Unity
{
    /// <summary>在部件动画之后应用统一倒液姿态，避免 ItemView 的量酒器换端覆盖站位。</summary>
    [DefaultExecutionOrder(50)]public sealed class PourPresentationDriver:MonoBehaviour
    {
        public BarSession Session;public float Tilt,Press;
        public PourPresentation Presentation {get;private set;}
        public void Initialize(){Presentation??=new PourPresentation(Session);}
        public void PresentNow(){if(Session&&Session.enabled)(Presentation??=new PourPresentation(Session)).Tick(Time.unscaledDeltaTime,Tilt,Press);}
        private void LateUpdate(){if(Session&&Session.enabled)(Presentation??=new PourPresentation(Session)).Tick(0,Tilt,Press);}
    }
}
