using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using SystemFonts = System.Windows.SystemFonts;
using FlowDirection = System.Windows.FlowDirection;

namespace Desktop.Views.Windows.DanMuCanvas
{
    /// <summary>
    /// 单元素描边文本：用FormattedText.BuildGeometry一次性生成字形轮廓，填充+描边在同一个元素里完成。
    /// 替代原"4个阴影TextBlock+1个正文TextBlock"的5元素方案，弹幕洪峰时视觉树节点数减少80%，
    /// 且描边是几何轮廓而不是错位的多个文本，观感更清晰
    /// </summary>
    public class OutlinedText : FrameworkElement
    {
        private FormattedText _formattedText;
        private Geometry _geometry;
        private Pen _strokePen;

        public string Text { get; set; } = "";
        public double TextFontSize { get; set; } = 25;
        public FontFamily TextFontFamily { get; set; }
        public Brush Fill { get; set; } = Brushes.White;
        public Brush Stroke { get; set; } = Brushes.Black;
        public double StrokeThickness { get; set; } = 1.0;

        private void EnsureGeometry()
        {
            if (_formattedText != null)
            {
                return;
            }
            Typeface typeface = new Typeface(
                TextFontFamily ?? SystemFonts.MessageFontFamily,
                FontStyles.Normal,
                //用Black(900)字重让弹幕更粗；若自定义字体没有Black字重会回落到Bold，观感不变差
                FontWeights.Black,
                FontStretches.Normal);
            _formattedText = new FormattedText(
                Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                TextFontSize,
                Fill,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            _geometry = _formattedText.BuildGeometry(new Point(0, 0));
            _strokePen = new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
            _strokePen.Freeze();
        }

        /// <summary>
        /// 文本实际渲染宽度，弹幕动画终点需要用（几何体未生成时会触发懒加载）
        /// </summary>
        public double TextWidth
        {
            get
            {
                EnsureGeometry();
                return _formattedText.Width;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureGeometry();
            //四周各留StrokeThickness余量，避免描边外沿被裁剪
            return new Size(_formattedText.Width + StrokeThickness * 2, _formattedText.Height + StrokeThickness * 2);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            EnsureGeometry();
            //一次DrawGeometry同时完成填充和描边，等价于原来5个TextBlock叠加的效果
            drawingContext.DrawGeometry(Fill, _strokePen, _geometry);
        }
    }
}
