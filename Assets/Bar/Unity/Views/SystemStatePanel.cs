using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bar.Domain;
using Bar.Logging;
using CraftingResearch.CoreV2;
using UnityEngine;

namespace Bar.Unity
{
    /// <summary>Read-only debug inspector. Observes confirmed events; never advances or writes the simulation.</summary>
    public sealed class SystemStatePanel : IDisposable
    {
        private sealed class Timing { public double shake, stir; }
        private readonly BarSession session;
        private readonly CueSettings cues;
        private readonly Dictionary<string, Timing> timing = new Dictionary<string, Timing>();
        private readonly Dictionary<string, Container> containers = new Dictionary<string, Container>();
        private GUIStyle normal, heading, highlight, muted;
        private Vector2 scroll;
        private string pinned;
        public string InspectedId { get; private set; }
        public int RenderedFrame { get; private set; } = -1;

        public SystemStatePanel(BarSession session, CueSettings cues)
        {
            this.session = session; this.cues = cues;
            foreach (var view in session.Views) Register(view.Item);
            session.Adapter.Events.Published += Observe;
        }
        private void Register(Item item)
        {
            if (item is Container c) containers[c.CoreVesselId] = c;
            if (item is CompositeItem composite) foreach (var part in composite.Parts) Register(part);
        }
        private void Observe(DomainEvent e)
        {
            if (e.Kind != "core_operation" || e.Observation is not LogRecord row || row.rejected) return;
            var op = row.operation;
            if (op.type != "Shake" && op.type != "Stir") return;
            if (!timing.TryGetValue(op.target, out var t)) { t = new Timing(); timing.Add(op.target, t); }
            if (op.type == "Shake") t.shake += op.seconds.Value; else t.stir += op.seconds.Value;
        }
        public void Dispose() => session.Adapter.Events.Published -= Observe;
        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string F(double? value) => value.HasValue ? F(value.Value) : "N/A";
        private static bool Empty(Vessel v) => v.volume == 0 && v.ice == 0;
        private static bool Contains(Item item, string id) => item is Container c && c.CoreVesselId == id ||
            item is CompositeItem composite && composite.Parts.Any(p => Contains(p, id));
        private bool Focused(Vessel v) => session.Target && Contains(session.Target.Item, v.id);
        private bool Held(Vessel v) => Contains(session.Hands.Left, v.id) || Contains(session.Hands.Right, v.id);

        public int Page { get; set; }
        private bool vesselList, minimized;
        private float Scale => Mathf.Clamp(Screen.height / 1080f, 1f, 1.15f);
        private float PageHeight => vesselList ? 535 : Page == 0 ? 375 : Page == 1 ? 475 : Page == 2 ? 405 : 470;
        public Rect Bounds => new Rect(16, 18, Mathf.Min(440 * Scale, Screen.width - 32), (minimized ? 46 : Mathf.Min(PageHeight, (Screen.height - 36) / Scale)) * Scale);
        private static string T(string zh, string en) => UiText.T(zh, en);
        private void Note(string text) => GUILayout.Label(text, muted);
        public void Draw(Font font)
        {
            if (normal == null)
            {
                normal = new GUIStyle(GUI.skin.label) { font = font, fontSize = 13, wordWrap = true,
                    padding = new RectOffset(), margin = new RectOffset(0, 0, 1, 1) };
                normal.normal.textColor = new Color(.9f, .94f, .98f);
                heading = new GUIStyle(normal) { fontSize = 15, fontStyle = FontStyle.Bold };
                highlight = new GUIStyle(normal); highlight.normal.textColor = new Color(1, .76f, .32f);
                muted = new GUIStyle(normal) { fontSize = 12 }; muted.normal.textColor = new Color(.65f, .73f, .81f);
            }
            normal.font = heading.font = highlight.font = muted.font = font;
            var vessels = session.Adapter.World.Vessels.OrderByDescending(Focused).ThenByDescending(Held)
                .ThenByDescending(v => v.m > 0).ThenByDescending(v => v.ice > 0).ThenBy(v => v.id).ToArray();
            var selected = vessels.FirstOrDefault(v => v.id == pinned && !Empty(v)) ?? vessels.FirstOrDefault(v => !Empty(v));
            InspectedId = selected?.id;
            var bounds = Bounds; var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
            GUI.depth = -100; GUI.color = new Color(.025f, .04f, .06f, .95f);
            GUI.DrawTexture(bounds, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.matrix = Matrix4x4.TRS(new Vector3(bounds.x, bounds.y, 0), Quaternion.identity, new Vector3(Scale, Scale, 1));
            GUILayout.BeginArea(new Rect(12, 9, bounds.width / Scale - 24, bounds.height / Scale - 18));
            GUILayout.BeginHorizontal(); GUILayout.Label(T("系统透视 · 只读", "System inspector · Read only"), heading);
            if (GUILayout.Button(minimized ? "+" : "−", GUILayout.Width(26))) minimized = !minimized;
            if (GUILayout.Button("×", GUILayout.Width(26))) session.ToggleDebug(); GUILayout.EndHorizontal();
            if (!minimized)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((vesselList ? "▾ " : "▸ ") + (selected == null ? T("选择容器", "Select vessel") : Name(selected)), normal)) vesselList = !vesselList;
                if (pinned != null && GUILayout.Button(T("自动", "Auto"), GUILayout.Width(48))) pinned = null;
                GUILayout.EndHorizontal();
                if (selected != null) Note(F(selected.volume) + " ml   ·   " + T("冰 ", "Ice ") + F(selected.ice) + " g   ·   " + F(selected.Snapshot().temperature) + " °C");
                int next = GUILayout.Toolbar(Page, new[] { T("概览", "Overview"), T("状态", "State"), T("过程", "Process"), T("订单", "Order") });
                if (next != Page) { Page = next; scroll = Vector2.zero; }
                scroll = GUILayout.BeginScrollView(scroll);
                if (vesselList)
                {
                    foreach (var v in vessels)
                    {
                        string status = (Focused(v) ? T(" [准星]", " [Aim]") : "") + (Held(v) ? T(" [手持]", " [Held]") : "") + (v.served ? T(" [已出杯]", " [Served]") : "");
                        string row = Name(v) + "  " + F(v.volume) + " ml / " + F(v.ice) + " g" + status;
                        if (Empty(v)) Note(row + T(" · 空", " · Empty"));
                        else if (GUILayout.Button(row, v == selected ? highlight : normal)) { pinned = v.id; vesselList = false; scroll = Vector2.zero; }
                    }
                    GUILayout.Space(6);
                }
                if (selected == null) Note(T("加液体或冰块后显示实时状态。", "Add liquid or ice to inspect live state."));
                else
                {
                    switch (Page)
                    {
                        case 0: Overview(selected); break;
                        case 1: State(selected); break;
                        case 2: Process(selected); break;
                        case 3: Preview(selected, selected.Snapshot(), true); break;
                    }
                }
                GUILayout.EndScrollView();
                Note(T("F3 显隐 · Esc 释放鼠标后可点选", "F3 toggle · Esc frees cursor for tabs"));
            }
            GUILayout.EndArea(); GUI.matrix = matrix; GUI.color = color; GUI.depth = depth;
            if (Event.current.type == EventType.Repaint) RenderedFrame = Time.frameCount;
        }
        private string Name(Vessel v) => UiText.Translate(containers[v.id].Definition.DisplayName);
        private void Overview(Vessel v)
        {
            var s = v.Snapshot();
            GUILayout.Label(T("实时终点描述符", "Live endpoint descriptors"), heading);
            Line(T("稀释 / 酒精度", "Dilution / ABV"), F(s.dilutionPercent) + "% / " + F(s.abvPercent) + "%");
            Line(T("糖酸比 / 当前温度", "Sugar-acid ratio / Temp"), F(s.sugarAcidRatio) + " / " + F(s.temperature) + " °C");
            Line(T("含气量 / 保留率", "CO₂ / Retention"), F(s.co2GL) + " g/L / " + (s.co2Retention.HasValue ? F(s.co2Retention.Value * 100) + "%" : "N/A"));
            var t = TimingFor(v);
            Line(T("摇制 / 搅拌", "Shake / Stir"), F(t.shake) + " / " + F(t.stir) + " s");
            Note(T("计时仅属本容器本次尝试，不随倒液继承。", "Timing is local to this vessel and attempt; not transferred."));
            GUILayout.Space(8); Preview(v, s, false);
        }
        private static readonly Timing NoAgitation = new Timing();
        private Timing TimingFor(Vessel v) => timing.TryGetValue(v.id, out var t) ? t : NoAgitation;
        private void State(Vessel v)
        {
            var s = v.Snapshot();
            GUILayout.Label(T("组分账本", "Component ledger"), heading);
            Line(T("液体体积 / 质量", "Liquid volume / Mass"), F(s.volumeMl) + " ml / " + F(s.liquidGrams) + " g");
            Line(T("固相冰 / 累计融冰", "Solid ice / Melt"), F(s.iceGrams) + " / " + F(s.meltGrams) + " g");
            Line(T("乙醇 / 糖 / 酸", "Ethanol / Sugar / Acid"), F(v.ethanol) + " / " + F(v.sugar) + " / " + F(v.acid) + " g");
            Line(T("水 / 溶解 CO₂", "Water / Dissolved CO₂"), F(v.water) + " / " + F(v.gas) + " g");
            Line(T("液体 / 壁面能量", "Liquid / Wall energy"), F(v.E) + " / " + F(v.vesselEnergy) + " J");
            GUILayout.Space(5); GUILayout.Label(T("热力学", "Thermodynamics"), heading);
            Line(T("液温 / 壁温", "Liquid / Wall temp"), F(s.temperature) + " / " + F(s.vesselTemperature) + " °C");
            var fp = s.liquidGrams > 0 ? session.Adapter.World.IceFreezing(v.ethanol, v.sugar, v.m) : null;
            Line(T("溶液冰点", "Freezing point"), fp == null ? T("N/A · 无液体", "N/A · No liquid") : fp.outside ? T("超出模型有效域", "Outside model domain") : F(fp.temperature) + " °C");
            Line(T("冰交换暂停", "Ice exchange paused"), s.iceExchangePaused ? T("是", "Yes") : T("否", "No"));
            Line(T("相态", "Phase"), s.iceGrams > 0 ? T("仍有固相冰", "Solid ice present") : T("无剩余固相冰", "No solid ice"));
            GUILayout.Space(5); GUILayout.Label(T("碳酸与实际视觉线索", "Carbonation & displayed cues"), heading);
            Line("co2GL / co2Retention", F(s.co2GL) + " g/L / " + F(s.co2Retention));
            if (s.co2GL.HasValue)
            {
                var curve = cues.bubbleCount; double gas = s.co2GL.Value;
                int segment = 1; while (segment < curve.Length - 1 && gas > curve[segment].x) segment++;
                string band = gas <= curve[0].x ? "≤ " + F(curve[0].x) : gas >= curve[curve.Length - 1].x ? "≥ " + F(curve[curve.Length - 1].x) : F(curve[segment - 1].x) + "–" + F(curve[segment].x);
                double mapped = Math.Min(cues.bubbleMax, Math.Round(CueSettings.Map(curve, gas)));
                var rendered = session.Adapter.DisplayedCues.FirstOrDefault(c => c.vesselId == v.id);
                Line(T("视觉分档 / 映射泡数", "Cue band / Mapped bubbles"), band + " g/L / " + F(mapped));
                Line(T("实际呈现泡数", "Displayed bubbles"), rendered == null ? T("无线索绑定", "No cue binding") : F(rendered.bubbleCount) + (rendered.liquidVisible ? T(" · 液体可见", " · Visible") : T(" · 液体隐藏或离屏", " · Hidden / off screen")));
            }
            Note(v.id);
        }
        private void Process(Vessel v)
        {
            var s = v.Snapshot(); var t = TimingFor(v);
            GUILayout.Label(T("过程记忆", "Process memory"), heading);
            Line(T("累计摇制", "ShakeTime"), F(t.shake) + " s");
            Line(T("累计搅拌", "StirTime"), F(t.stir) + " s");
            Line(T("摇搅总时长", "Total agitation"), F(t.shake + t.stir) + " s");
            Note(T("计时仅属本容器本次尝试，不随倒液继承。", "Timing is local to this vessel and attempt; not transferred."));
            Line(T("碳酸注入方式", "Carbonated input"), Method(s.carbonatedPourMethod));
            Line(T("直倒 / 引流体积", "Direct / Guided volume"), F(s.directPourVolumeMl) + " / " + F(s.guidedPourVolumeMl) + " ml");
            Line(T("入杯失气", "Entry CO₂ loss"), F(s.co2LostDuringPour) + " g");
            GUILayout.Space(8); GUILayout.Label(T("最近一次操作请求", "Last operation request"), heading);
            var request = session.Adapter.LastRequest;
            if (request == null) Note(T("尚无请求", "No request yet"));
            else
            {
                Line(T("意图", "Intended"), F(request.IntendedMl) + " " + request.AmountUnit);
                Line(T("请求", "Requested"), F(request.RequestedMl) + " " + request.AmountUnit);
                Line(T("实际", "Actual"), (request.ActualAmount.HasValue ? F(request.ActualAmount.Value) : T("待确认", "Pending")) + " " + request.AmountUnit);
            }
            Note(World.Model + " · " + F(session.Adapter.World.Time) + " s"); Note(v.id);
        }
        private static string Method(string method) => method switch
        {
            "None" => T("无碳酸注入", "None"), "Direct" => T("直倒", "Direct"), "BarspoonGuided" => T("吧勺引流", "Guided"), "Mixed" => T("混合方式", "Mixed"), _ => method
        };
        private static string Dimension(string id) => id switch
        {
            "abvPercent" => T("酒精度", "ABV %"), "sugarAcidRatio" => T("糖酸比", "Sugar-acid ratio"),
            "temperature" => T("温度 °C", "Temp °C"), "dilutionPercent" => T("稀释率 %", "Dilution %"),
            "co2Retention" => T("碳酸保留率", "CO₂ retention"), "glass" => T("杯型", "Glass"), _ => id
        };
        private static string Direction(string id) => id switch
        {
            "inside" => T("区间内", "Inside"), "low" => T("偏低", "Low"), "high" => T("偏高", "High"),
            "missing" => T("无数据", "Missing"), "wrong" => T("不匹配", "Wrong"), _ => id
        };
        private void Preview(Vessel v, VesselState s, bool full)
        {
            var loop = session.Orders; var order = loop.Current;
            string glass = containers[v.id] is Glass g ? (g.GlassType == GlassType.Coupe ? "coupe" : "highball") : order.glass;
            GUILayout.Label(order.name + T(" · 尝试 ", " · Attempt ") + loop.AttemptIndex, heading);
            Note(containers[v.id] is Glass ? T("当前杯型与快照；未执行出杯。", "Actual glass and snapshot; no Serve executed.") : T("预估转入目标杯型：", "Assumes transfer to required glass: ") + order.glass + T("；本容器不可直接出杯。", "; source cannot be served."));
            var evaluated = loop.Catalog.Evaluate(order, s, glass); var feedback = loop.Catalog.Displayed(evaluated);
            if (full)
            {
                for (int i = 0; i < evaluated.Length; i++)
                {
                    var d = evaluated[i]; var range = order.ranges[i];
                    bool top = !d.inside && feedback.Contains(d.feedbackId);
                    string target = d.dimension == "glass" ? order.glass : "[" + (range.hasMin ? F(range.min) : "−∞") + ", " + (range.hasMax ? F(range.max) : "+∞") + "]";
                    string value = d.dimension == "glass" ? glass : F(d.value);
                    GUILayout.Label((top ? "★ " : "") + Dimension(d.dimension) + ": " + value + " / " + target, top ? highlight : normal);
                    string severity = d.severity == "strong" ? T("明显", "Strong") : d.severity == "slight" ? T("轻微", "Slight") : T("无", "None");
                    Note("    " + T("越界偏差 ", "Over-margin ") + F(d.normalizedDeviation) + " · " + Direction(d.direction) + " / " + severity);
                }
            }
            else
            {
                var top = evaluated.Where(d => !d.inside && feedback.Contains(d.feedbackId)).Select(d => Dimension(d.dimension));
                Line(T("Top-2 反馈维度", "Top-2 feedback dimensions"), string.Join(" / ", top));
            }
            foreach (string id in feedback) GUILayout.Label(UiText.Translate(loop.Catalog.Text(id)), highlight);
            if (full) Note(T("越界偏差使用原 OrderCatalog 归一化值，不是新评分。", "Over-margin is the existing OrderCatalog normalized deviation, not a new score."));
        }
        private void Line(string label, string value) => GUILayout.Label(label + ":  " + value, normal);
    }
}
