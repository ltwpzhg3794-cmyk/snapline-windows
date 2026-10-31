using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Snapline
{
    enum InkTool { Pointer, Pen, Ellipse, Text }
    sealed class SelectionInk
    {
        internal InkTool Tool;
        internal Color Color;
        internal float Size;
        internal string Text;
        internal readonly List<PointF> Points = new List<PointF>();
        internal void Draw(Graphics graphics)
        {
            if (Points.Count == 0) return;
            using (var brush = new SolidBrush(Color))
            using (var pen = new Pen(Color, Size) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (Tool == InkTool.Text)
                {
                    using (var font = new Font("Microsoft YaHei UI", Size, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                        graphics.DrawString(Text, font, brush, Points[0], format);
                }
                else if (Tool == InkTool.Ellipse && Points.Count > 1)
                {
                    var a = Points[0]; var b = Points[Points.Count - 1];
                    float width = Math.Abs(b.X - a.X), height = Math.Abs(b.Y - a.Y);
                    if (width > 0 && height > 0) graphics.DrawEllipse(pen, Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), width,height);
                }
                else if (Points.Count > 1) graphics.DrawLines(pen, Points.ToArray());
                else graphics.FillEllipse(brush, Points[0].X - Size/2, Points[0].Y - Size/2, Size, Size);
            }
        }
        internal static Bitmap Render(Bitmap desktop, Rectangle selection, IEnumerable<SelectionInk> marks)
        {
            var result = desktop.Clone(selection, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var mark in marks) mark.Draw(graphics);
            }
            return result;
        }
    }
}
