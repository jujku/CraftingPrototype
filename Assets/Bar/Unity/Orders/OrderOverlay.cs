using UnityEngine;
namespace Bar.Unity
{
    /// <summary>独立订单界面：无框、暖白与琥珀细线，不改变现有 SpeakeasyHud。</summary>
    public sealed class OrderOverlay:MonoBehaviour
    {
        private OrderLoop loop;private Font font;
        private static readonly Color Ivory=new Color(1,.992f,.976f,1),Gold=new Color(.898f,.663f,.235f,.85f),Muted=new Color(.902f,.843f,.722f,.75f);
        public void Initialize(OrderLoop owner,Font fallback){loop=owner;font=fallback;}
        public Rect CardRect=>new Rect(30,26,Mathf.Min(310,Screen.width*.39f),130);
        public Rect FeedbackRect=>new Rect(Screen.width*.5f-Mathf.Min(240,Screen.width*.42f),Screen.height*.35f,Mathf.Min(480,Screen.width*.84f),190);
        private GUIStyle Style(int size,Color color,bool bold=false)
        {
            var style=new GUIStyle(GUI.skin.label){font=UiText.DisplayFont(font),fontSize=size,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,wordWrap=true};
            // GUI.Label also selects hover states. A shadow must remain dark in every state.
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})state.textColor=color;
            return style;
        }
        private void Text(Rect r,string text,int size,Color color,bool bold=false)
        {text=UiText.Translate(text);var shadow=Style(size,new Color(0,0,0,.85f),bold);GUI.Label(new Rect(r.x+1,r.y+2,r.width,r.height),text,shadow);GUI.Label(r,text,Style(size,color,bold));}
        private void Line(Rect r,Color color){var previous=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=previous;}
        private bool Button(Rect r,string text)
        {
            bool over=r.Contains(Event.current.mousePosition);Text(r,text,16,over?Ivory:Muted,true);Line(new Rect(r.x,r.yMax-3,r.width,over?2:1),over?Gold:new Color(.9f,.8f,.6f,.3f));
            return GUI.Button(r,GUIContent.none,GUIStyle.none);
        }
        private void OnGUI()
        {
            if(!loop||loop.DebugVisible||loop.SettingsOpen)return;var old=GUI.color;GUI.color=Color.white;
            if(!loop.Complete)
            {
                var c=CardRect;Text(new Rect(c.x,c.y,c.width,20),"今夜的点单",12,Muted);
                // The shared CJK font can require more than 30 pixels at size 22.
                // Measure its real line box, then move the remaining card content with it.
                float titleHeight=Mathf.Max(30,Mathf.Ceil(Style(22,Ivory,true).CalcHeight(new GUIContent(UiText.Translate(loop.Current.name)),c.width)));
                var title=new Rect(c.x,c.y+23,c.width,titleHeight);Text(title,loop.Current.name,22,Ivory,true);
                float divider=title.yMax+5;Line(new Rect(c.x,divider,Mathf.Min(c.width,245),1),Gold);
                Text(new Rect(c.x,divider+10,c.width,23),loop.Current.glassLabel,13,Muted);
                Text(new Rect(c.x,divider+38,c.width,80),loop.Current.description,14,Ivory);
            }
            if(loop.ModalOpen)
            {
                var f=FeedbackRect;
                Text(new Rect(f.x,f.y,f.width,25),loop.Complete?"今夜，收工":"来自客人的话",14,Muted);
                Line(new Rect(f.x,f.y+34,f.width,1),Gold);
                string text=loop.Complete?"三杯已上齐。谢谢你今夜的招待。":string.Join("\n",System.Array.ConvertAll(loop.FeedbackIds,loop.Catalog.Text));
                Text(new Rect(f.x,f.y+50,f.width,88),text,20,Ivory);
                if(!loop.Complete)
                {
                    if(Event.current.type==EventType.Repaint)loop.MarkPresented();
                    if(Button(new Rect(f.x,f.y+146,f.width*.44f,36),"再做一次"))loop.Advance(true);
                    if(Button(new Rect(f.x+f.width*.56f,f.y+146,f.width*.44f,36),"下一单"))loop.Advance(false);
                }
            }
            GUI.color=old;
        }
    }
}
