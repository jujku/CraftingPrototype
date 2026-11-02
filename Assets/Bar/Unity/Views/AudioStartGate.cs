using UnityEngine;
namespace Bar.Unity
{
    /// <summary>Web 音频只在用户点击开始后播放；桌面默认开启。</summary>
    [DefaultExecutionOrder(1000)]public sealed class AudioStartGate:MonoBehaviour
    {
        private Font font;
        public bool Unlocked {get;private set;}
        private void Awake()
        {
            font=Resources.Load<Font>("BarUISubsetSC");
            #if UNITY_WEBGL && !UNITY_EDITOR
            Unlocked=false;
            #else
            Unlocked=true;
            #endif
        }
        public void RequireInteraction(){Unlocked=false;}
        public void Unlock(){Unlocked=true;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        private void LateUpdate(){if(!Unlocked){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
        private void OnGUI()
        {
            if(Unlocked)return;var previous=GUI.skin.font;if(font)GUI.skin.font=font;GUI.Box(new Rect(0,0,Screen.width,Screen.height),"");
            GUI.Label(new Rect(Screen.width/2-180,Screen.height/2-75,360,40),UiText.Translate("调酒工作台 · 点击开始以启用声音"));
            if(GUI.Button(new Rect(Screen.width/2-100,Screen.height/2-10,200,55),UiText.Translate("点击开始")))Unlock();GUI.skin.font=previous;
        }
    }
}
