using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace Snapline
{
    static class InlineCaptureTests
    {
        static void Require(bool value, string message) { if(!value) throw new Exception(message); }
        internal static int Run(string root, bool preview)
        {
            Directory.CreateDirectory(root);
            var report=new List<string>();
            foreach(var monitor in new[]{new Rectangle(0,0,1920,1080),new Rectangle(-1920,-1080,1920,1080),new Rectangle(0,0,800,600)})
            foreach(double dpi in new[]{1.0,1.25,1.5,2.0})
            foreach(var barSize in new[]{new Size(292,38),new Size(328,80)})
            foreach(var selection in new[]{monitor,new Rectangle(monitor.Left+5,monitor.Top+5,10,10),new Rectangle(monitor.Right-100,monitor.Bottom-100,80,80)})
            {
                var toolbar=RegionCapture.SelectionWindow.ToolbarBounds(selection,monitor,new Size((int)(barSize.Width*dpi),(int)(barSize.Height*dpi)),8);
                Require(monitor.Contains(toolbar),"Toolbar escaped screen bounds.");
            }
            report.Add("PASS: toolbar stays on screen at edges, negative origins, small selections and 100/125/150/200% DPI.");
            using(var original=new Bitmap(800,600,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(original)) g.Clear(Color.White);
                var region=new Rectangle(100,80,500,400);
                var marks=new List<SelectionInk>();
                using(var clean=SelectionInk.Render(original,region,marks))
                    Require(clean.Size==region.Size && clean.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"Unmarked crop changed pixels.");
                var pen=new SelectionInk { Tool=InkTool.Pen, Color=Color.Red, Size=6 };
                pen.Points.Add(new PointF(20,40)); pen.Points.Add(new PointF(180,40)); marks.Add(pen);
                var text=new SelectionInk { Tool=InkTool.Text, Color=Color.Blue, Size=28, Text="说明文字\n第二行" };
                text.Points.Add(new PointF(20,100)); marks.Add(text);
                var ellipse=new SelectionInk { Tool=InkTool.Ellipse, Color=Color.Green, Size=4 };
                ellipse.Points.Add(new PointF(450,350)); ellipse.Points.Add(new PointF(280,220)); marks.Add(ellipse);
                using(var result=SelectionInk.Render(original,region,marks))
                {
                    Require(result.Size==region.Size,"Export resolution changed.");
                    Require(result.GetPixel(70,40).G<20,"Pen coordinates/color incorrect.");
                    int blue=0, green=0;
                    for(int y=0;y<result.Height;y++) for(int x=0;x<result.Width;x++) { var p=result.GetPixel(x,y); if(p.B>180&&p.R<100)blue++; if(p.G>80&&p.R<50&&p.B<50)green++; }
                    Require(blue>100 && green>100,"Text or reverse-drag ellipse not rendered.");
                    Require(result.GetPixel(499,399).ToArgb()==Color.White.ToArgb(),"UI or unwanted content leaked into export.");
                    result.Save(Path.Combine(root,"inline-render.png"),ImageFormat.Png);
                }
                Require(original.GetPixel(170,120).ToArgb()==Color.White.ToArgb(),"Original snapshot mutated.");
            }
            report.Add("PASS: original-size crop, crop-local pen/text/ellipse coordinates, Chinese multiline text, independent colors and unmodified source pixels.");
            File.WriteAllLines(Path.Combine(root,"inline-results.txt"),report);
            if(preview)
            {
                var screen=Forms.Screen.PrimaryScreen.Bounds;
                using(var snapshot=new Bitmap(screen.Width,screen.Height))
                {
                    using(var g=Graphics.FromImage(snapshot))
                    using(var title=new Font("Microsoft YaHei UI",30))
                    using(var body=new Font("Microsoft YaHei UI",20))
                    {
                        g.Clear(Color.FromArgb(28,37,49));
                        g.FillRectangle(Brushes.WhiteSmoke,200,140,1100,650);
                        g.DrawString("截图标注 · 原地操作测试",title,Brushes.DarkSlateGray,260,200);
                        g.DrawString("框选后直接在选区内输入文字、自由圈画",body,Brushes.Gray,260,270);
                        g.FillRectangle(Brushes.LightBlue,300,370,300,220);
                        g.FillEllipse(Brushes.PaleGoldenrod,800,370,280,220);
                    }
                    using(var window=new RegionCapture.SelectionWindow(snapshot,screen))
                    {
                        window.ShowInTaskbar=true;
                        if(window.ShowDialog()==Forms.DialogResult.OK)
                            using(var result=window.RenderResult()) result.Save(Path.Combine(root,"inline-ui.png"),ImageFormat.Png);
                    }
                }
            }
            return 0;
        }
    }
}
