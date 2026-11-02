using System;
using System.Collections.Generic;
using Bar.Data;
using Bar.Domain;
using UnityEngine;
namespace Bar.Unity
{
    /// <summary>只读的酒吧HUD文案；不改领域动作名称、前置条件和输入绑定。</summary>
    public static class HudText
    {
        private static bool IsTarget(Item item,Item hovered)
        {if(item==null||hovered==null)return false;for(var p=item;p!=null;p=p.Parent)if(ReferenceEquals(p,hovered))return true;return hovered is CobblerShaker s&&ReferenceEquals(item,s.Body)||hovered is Jigger j&&(ReferenceEquals(item,j.CupA)||ReferenceEquals(item,j.CupB));}
        public static int Priority(ActionOption option,Item hovered,Hands hands)
        {if(hovered is Glass glass&&glass.Definition.Id=="glass.highball"&&(hands.Left is BarSpoon||hands.Right is BarSpoon)){if(option.Action.Id=="pour.bottle_to_container"&&IsTarget(option.Target,hovered))return -2;if(option.Action.Id=="pour.barspoon_guided")return -1;}if(IsTarget(option.Target,hovered))return hands.SideOf(option.Actor).HasValue&&!ReferenceEquals(option.Actor,option.Target)?0:1;return option.Action.Id=="jigger.flip"?3:2;}
        public static bool Hold(ActionOption o)=>o.Action.InputStyle==InputStyle.HoldTilt||o.Action.InputStyle==InputStyle.HoldPress||o.Action.InputStyle==InputStyle.HoldOscillate||o.Action.InputStyle==InputStyle.HoldCircle;
        public static string Badge(ActionOption o,Hands hands)
        {
            string hold=Hold(o)?UiText.T("[按住]","[Hold]"):"";var side=hands.SideOf(o.Actor??o.Target);
            if(!side.HasValue||side.Value==hands.MainHand)return hold;
            string hand=side==HandSide.Left?UiText.T("[左手]","[Left]"):UiText.T("[右手]","[Right]");return hold.Length==0?hand:hold+" "+hand;
        }
        public static string Name(Item item)=>item is CobblerShaker?UiText.T("雪克壶","Shaker"):item is Jigger?UiText.T("量酒器","Jigger"):UiText.Translate(item?.Definition.DisplayName??"");
        public static string Line(ActionOption o)
        {
            switch(o.Action.Id)
            {
                case "pour.barspoon_guided":return "沿吧勺引流";
                case "shaker.wipe":return "擦干壶身";
                case "shaker.attach_strainer":return "盖上滤冰器";
                case "shaker.detach_strainer":return "取下滤冰器";
                case "shaker.attach_cap":return "盖上壶盖";
                case "shaker.detach_cap":return "取下壶盖";
                case "jigger.flip":return "翻转量酒器 (30ml ⇄ 45ml)";
                case "pour.bottle_to_container":case "pour.jigger_to_container":case "shaker.pour":return UiText.T("倒入","Pour into ")+Name(o.Target);
                case "sink.empty":return "倒入水槽";
                default:return o.Action.Label;
            }
        }
        public static string Slot(Item item)=>item==null?"—":item is Jigger j?UiText.Translate(item.Definition.DisplayName)+" · "+j.ActiveCup.NominalMl.ToString("0")+"ml":UiText.Translate(item.Definition.DisplayName);
        public static bool Inspectable(Item item)=>InspectionPolicy.CanInspect(item);
        public static double Liquid(Item item)=>item is Bottle b?b.RemainingMl:item is Jigger j?j.ActiveCup.Contents.LiquidMl:item is CobblerShaker s?s.Body.Contents.LiquidMl:item is Container c?c.Contents.LiquidMl:0;
    }
    /// <summary>无框焦点滚轮、动态准星、双手槽。复用项目二阶弹簧，只观察会话。</summary>
    [DefaultExecutionOrder(200)]public sealed class SpeakeasyHud:MonoBehaviour
    {
        private BarSession session;private Font font;private GUIStyle text,small;private readonly GUIContent measure=new GUIContent();
        private SecondOrderFloat focus,hand,radius;private SecondOrderVector follow;private ItemView lastTarget;
        private readonly Color ivory=new Color(1,.992f,.976f),gold=new Color(.898f,.663f,.235f),header=new Color(.902f,.843f,.722f,.75f);
        private Texture2D dot;private readonly RingCanvas ring=new RingCanvas(),arc=new RingCanvas();private ActiveAction holding;private double initialLiquid;private float holdSeconds;
        private sealed class Row{public Item Actor,Target;public IBarAction Action;public HandSide? Side;public HandSide Main;public int Language=-1;public string Label,Badge;}
        private readonly List<Row> rows=new List<Row>();
        private int slotLanguage=-1;
        private readonly Item[] slotItems=new Item[2];private readonly int[] slotEnds={-1,-1};private readonly string[] slotLabels={"—","—"};
        public float FocusPosition=>focus?.Value??0;public float HandPosition=>hand?.Value??1;public float HoldProgress {get;private set;}
        public Vector2 MenuPosition=>follow==null?Vector2.zero:(Vector2)follow.Value;
        public Rect ActionRect(int index)=>new Rect(MenuPosition.x+12,MenuPosition.y+40+index*28,320,27);
        public Rect TabPromptRect=>new Rect(Screen.width/Scale*.5f-32,Screen.height/Scale-68,64,25);
        public Rect InspectPromptRect(HandSide side)=>new Rect(Screen.width/Scale*.5f+(side==HandSide.Left?-225:41),Screen.height/Scale-39,184,22);
        public void Initialize(BarSession owner,Font uiFont){session=owner;font=uiFont;focus=new SecondOrderFloat(5,1,0,0);hand=new SecondOrderFloat(4,.85f,0,owner.Hands.MainHand==HandSide.Left?0:1);radius=new SecondOrderFloat(6,.85f,0,1);}
        private void LateUpdate()
        {
            if(!session||session.Clock==null)return;float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);float scale=Scale;
            var camera=session.Player.ViewCamera;Vector3 screen=session.Target?camera.WorldToScreenPoint(session.HudTargetPoint):new Vector3(Screen.width*.5f,Screen.height*.5f,1);
            float w=Screen.width/scale,h=Screen.height/scale;
            float menuHeight=40+session.MenuOptions.Count*28+52;
            var desired=new Vector3(Mathf.Clamp(screen.x/scale+34,18,Mathf.Max(18,w-350)),Mathf.Clamp((Screen.height-screen.y)/scale-48,24,Mathf.Max(24,h-menuHeight-88)),0);
            if(follow==null)follow=new SecondOrderVector(3.5f,.9f,0,desired);
            follow.Update(dt,desired);
            if(lastTarget!=session.Target){focus.Reset(session.SelectedMenuIndex);lastTarget=session.Target;}
            focus.Update(dt,session.SelectedMenuIndex);hand.Update(dt,session.Hands.MainHand==HandSide.Left?0:1);
            radius.Update(dt,session.Target&&(session.MenuOpen||session.SecondaryHint!=null)||session.HudHolding?9:1);
            var active=session.Clock.Active;
            if(session.HudHolding)
            {
                if(holding!=active){holding=active;initialLiquid=HudText.Liquid(active?.Actor);holdSeconds=0;}
                holdSeconds+=dt;
                HoldProgress=initialLiquid>0?(float)Math.Max(0,(initialLiquid-HudText.Liquid(active?.Actor))/initialLiquid):session.HudPressFraction;
            }
            else{holding=null;holdSeconds=0;HoldProgress=0;}
        }
        private float Scale=>Mathf.Clamp(Screen.height/900f,1,1.35f);
        private void Prepare()
        {
            if(text!=null){text.font=small.font=UiText.DisplayFont(font);return;}text=new GUIStyle(GUI.skin.label){font=UiText.DisplayFont(font),fontSize=18,fontStyle=FontStyle.Bold,clipping=TextClipping.Clip,wordWrap=false,padding=new RectOffset(0,0,0,0)};small=new GUIStyle(text){fontSize=12,fontStyle=FontStyle.Normal};
            dot=Texture();
        }
        private static Texture2D Texture()
        {
            var t=new Texture2D(64,64,TextureFormat.RGBA32,false);var pixels=new Color[4096];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32));pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(30-d));}
            t.SetPixels(pixels);t.Apply();return t;
        }
        /// <summary>
        /// 2026-11-01 用户在浏览器试玩：准星外圈和金色进度弧断成一圈点。原来外圈是 64 像素贴图缩到约 20 像素画（环宽不到 1 像素），
        /// 金弧是 64 段 1.5 像素的旋转小方块，缩小和旋转后都没有抗锯齿。现在按屏幕实际像素把环画进一张小贴图（1 纹素 = 1 屏幕像素，
        /// 径向和弧端按距离做抗锯齿），外圈和金弧共用；参数不变时不重画。半径、动画和颜色照旧。
        /// </summary>
        private sealed class RingCanvas
        {
            private Texture2D texture;private Color32[] pixels;private int size;private Vector4 last=new Vector4(-1,-1,-1,-1);
            public void Draw(Vector2 center,float radius,float width,float from,float fraction,float scale)
            {
                float r=radius*scale,wPx=width*scale;int need=Mathf.CeilToInt(r+wPx+2)*2;
                if(need>size){if(texture)UnityEngine.Object.Destroy(texture);size=need;texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};pixels=new Color32[size*size];last.x=-1;}
                var key=new Vector4(r,wPx,Mathf.Repeat(from,360),Mathf.Clamp01(fraction));
                if(key!=last){Paint(key.x,key.y,key.z,key.w);last=key;}
                var c=center*scale;GUI.DrawTexture(new Rect((Mathf.Round(c.x)-size*.5f)/scale,(Mathf.Round(c.y)-size*.5f)/scale,size/scale,size/scale),texture);
            }
            private void Paint(float r,float w,float from,float fraction)
            {
                float half=size*.5f,span=fraction*360;
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float dx=x+.5f-half,down=size-1-y+.5f-half,d=Mathf.Sqrt(dx*dx+down*down);float a=Mathf.Clamp01(w*.5f+.5f-Mathf.Abs(d-r));
                    if(a>0&&fraction<1)
                    {
                        // Clockwise from 12 o'clock in screen space, as the old segment arc; soften both ends by arc length.
                        float rel=Mathf.Repeat(Mathf.Atan2(dx,-down)*Mathf.Rad2Deg-from,360);if(rel>180+span*.5f)rel-=360;
                        a*=Mathf.Clamp01(Mathf.Min(rel,span-rel)*Mathf.Deg2Rad*r+.5f);
                    }
                    pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255));
                }
                texture.SetPixels32(pixels);texture.Apply(false);
            }
            public void Dispose(){if(texture)UnityEngine.Object.Destroy(texture);}
        }
        private void Label(Rect rect,string value,Color color,int size=14,bool bold=false)
        {
            value=UiText.Translate(value);
            text.fontSize=size;text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;
            GUI.color=new Color(0,0,0,color.a*.85f);GUI.Label(new Rect(rect.x+1,rect.y+2,rect.width,rect.height),value,text);
            GUI.color=color;GUI.Label(rect,value,text);GUI.color=Color.white;
        }
        private void Line(Rect rect,Color color){GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=Color.white;}
        private void Arc(Vector2 center,float r,float from,float fraction,Color color)
        {
            if(fraction<=0)return;GUI.color=color;arc.Draw(center,r,1.5f,from,fraction,Scale);GUI.color=Color.white;
        }
        private void OnGUI()
        {
            if(!session||session.Clock==null||session.Settings.IsOpen||Event.current.type!=EventType.Repaint)return;Prepare();var oldMatrix=GUI.matrix;var oldColor=GUI.color;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Scale,Scale,1));float w=Screen.width/Scale,h=Screen.height/Scale;
            var center=new Vector2(w*.5f,h*.5f);float r=Mathf.Max(1,radius.Value),expand=Mathf.Clamp01((r-1)/8);
            GUI.color=new Color(1,.992f,.976f,1-expand);GUI.DrawTexture(new Rect(center.x-1,center.y-1,2,2),dot);
            GUI.color=new Color(1,.992f,.976f,expand*.8f);ring.Draw(center,(r+1)*.875f,Mathf.Max(1f/Scale,(r+1)/16),0,1,Scale);GUI.color=Color.white;
            if(session.HudHolding){if(holding?.Action.InputStyle==InputStyle.HoldTilt||holding?.Action.InputStyle==InputStyle.HoldPress)Arc(center,r,0,Mathf.Clamp01(HoldProgress),gold);else Arc(center,r,holdSeconds*120,.24f,gold);}
            DrawMenu();DrawHands(w,h);
            if(session.HudSuspended)Label(new Rect(center.x-100,center.y+28,220,24),"点击恢复",new Color(.902f,.843f,.722f,.65f),12);
            GUI.color=oldColor;GUI.matrix=oldMatrix;
        }
        private void DrawMenu()
        {
            if(session.HudHolding||!session.MenuOpen&&session.SecondaryHint==null)return;
            var p=MenuPosition;var options=session.MenuOptions;float bottom=p.y+34;
            if(session.Target){Label(new Rect(p.x,p.y,320,24),UiText.Translate(session.Target.Item.Definition.DisplayName),header,13);Line(new Rect(p.x,p.y+28,210,1),new Color(gold.r,gold.g,gold.b,.42f));}
            for(int i=0;i<options.Count;i++)
            {
                var o=options[i];bool selected=i==session.SelectedMenuIndex;
                const int size=16;float y=ActionRect(i).y;bottom=Mathf.Max(bottom,y+27);
                var color=new Color(ivory.r,ivory.g,ivory.b,selected?1:.35f);
                while(rows.Count<=i)rows.Add(new Row());var row=rows[i];var side=session.Hands.SideOf(o.Actor??o.Target);
                if(row.Action!=o.Action||row.Actor!=o.Actor||row.Target!=o.Target||row.Side!=side||row.Main!=session.Hands.MainHand||row.Language!=UiText.Revision)
                {row.Language=UiText.Revision;row.Action=o.Action;row.Actor=o.Actor;row.Target=o.Target;row.Side=side;row.Main=session.Hands.MainHand;row.Label=UiText.Translate(HudText.Line(o));row.Badge=HudText.Badge(o,session.Hands);}
                string label=row.Label;Label(ActionRect(i),label,color,size);
                text.fontSize=size;text.fontStyle=FontStyle.Normal;measure.text=label;float width=text.CalcSize(measure).x;
                Label(new Rect(p.x+width+23,y+3,125,22),row.Badge,new Color(.902f,.843f,.722f,selected ? .65f : .3f),11);
            }
            if(options.Count>0)Line(new Rect(p.x,p.y+44+Mathf.Clamp(focus.Value,0,options.Count-1)*28,2,18),gold);
            if(session.SecondaryHint!=null)Label(new Rect(p.x+12,bottom+14,270,23),session.SecondaryHint,new Color(.72f,.72f,.7f,.65f),12);
        }
        private void DrawHands(float w,float h)
        {
            if(slotLanguage!=UiText.Revision){slotLanguage=UiText.Revision;slotItems[0]=slotItems[1]=null;slotEnds[0]=slotEnds[1]=-1;}
            float slotWidth=184,gap=82,left=w*.5f-slotWidth-gap*.5f,right=w*.5f+gap*.5f,y=h-78;
            for(int i=0;i<2;i++)
            {
                var side=i==0?HandSide.Left:HandSide.Right;var item=session.Hands.Get(side);float x=i==0?left:right;bool primary=session.Hands.MainHand==side;
                int end=item is Jigger j?(int)j.ActiveCup.NominalMl:0;
                if(slotItems[i]!=item||slotEnds[i]!=end){slotItems[i]=item;slotEnds[i]=end;slotLabels[i]=HudText.Slot(item);}
                Label(new Rect(x,y,slotWidth+35,26),slotLabels[i],new Color(ivory.r,ivory.g,ivory.b,item==null ? .22f : primary ? 1 : .35f),16,primary);
                Line(new Rect(x,y+32,slotWidth,1),new Color(.3f,.3f,.29f,.4f));
                if(!session.Hands.IsBusy&&!session.HudHolding&&HudText.Inspectable(item))
                Label(InspectPromptRect(side),"[F] 检视",new Color(.88f,.85f,.77f,.5f),12);
            }
            float underline=Mathf.LerpUnclamped(left,right,hand.Value);Line(new Rect(underline-1,y+31,slotWidth+2,4),new Color(gold.r,gold.g,gold.b,.13f));Line(new Rect(underline,y+32,slotWidth,2),gold);
            Label(TabPromptRect,"[Tab ⇄]",new Color(.88f,.85f,.77f,.5f),12);
        }
        public void DrawNotice(string message)
        {if(string.IsNullOrEmpty(message)||Event.current.type!=EventType.Repaint)return;Prepare();Label(new Rect(28,28,Screen.width-56,28),message,new Color(.902f,.843f,.722f,.85f),13);}
        private void OnDestroy(){if(dot)Destroy(dot);ring.Dispose();arc.Dispose();}
    }
}
