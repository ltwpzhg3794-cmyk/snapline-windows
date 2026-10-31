using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    // The result belongs to this capture session; no clipboard-source guessing is needed.
    static class RegionCapture
    {
        internal static BitmapSource Select()
        {
            var desktop = Forms.SystemInformation.VirtualScreen;
            if (desktop.Width <= 0 || desktop.Height <= 0 || (long)desktop.Width * desktop.Height > 50000000)
                throw new InvalidOperationException("屏幕总尺寸过大，无法开始框选截图。");
            using (var pixels = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(pixels))
                    graphics.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size, CopyPixelOperation.SourceCopy);
                using (var selection = new SelectionWindow(pixels, desktop))
                {
                    if (selection.ShowDialog() != Forms.DialogResult.OK) return null;
                    using (var cropped = selection.RenderResult())
                    using (var stream = new MemoryStream())
                    {
                        cropped.Save(stream, ImageFormat.Png);
                        stream.Position = 0;
                        var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        image.Freeze();
                        return image;
                    }
                }
            }
        }
        internal sealed class SelectionWindow : Forms.Form
        {
            readonly Bitmap pixels;
            readonly Rectangle desktop;
            readonly Font hintFont;
            readonly List<SelectionInk> marks = new List<SelectionInk>();
            readonly CaptureBar toolbar = new CaptureBar();
            readonly CaptureBar options = new CaptureBar();
            readonly Forms.ToolTip tooltips = new Forms.ToolTip { InitialDelay = 350, ReshowDelay = 100 };
            readonly Forms.TextBox input = new Forms.TextBox();
            readonly List<CaptureIcon> tools = new List<CaptureIcon>();
            readonly List<CaptureIcon> colors = new List<CaptureIcon>();
            readonly float scale;
            readonly List<CaptureIcon> sizes = new List<CaptureIcon>();
            Font inputFont;
            InkTool tool = InkTool.Pointer;
            Color ink = Color.FromArgb(255, 90, 110);
            SelectionInk stroke;
            PointF textOrigin;
            int sizeIndex = 1;
            bool ready;
            Point anchor;
            bool selecting;
            internal Rectangle Selection;
            internal SelectionWindow(Bitmap snapshot, Rectangle bounds)
            {
                pixels = snapshot; desktop = bounds;
                Text = "截图晾台 · 框选截图";
                FormBorderStyle = Forms.FormBorderStyle.None;
                AutoScaleMode = Forms.AutoScaleMode.None;
                StartPosition = Forms.FormStartPosition.Manual;
                Bounds = bounds;
                ShowInTaskbar = false; TopMost = true;
                DoubleBuffered = true; KeyPreview = true;
                Cursor = Forms.Cursors.Cross;
                Native.Point pointer; Native.GetCursorPos(out pointer);

                scale = 1;
                try { scale = Math.Max(1, Native.GetDpiForWindow(Handle) / 96f); } catch (EntryPointNotFoundException) { }
                hintFont = new Font("Microsoft YaHei UI", Math.Max(16, 16 * scale), FontStyle.Regular, GraphicsUnit.Pixel);
                MakeToolbar();
                input.Visible = false; input.Multiline = true; input.AcceptsReturn = true; input.WordWrap = false;
                input.BorderStyle = Forms.BorderStyle.None; input.BackColor = Color.White;
                input.ForeColor = ink; input.ImeMode = Forms.ImeMode.On;
                input.AccessibleName = "选区文字说明";
                Controls.Add(input);
                input.TextChanged += delegate { ResizeInput(); };
                Shown += delegate
                {
                    // Use physical desktop pixels, including monitors with negative origins.
                    Native.SetWindowPos(Handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0040);
                    Activate(); Focus();
                };
            }
            int S(int value) { return (int)Math.Round(value * scale); }
            float MarkSize { get { return (tool == InkTool.Text ? new[] { 18f, 26f, 36f } : new[] { 2f, 4f, 7f })[sizeIndex] * scale; } }
            CaptureIcon AddIcon(CaptureBar bar, string glyph, string tip, int x, int width, Action action)
            {
                var button = new CaptureIcon { Glyph = glyph, AccessibleName = tip,
                    Bounds = new Rectangle(S(x),S(4),S(width),S(30)) };
                tooltips.SetToolTip(button,tip); button.Click += delegate { action(); };
                bar.Controls.Add(button); return button;
            }
            void MakeToolbar()
            {
                toolbar.Size=new Size(S(292),S(38)); toolbar.Snapshot=pixels;
                toolbar.Visible=false; toolbar.Cursor=Forms.Cursors.Default; Controls.Add(toolbar);
                options.Size=new Size(S(328),S(36)); options.Snapshot=pixels;
                options.Visible=false; options.Cursor=Forms.Cursors.Default; Controls.Add(options);
                string[] names={"选择 / 双击完成","画笔","椭圆圈选","文字"};
                string[] glyphs={"pointer","pen","ellipse","text"};
                for(int i=0;i<4;i++)
                {
                    InkTool mode=(InkTool)i;
                    tools.Add(AddIcon(toolbar,glyphs[i],names[i],6+i*34,32,delegate { CommitText(); EndStroke(); tool=mode; UpdateTools(); Focus(); }));
                }
                AddIcon(toolbar,"undo","撤销 · Ctrl+Z",150,32,delegate { CommitText(); EndStroke(); if(marks.Count>0)marks.RemoveAt(marks.Count-1); Invalidate(); });
                AddIcon(toolbar,"reset","重新框选",184,32,delegate { ResetSelection(); });
                AddIcon(toolbar,"cancel","取消 · Esc",224,28,delegate { DialogResult=Forms.DialogResult.Cancel; });
                AddIcon(toolbar,"done","完成 · Ctrl+Enter",256,30,delegate { Finish(); });
                for(int i=0;i<3;i++)
                {
                    int choice=i;
                    sizes.Add(AddIcon(options,new[]{"small","medium","large"}[i],new[]{"细笔 / 小字","中笔 / 中字","粗笔 / 大字"}[i],5+i*25,24,delegate { sizeIndex=choice; UpdateTools(); }));
                }
                Color[] palette={Color.FromArgb(255,90,110),Color.FromArgb(255,190,55),Color.FromArgb(255,235,80),Color.FromArgb(91,220,151),Color.FromArgb(83,190,255),Color.FromArgb(185,130,255),Color.White,Color.Black};
                string[] namesOfColors={"红色","橙色","黄色","绿色","蓝色","紫色","白色","黑色"};
                for(int i=0;i<palette.Length;i++)
                {
                    Color value=palette[i];
                    var swatch=AddIcon(options,"",namesOfColors[i],87+i*25,24,delegate { ink=value; input.ForeColor=ink; UpdateTools(); });
                    swatch.Swatch=value; colors.Add(swatch);
                }
                AddIcon(options,"palette","自选颜色",294,28,delegate { using(var picker=new Forms.ColorDialog { Color=ink,FullOpen=true })
                    if(picker.ShowDialog(this)==Forms.DialogResult.OK) { ink=picker.Color; input.ForeColor=ink; UpdateTools(); } });
                UpdateTools();
            }
            void UpdateTools()
            {
                for(int i=0;i<tools.Count;i++) { tools[i].Selected=(int)tool==i; tools[i].Invalidate(); }
                for(int i=0;i<sizes.Count;i++) { sizes[i].Selected=sizeIndex==i; sizes[i].Invalidate(); }
                foreach(var button in colors) { button.Selected=button.Swatch.Value.ToArgb()==ink.ToArgb(); button.Invalidate(); }
                if(ready)PlaceToolbar();
                if(input.Visible) { SetInputFont(); ResizeInput(); input.Focus(); }
            }
            void SetInputFont()
            {
                var previous=inputFont;
                inputFont=new Font("Microsoft YaHei UI",MarkSize,FontStyle.Regular,GraphicsUnit.Pixel);
                input.Font=inputFont;
                if(previous!=null) previous.Dispose();
            }
            internal static Rectangle ToolbarBounds(Rectangle selection, Rectangle monitor, Size size, int gap)
            {
                int x=Math.Max(monitor.Left,Math.Min(selection.Right-size.Width,monitor.Right-size.Width));
                int y=selection.Bottom+gap;
                if(y+size.Height>monitor.Bottom) y=selection.Top-size.Height-gap;
                if(y<monitor.Top) y=Math.Max(monitor.Top,monitor.Bottom-size.Height-gap);
                return new Rectangle(x,y,Math.Min(size.Width,monitor.Width),Math.Min(size.Height,monitor.Height));
            }
            void PlaceToolbar()
            {
                var screen=Forms.Screen.FromPoint(new Point(desktop.Left+Selection.Right-1,desktop.Top+Selection.Bottom-1));
                var area=screen.Bounds; area.Offset(-desktop.Left,-desktop.Top);
                                bool showOptions=tool!=InkTool.Pointer;
                var group=ToolbarBounds(Selection,area,new Size(S(showOptions?328:292),S(showOptions?80:38)),S(8));
                toolbar.Bounds=new Rectangle(group.Right-S(292),group.Top,S(292),S(38));
                options.Bounds=new Rectangle(group.Right-S(328),group.Top+S(42),S(328),S(36));
                options.Visible=showOptions;
                if(showOptions)options.BringToFront();
                toolbar.Visible=true; toolbar.BringToFront();
            }
            PointF LocalPoint(Point p) { return new PointF(Math.Max(0,Math.Min(Selection.Width-1,p.X-Selection.Left)),Math.Max(0,Math.Min(Selection.Height-1,p.Y-Selection.Top))); }
            void EndStroke() { stroke=null; Capture=false; }
            void ResetSelection()
            {
                input.Visible=false; input.Clear(); EndStroke(); marks.Clear(); ready=false;
                Selection=Rectangle.Empty; toolbar.Visible=false; options.Visible=false; selecting=false; Focus(); Invalidate();
            }
            void ResizeInput()
            {
                if(!input.Visible) return;
                int maxWidth=Math.Max(2,Selection.Right-input.Left), maxHeight=Math.Max(2,Selection.Bottom-input.Top);
                Size measured=Forms.TextRenderer.MeasureText(input.Text+"  ",input.Font,new Size(Int32.MaxValue,Int32.MaxValue),Forms.TextFormatFlags.NoPadding);
                input.Size=new Size(Math.Min(maxWidth,Math.Max(S(180),measured.Width+S(12))),Math.Min(maxHeight,Math.Max(S(40),measured.Height+S(12))));
            }
            void StartText(Point point)
            {
                CommitText();
                point.X=Math.Min(point.X,Selection.Right-Math.Min(Selection.Width,S(180)));
                point.Y=Math.Min(point.Y,Selection.Bottom-Math.Min(Selection.Height,S(48)));
                textOrigin=LocalPoint(point);
                input.Location=new Point(Selection.Left+(int)textOrigin.X,Selection.Top+(int)textOrigin.Y);
                input.Text=""; input.ForeColor=ink;
                input.BackColor=pixels.GetPixel(Math.Max(0,Math.Min(pixels.Width-1,point.X)),Math.Max(0,Math.Min(pixels.Height-1,point.Y)));
                SetInputFont();
                input.Visible=true; ResizeInput(); input.BringToFront(); input.Focus();
            }
            void CommitText()
            {
                if(!input.Visible) return;
                if(!String.IsNullOrWhiteSpace(input.Text))
                {
                    var mark=new SelectionInk { Tool=InkTool.Text,Color=input.ForeColor,Size=input.Font.Size,Text=input.Text };
                    mark.Points.Add(textOrigin); marks.Add(mark);
                }
                input.Visible=false; input.Clear(); Invalidate();
            }
            void Finish() { if(!ready) return; CommitText(); EndStroke(); DialogResult=Forms.DialogResult.OK; }
            internal Bitmap RenderResult() { return SelectionInk.Render(pixels,Selection,marks); }
            Point Pointer()
            {
                Native.Point pointer; Native.GetCursorPos(out pointer);
                return new Point(Math.Max(0, Math.Min(desktop.Width, pointer.X - desktop.Left)),
                    Math.Max(0, Math.Min(desktop.Height, pointer.Y - desktop.Top)));
            }
            Point EventPoint(Forms.MouseEventArgs e)
            {
                return new Point(Math.Max(0, Math.Min(desktop.Width, e.X)), Math.Max(0, Math.Min(desktop.Height, e.Y)));
            }
            protected override void OnMouseDown(Forms.MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == Forms.MouseButtons.Right) { DialogResult = Forms.DialogResult.Cancel; return; }
                if (e.Button != Forms.MouseButtons.Left) return;
                if(ready)
                {
                    if(!Selection.Contains(e.Location)) { CommitText(); Focus(); return; }
                    if(tool==InkTool.Text) { StartText(e.Location); return; }
                    CommitText(); Focus();
                    if(tool==InkTool.Pen || tool==InkTool.Ellipse)
                    {
                        stroke=new SelectionInk { Tool=tool,Color=ink,Size=MarkSize };
                        stroke.Points.Add(LocalPoint(e.Location)); marks.Add(stroke); Capture=true; Invalidate();
                    }
                    return;
                }
                anchor = EventPoint(e); selecting = true; Capture = true;
                Selection = Rectangle.Empty; Invalidate();
            }
            protected override void OnMouseMove(Forms.MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if(stroke!=null)
                {
                    var point=LocalPoint(e.Location);
                    if(stroke.Tool==InkTool.Ellipse && stroke.Points.Count>1) stroke.Points[1]=point;
                    else stroke.Points.Add(point);
                    Invalidate(); return;
                }
                if (!selecting) return;
                var end = EventPoint(e);
                Selection = Rectangle.FromLTRB(Math.Min(anchor.X, end.X), Math.Min(anchor.Y, end.Y),
                    Math.Max(anchor.X, end.X), Math.Max(anchor.Y, end.Y));
                Invalidate();
            }
            protected override void OnMouseUp(Forms.MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if(stroke!=null && e.Button==Forms.MouseButtons.Left) { OnMouseMove(e); EndStroke(); Invalidate(); return; }
                if (!selecting || e.Button != Forms.MouseButtons.Left) return;
                OnMouseMove(e); selecting = false; Capture = false;
                if (Selection.Width < 2 || Selection.Height < 2) { Selection = Rectangle.Empty; Invalidate(); return; }
                ready=true; PlaceToolbar(); Invalidate();
            }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture) stroke=null; }
            protected override void OnMouseDoubleClick(Forms.MouseEventArgs e)
            { base.OnMouseDoubleClick(e); if(ready && tool==InkTool.Pointer && Selection.Contains(e.Location)) Finish(); }
            protected override bool ProcessCmdKey(ref Forms.Message message, Forms.Keys key)
            {
                if (key == Forms.Keys.Escape) { DialogResult = Forms.DialogResult.Cancel; return true; }
                if(ready && (key==(Forms.Keys.Control|Forms.Keys.Enter) || (key==Forms.Keys.Enter && !input.Focused))) { Finish(); return true; }
                if(ready && key==(Forms.Keys.Control|Forms.Keys.Z) && !input.Focused) { EndStroke(); if(marks.Count>0) marks.RemoveAt(marks.Count-1); Invalidate(); return true; }
                return base.ProcessCmdKey(ref message, key);
            }
            protected override void OnPaint(Forms.PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(pixels, Point.Empty);
                using (var shade = new SolidBrush(Color.FromArgb(105, 0, 0, 0))) e.Graphics.FillRectangle(shade, ClientRectangle);
                if (Selection.Width > 0 && Selection.Height > 0)
                {
                    e.Graphics.DrawImage(pixels, Selection, Selection, GraphicsUnit.Pixel);
                    var saved=e.Graphics.Save();
                    e.Graphics.SetClip(Selection); e.Graphics.TranslateTransform(Selection.Left,Selection.Top);
                    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                    foreach(var mark in marks) mark.Draw(e.Graphics);
                    e.Graphics.Restore(saved);
                    using (var border = new Pen(Color.FromArgb(95, 220, 164), 1))
                        e.Graphics.DrawRectangle(border, Selection.X, Selection.Y, Selection.Width - 1, Selection.Height - 1);
                }
                if(ready) return;
                var pointer = Pointer();
                string hint = selecting ? Selection.Width + " × " + Selection.Height + "   松开后可标注 · Esc 取消" : "拖动选择截图区域 · Esc / 右键取消";
                SizeF size = e.Graphics.MeasureString(hint, hintFont);
                int width = (int)Math.Ceiling(size.Width) + 24, height = (int)Math.Ceiling(size.Height) + 18;
                int x = Math.Max(8, Math.Min(desktop.Width - width - 8, pointer.X + 18));
                int y = Math.Max(8, Math.Min(desktop.Height - height - 8, pointer.Y + 22));
                using (var background = new SolidBrush(Color.FromArgb(230, 25, 42, 57))) e.Graphics.FillRectangle(background, x, y, width, height);
                e.Graphics.DrawString(hint, hintFont, Brushes.White, x + 12, y + 9);
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { tooltips.Dispose(); hintFont.Dispose(); if(inputFont!=null) inputFont.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
