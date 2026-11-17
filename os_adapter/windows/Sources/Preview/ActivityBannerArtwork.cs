// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ComputerUse.Windows
{
    internal static class ActivityBannerArtwork
    {
        const string Status = " 正在使用你的电脑…";
        internal const float ShadowMargin = 10;
        const float Height = 46, Padding = 15, Radius = 12;
        const float Dot = 8, DotGap = 10, DividerGap = 14, KeyGap = 6, KeyWidth = 34, KeyHeight = 23;

        internal static Bitmap Render(string name, float scale, float screenWidth, bool canStop)
        {
            using (var title = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var shortcut = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            using (var measureImage = new Bitmap(1, 1))
            using (var measure = Graphics.FromImage(measureImage))
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                format.LineAlignment = StringAlignment.Center;
                measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float statusWidth = Width(measure, Status, title, format);
                float pressWidth = Width(measure, "按", shortcut, format);
                float stopWidth = Width(measure, "停止", shortcut, format);
                float shortcutWidth = canStop ? DividerGap * 2 + 1 + pressWidth + KeyGap * 2 + KeyWidth + stopWidth : 0;
                float fixedWidth = Padding * 2 + Dot + DotGap + statusWidth + shortcutWidth;
                float nameWidth = Math.Max(0, Math.Min(Width(measure, name, title, format),
                    Math.Min(560, screenWidth - ShadowMargin * 2) - fixedWidth));
                var size = new Size((int)Math.Ceiling((fixedWidth + nameWidth + ShadowMargin * 2) * scale),
                    (int)Math.Ceiling((Height + ShadowMargin * 2) * scale));
                var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                using (var dot = new SolidBrush(Color.FromArgb(141, 183, 231)))
                using (var text = new SolidBrush(Color.FromArgb(244, 247, 251)))
                using (var secondary = new SolidBrush(Color.FromArgb(229, 234, 240)))
                {
                    graphics.ScaleTransform(scale, scale);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var frame = new RectangleF(ShadowMargin, ShadowMargin, fixedWidth + nameWidth, Height);
                    DrawFrame(graphics, frame);
                    float x = frame.Left + Padding;
                    graphics.FillEllipse(dot, x, frame.Top + (Height - Dot) / 2, Dot, Dot);
                    x += Dot + DotGap;
                    if (nameWidth > 0)
                    {
                        using (var nameFormat = new StringFormat(format) { Trimming = StringTrimming.EllipsisCharacter })
                            graphics.DrawString(name, title, text, new RectangleF(x, frame.Top, nameWidth, Height), nameFormat);
                    }
                    x += nameWidth;
                    graphics.DrawString(Status, title, text, new RectangleF(x, frame.Top, statusWidth, Height), format);
                    if (canStop)
                        DrawShortcut(graphics, frame.Top, x + statusWidth + DividerGap, shortcut, secondary, format, pressWidth, stopWidth);
                }
                return bitmap;
            }
        }

        static float Width(Graphics graphics, string text, Font font, StringFormat format)
        {
            return (float)Math.Ceiling(graphics.MeasureString(text, font, int.MaxValue, format).Width) + 1;
        }

        static void DrawFrame(Graphics graphics, RectangleF frame)
        {
            for (int spread = 6; spread > 0; spread--)
            {
                var shadow = frame;
                shadow.Offset(0, 3);
                shadow.Inflate(spread, spread);
                using (var path = Rounded(shadow, Radius + spread))
                using (var brush = new SolidBrush(Color.FromArgb(8 + (6 - spread) * 2, 9, 16, 29)))
                    graphics.FillPath(brush, path);
            }
            using (var path = Rounded(frame, Radius))
            using (var face = new SolidBrush(Color.FromArgb(250, 32, 39, 49)))
            using (var border = new Pen(Color.FromArgb(150, 97, 110, 127)))
            {
                graphics.FillPath(face, path);
                graphics.DrawPath(border, path);
            }
        }

        static void DrawShortcut(Graphics graphics, float y, float x, Font font, Brush text,
            StringFormat format, float pressWidth, float stopWidth)
        {
            using (var divider = new Pen(Color.FromArgb(105, 147, 159, 175)))
                graphics.DrawLine(divider, x, y + 13, x, y + Height - 13);
            x += 1 + DividerGap;
            graphics.DrawString("按", font, text, new RectangleF(x, y, pressWidth, Height), format);
            x += pressWidth + KeyGap;
            var key = new RectangleF(x, y + (Height - KeyHeight) / 2, KeyWidth, KeyHeight);
            using (var path = Rounded(key, 5))
            using (var face = new SolidBrush(Color.FromArgb(38, 46, 57)))
            using (var border = new Pen(Color.FromArgb(107, 122, 140)))
            using (var center = new StringFormat(format) { Alignment = StringAlignment.Center })
            {
                graphics.FillPath(face, path);
                graphics.DrawPath(border, path);
                graphics.DrawString("Esc", font, text, key, center);
            }
            x += KeyWidth + KeyGap;
            graphics.DrawString("停止", font, text, new RectangleF(x, y, stopWidth, Height), format);
        }

        static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
