using System;

namespace Bar.Domain
{
    /// <summary>
    /// 跟踪鼠标画圈的转角。玩家按下左键后才开始画圈，按下的那一点在圆周上而不是圆心；
    /// 旧写法绕按下点量角度，一整圈只记到约一半，且贴近起点的采样被丢弃。现在先把轨迹平滑，鼠标每移动满
    /// <c>sampleTravel</c> 才采样一次，圆心取轨迹的指数滑动平均（<c>centreSeconds</c>）。只要估计的圆心落在圈内，
    /// 每画一圈转角正好累计 2π，与起点、圈的位置和缓慢漂移无关；原地手抖不足最小半径，不计；直线来回扫过圆心的跳变超过
    /// <c>maxStep</c>，不计。纯 C#，领域测量与画面共用同一套规则。
    /// </summary>
    public sealed class CircleTracker
    {
        private readonly double minRadius, centreSeconds, smoothingSeconds, sampleTravel, maxStep;
        private double sx, sy, cx, cy, lx, ly, angle;
        private bool started, sampled, hasAngle;
        /// <summary>本次 Update 新增的有符号转角（弧度，鼠标坐标中逆时针为正）；未采样时为零。</summary>
        public double LastDelta { get; private set; }
        /// <summary>开始以来的有符号累计转角。</summary>
        public double SignedRadians { get; private set; }
        /// <summary>开始以来的无符号累计转角。</summary>
        public double Radians { get; private set; }
        /// <summary>平滑后的鼠标点到估计圆心的距离。</summary>
        public double Radius { get; private set; }
        /// <summary>创建跟踪器。</summary>
        /// <precondition>所有参数为有限正数。</precondition>
        /// <effects>只保存参数。</effects>
        public CircleTracker(double minRadius, double centreSeconds, double smoothingSeconds, double sampleTravel, double maxStep = 1.5)
        { this.minRadius = minRadius; this.centreSeconds = centreSeconds; this.smoothingSeconds = smoothingSeconds; this.sampleTravel = sampleTravel; this.maxStep = maxStep; }
        /// <summary>累计一段鼠标位置。</summary>
        /// <precondition>x、y 有限，dt 非负；零 dt 不计。</precondition>
        /// <effects>更新平滑点、圆心和累计转角；不调用核心。</effects>
        public void Update(double x, double y, double dt)
        {
            LastDelta = 0; if (dt <= 0) return;
            if (!started) { sx = cx = lx = x; sy = cy = ly = y; started = true; return; }
            double k = 1 - Math.Exp(-dt / smoothingSeconds); sx += (x - sx) * k; sy += (y - sy) * k;
            double c = 1 - Math.Exp(-dt / centreSeconds); cx += (sx - cx) * c; cy += (sy - cy) * c;
            if (sampled && (sx - lx) * (sx - lx) + (sy - ly) * (sy - ly) < sampleTravel * sampleTravel) return;
            lx = sx; ly = sy; sampled = true;
            double dx = sx - cx, dy = sy - cy; Radius = Math.Sqrt(dx * dx + dy * dy);
            if (Radius < minRadius) { hasAngle = false; return; }
            double next = Math.Atan2(dy, dx);
            if (hasAngle)
            {
                double delta = next - angle; if (delta > Math.PI) delta -= 2 * Math.PI; if (delta < -Math.PI) delta += 2 * Math.PI;
                if (Math.Abs(delta) <= maxStep) { LastDelta = delta; SignedRadians += delta; Radians += Math.Abs(delta); }
            }
            angle = next; hasAngle = true;
        }
        /// <summary>清除历史。</summary>
        /// <precondition>任何时候可调用。</precondition>
        /// <effects>回到未开始状态，累计转角归零。</effects>
        public void Reset() { started = sampled = hasAngle = false; LastDelta = SignedRadians = Radians = Radius = 0; }
    }
}
