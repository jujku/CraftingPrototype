using System.Reflection;
using Bar.Unity;
using UnityEngine;
namespace Bar.Tests
{
    /// <summary>在原生IMGUI事件内调用真实订单绘制，只控制鼠标位置；不移动系统光标。</summary>
    public sealed class OrderHoverProbe:MonoBehaviour
    {
        public OrderOverlay Overlay;public bool Hover;
        private static readonly MethodInfo paint=typeof(OrderOverlay).GetMethod("OnGUI",BindingFlags.Instance|BindingFlags.NonPublic);
        private void OnGUI()
        {
            if(!Overlay)return;var saved=Event.current.mousePosition;var f=Overlay.FeedbackRect;
            // Isolate the real text renderer from animated bottle bubbles behind it; test-only matte.
            var color=GUI.color;GUI.color=new Color(.07f,.08f,.09f,1);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=color;
            Event.current.mousePosition=Hover?new Vector2(f.x+50,f.y+62):new Vector2(-10,-10);
            paint.Invoke(Overlay,null);Event.current.mousePosition=saved;
        }
    }
}
