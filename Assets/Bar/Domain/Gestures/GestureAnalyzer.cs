using System;
using Bar.Data;

namespace Bar.Domain
{
    /// <summary>从带时间的鼠标位置统计往复、圆周及下压输入。</summary>
    public sealed class GestureAnalyzer
    {
        private readonly Controls controls;
        private readonly CircleTracker circle;
        private double seconds, yExtreme, previousY;
        private int direction, reversals;
        private bool hasY;
        /// <summary>由合格换向次数和经过时间得到的往复强度，范围零到一。</summary>
        public double OscillationIntensity => seconds == 0 ? 0 : Math.Min(1, reversals / seconds / controls.MaxReversalsPerSecond);
        /// <summary>由累计圆周角度和经过时间得到的圈速强度，范围零到一。圆周角度绕轨迹的滑动圆心计（见 CircleTracker）。</summary>
        public double CircleIntensity => seconds == 0 ? 0 : Math.Min(1, circle.Radians / (2 * Math.PI) / seconds / controls.MaxCirclesPerSecond);
        /// <summary>创建GestureAnalyzer。</summary>
        /// <precondition>Controls 非空且所有流量、角度、时长与手势参数通过校验。</precondition>
        /// <effects>保存只读控制依赖；建立零采样历史。</effects>
        public GestureAnalyzer(Controls controls) { this.controls = controls ?? throw new ArgumentNullException(nameof(controls)); controls.Validate(); circle = new CircleTracker(controls.CircleMinRadius, controls.CircleCentreSeconds, controls.CircleSmoothingSeconds, controls.CircleSampleTravel); }
        /// <summary>累计一段鼠标位置与时间。</summary>
        /// <precondition>x、y 有限；dt 有限且非负；零 dt 不计移动。</precondition>
        /// <effects>增加经过时间；达到行程阈值才计往复换向；圆周角度交给 CircleTracker（绕滑动圆心、按行程采样）；不调用核心。</effects>
        public void Update(double x, double y, double dt)
        {
            Guard.Finite(x); Guard.Finite(y); Guard.Nonnegative(dt); if (dt == 0) return; seconds += dt;
            if (!hasY) { previousY = yExtreme = y; hasY = true; }
            else
            {
                if (direction == 0 && Math.Abs(y - previousY) >= controls.OscillationTravel) { direction = y > previousY ? 1 : -1; yExtreme = y; }
                else if (direction > 0) { yExtreme = Math.Max(yExtreme, y); if (yExtreme - y >= controls.OscillationTravel) { reversals++; direction = -1; yExtreme = y; } }
                else if (direction < 0) { yExtreme = Math.Min(yExtreme, y); if (y - yExtreme >= controls.OscillationTravel) { reversals++; direction = 1; yExtreme = y; } }
            }
            circle.Update(x, y, dt);
        }
        /// <summary>把向下行程换算为下压程度。</summary>
        /// <precondition>行程有限，可以为负。</precondition>
        /// <effects>返回限制在零到一之间的行程比，不消耗柠檬汁。</effects>
        public double Press(double downwardTravel) { Guard.Finite(downwardTravel); return Math.Max(0, Math.Min(1, downwardTravel / controls.PressTravel)); }
        /// <summary>清除本次手势历史。</summary>
        /// <precondition>可在任何采样阶段调用。</precondition>
        /// <effects>经过时间、极值、圈角和换向计数归零，清除首采样标记。</effects>
        public void Reset() { seconds = yExtreme = previousY = 0; direction = reversals = 0; hasY = false; circle.Reset(); }
    }
}
