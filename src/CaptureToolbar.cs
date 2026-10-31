using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Snapline
{
    sealed class CaptureBar : Forms.Panel
    {
        internal Bitmap Snapshot;
        internal CaptureBar() { DoubleBuffered=true; }
        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path=new GraphicsPath(); float d=radius*2;
            path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if(Width<2||Height<2) return;
            using(var path=Rounded(new RectangleF(0,0,Width,Height),Math.Min(8,Height/4)))
            { var old=Region; Region=new Region(path); if(old!=null)old.Dispose(); }
        }
        protected override void OnPaintBackground(Forms.PaintEventArgs e)
        {
            if(Snapshot!=null) e.Graphics.DrawImage(Snapshot,ClientRectangle,Bounds,GraphicsUnit.Pixel);
            using(var brush=new SolidBrush(Color.FromArgb(238,48,50,56))) e.Graphics.FillRectangle(brush,ClientRectangle);
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Rounded(new RectangleF(0.5f,0.5f,Width-1,Height-1),7))
            using(var pen=new Pen(Color.FromArgb(70,160,165,175))) e.Graphics.DrawPath(pen,path);
        }
    }
    sealed class CaptureIcon : Forms.Button
    {
        internal string Glyph;
        internal bool Selected;
        internal Color? Swatch { get; set; }
        bool hovered;
        internal CaptureIcon()
        {
            SetStyle(Forms.ControlStyles.UserPaint|Forms.ControlStyles.AllPaintingInWmPaint|Forms.ControlStyles.OptimizedDoubleBuffer|Forms.ControlStyles.SupportsTransparentBackColor,true);
            FlatStyle=Forms.FlatStyle.Flat; FlatAppearance.BorderSize=0;
            BackColor=Color.Transparent; TabStop=false; Cursor=Forms.Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            var g=e.Graphics;
            g.Clear(Color.FromArgb(48,50,56));
            var bar=Parent as CaptureBar;
            if(bar!=null && bar.Snapshot!=null)
            {
                g.DrawImage(bar.Snapshot,ClientRectangle,new Rectangle(bar.Left+Left,bar.Top+Top,Width,Height),GraphicsUnit.Pixel);
                using(var shade=new SolidBrush(Color.FromArgb(238,48,50,56)))g.FillRectangle(shade,ClientRectangle);
            }
            g.SmoothingMode=SmoothingMode.AntiAlias;
            if(hovered||Selected)
            using(var path=CaptureBar.Rounded(new RectangleF(2,2,Width-4,Height-4),4))
            using(var brush=new SolidBrush(Selected?Color.FromArgb(65,85,211,157):Color.FromArgb(26,255,255,255)))g.FillPath(brush,path);
            float factor=Math.Min(Width,Height)/32f;
            var state=g.Save(); g.TranslateTransform(Width/2f,Height/2f); g.ScaleTransform(factor,factor);
            Color color=Glyph=="done"?Color.FromArgb(90,224,167):Glyph=="cancel"?Color.FromArgb(249,118,132):Selected?Color.FromArgb(91,225,169):Color.FromArgb(224,226,232);
            using(var pen=new Pen(color,1.65f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            using(var brush=new SolidBrush(color))
            {
                if(Swatch.HasValue)
                {
                    using(var fill=new SolidBrush(Swatch.Value))g.FillRectangle(fill,-7,-7,14,14);
                    using(var outline=new Pen(Selected?Color.White:Color.FromArgb(70,200,200,200),Selected?1.6f:0.7f))g.DrawRectangle(outline,-8,-8,16,16);
                }
                else switch(Glyph)
                {
                    case "pointer": pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,-7,-6,14,12);break;
                    case "pen": g.DrawPolygon(pen,new[]{new PointF(-7,7),new PointF(-5,1),new PointF(4,-8),new PointF(8,-4),new PointF(-1,5)});g.DrawLine(pen,2,-6,6,-2);g.DrawLine(pen,-7,7,-1,5);break;
                    case "ellipse":g.DrawEllipse(pen,-8,-6,16,12);break;
                    case "text":g.DrawLine(pen,-7,-7,7,-7);g.DrawLine(pen,0,-7,0,8);g.DrawLine(pen,-3,8,3,8);break;
                    case "undo":g.DrawArc(pen,-6,-3,14,11,230,230);g.DrawLines(pen,new[]{new PointF(-2,-8),new PointF(-7,-3),new PointF(-1,-1)});break;
                    case "reset":g.DrawArc(pen,-7,-7,14,14,40,280);g.DrawLines(pen,new[]{new PointF(2,-8),new PointF(8,-6),new PointF(7,0)});break;
                    case "cancel":g.DrawLine(pen,-5,-5,5,5);g.DrawLine(pen,5,-5,-5,5);break;
                    case "done":g.DrawLines(pen,new[]{new PointF(-7,0),new PointF(-2,5),new PointF(7,-6)});break;
                    case "palette":
                        Color[] shades={Color.FromArgb(255,108,131),Color.FromArgb(255,220,96),Color.FromArgb(99,211,160),Color.FromArgb(111,188,255)};
                        for(int i=0;i<4;i++)using(var fill=new SolidBrush(shades[i]))g.FillEllipse(fill,i%2==0?-7:1,i<2?-7:1,6,6);break;
                    case "small":g.FillEllipse(brush,-1.6f,-1.6f,3.2f,3.2f);break;
                    case "medium":g.FillEllipse(brush,-2.7f,-2.7f,5.4f,5.4f);break;
                    case "large":g.FillEllipse(brush,-4,-4,8,8);break;
                }
            }
            g.Restore(state);
        }
    }
}
