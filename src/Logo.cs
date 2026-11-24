using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Tunex
{
    // Логотип: спидометр, стрелка в «зоне производительности», внизу молния — разгон и FPS.
    // Используется и в заголовке окна, и для генерации app.ico (tools\make-icon.cmd).
    static class Logo
    {
        public static Color Accent = Color.FromArgb(198, 240, 92);
        public static Color AccentDeep = Color.FromArgb(118, 156, 40);

        const float StartAngle = 145f, Sweep = 250f;   // шкала: от левого низа через верх к правому низу
        const float Value = 0.78f;                      // куда указывает стрелка (0..1)

        public static void Draw(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = r.Width;
            bool tiny = s < 24, small = s < 40;

            // Плитка-фон
            using (GraphicsPath bg = RoundRect(r, s * 0.24f))
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                    Color.FromArgb(36, 36, 33), Color.FromArgb(14, 14, 13), 90f))
                    g.FillPath(lb, bg);
                if (!small)
                    using (Pen border = new Pen(Color.FromArgb(30, 255, 255, 255), Math.Max(1f, s / 64f)))
                        g.DrawPath(border, bg);
            }

            PointF c = new PointF(r.X + s * 0.5f, r.Y + s * 0.54f);
            float rad = s * (tiny ? 0.34f : 0.31f);
            float w = s * (tiny ? 0.13f : 0.085f);
            RectangleF arcBox = new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2);

            // Пустая шкала
            using (Pen track = new Pen(Color.FromArgb(tiny ? 70 : 48, 255, 255, 255), w))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                g.DrawArc(track, arcBox, StartAngle, Sweep);
            }

            // Заполненная часть шкалы с переходом цвета
            float fill = Sweep * Value;
            using (LinearGradientBrush gb = new LinearGradientBrush(new RectangleF(arcBox.X - w, arcBox.Y - w, arcBox.Width + 2 * w, arcBox.Height + 2 * w), AccentDeep, Accent, 0f))
            using (Pen p = new Pen(gb, w))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawArc(p, arcBox, StartAngle, fill);
            }

            // Риски шкалы
            if (!small)
            {
                using (Pen tick = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, s * 0.018f)))
                {
                    tick.StartCap = tick.EndCap = LineCap.Round;
                    for (int i = 0; i <= 10; i++)
                    {
                        double a = (StartAngle + Sweep * i / 10f) * Math.PI / 180.0;
                        float r1 = rad - w * 1.15f, r2 = rad - w * (i % 5 == 0 ? 2.0f : 1.6f);
                        g.DrawLine(tick, c.X + (float)Math.Cos(a) * r1, c.Y + (float)Math.Sin(a) * r1,
                                         c.X + (float)Math.Cos(a) * r2, c.Y + (float)Math.Sin(a) * r2);
                    }
                }
            }

            // Стрелка
            double na = (StartAngle + fill) * Math.PI / 180.0;
            float len = rad * (small ? 0.78f : 0.82f), baseW = s * (tiny ? 0.07f : 0.045f);
            PointF tip = new PointF(c.X + (float)Math.Cos(na) * len, c.Y + (float)Math.Sin(na) * len);
            double pa = na + Math.PI / 2;
            PointF b1 = new PointF(c.X + (float)Math.Cos(pa) * baseW, c.Y + (float)Math.Sin(pa) * baseW);
            PointF b2 = new PointF(c.X - (float)Math.Cos(pa) * baseW, c.Y - (float)Math.Sin(pa) * baseW);
            PointF back = new PointF(c.X - (float)Math.Cos(na) * baseW * 1.6f, c.Y - (float)Math.Sin(na) * baseW * 1.6f);
            using (SolidBrush nb = new SolidBrush(Color.FromArgb(240, 242, 244)))
            {
                g.FillPolygon(nb, new PointF[] { tip, b1, back, b2 });
                float hub = s * (tiny ? 0.16f : 0.12f);
                g.FillEllipse(nb, c.X - hub / 2, c.Y - hub / 2, hub, hub);
            }
            if (!tiny)
            {
                float dot = s * 0.05f;
                using (SolidBrush db = new SolidBrush(Color.FromArgb(18, 20, 23))) g.FillEllipse(db, c.X - dot / 2, c.Y - dot / 2, dot, dot);
            }

            // Молния в просвете шкалы
            if (!tiny)
            {
                float bh = s * 0.2f, bw = s * 0.13f;
                float bx = c.X - bw / 2, by = c.Y + rad * 0.62f;
                PointF[] bolt =
                {
                    new PointF(bx + bw * 0.62f, by),
                    new PointF(bx + bw * 0.08f, by + bh * 0.56f),
                    new PointF(bx + bw * 0.46f, by + bh * 0.56f),
                    new PointF(bx + bw * 0.34f, by + bh),
                    new PointF(bx + bw * 0.92f, by + bh * 0.40f),
                    new PointF(bx + bw * 0.54f, by + bh * 0.40f)
                };
                using (SolidBrush bb = new SolidBrush(Accent)) g.FillPolygon(bb, bolt);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
