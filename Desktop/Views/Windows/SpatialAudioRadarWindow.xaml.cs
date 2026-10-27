using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Desktop.Services.SpatialAudio;
using Wpf.Ui.Controls;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace Desktop.Views.Windows
{
    /// <summary>
    /// 空间音频 2.5D 极坐标雷达交互窗口
    /// 支持左键拖动音源小球、中键拖动旋转人头朝向、滚轮调整距离
    /// </summary>
    public partial class SpatialAudioRadarWindow : FluentWindow
    {
        private static SpatialAudioRadarWindow? _instance;
        private static readonly object _lock = new();

        private bool _isDraggingBall = false;
        private long _draggingRoomId = 0;

        private bool _isRotatingHead = false;

        // 基础标准距离 (1.0m) 对应的画布像素半径
        private float _baseRadiusPixels = 120f;

        // 保存小球 HitTest 区域
        private readonly Dictionary<long, Rect> _ballHitBoxes = new();

        public static SpatialAudioRadarWindow GetOrCreate()
        {
            lock (_lock)
            {
                if (_instance == null || !_instance.IsLoaded)
                {
                    _instance = new SpatialAudioRadarWindow();
                }
                return _instance;
            }
        }

        public SpatialAudioRadarWindow()
        {
            InitializeComponent();
        }

        private void FluentWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ToggleSpatial.IsChecked = SpatialAudioEngine.Instance.IsEnabled;
            UpdateHeadYawHud(SpatialAudioEngine.Instance.ListenerHeadYaw);

            SpatialAudioEngine.Instance.ListenerHeadYawChanged += OnListenerHeadYawChanged;
            SpatialAudioEngine.Instance.ChannelAdded += OnChannelChanged;
            SpatialAudioEngine.Instance.ChannelRemoved += OnChannelRemoved;

            RedrawRadar();
        }

        private void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SpatialAudioEngine.Instance.ListenerHeadYawChanged -= OnListenerHeadYawChanged;
            SpatialAudioEngine.Instance.ChannelAdded -= OnChannelChanged;
            SpatialAudioEngine.Instance.ChannelRemoved -= OnChannelRemoved;
            _instance = null;
        }

        private void OnListenerHeadYawChanged(float yaw)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateHeadYawHud(yaw);
                RedrawRadar();
            });
        }

        private void OnChannelChanged(SpatialAudioChannel chan)
        {
            Dispatcher.Invoke(RedrawRadar);
        }

        private void OnChannelRemoved(long roomId)
        {
            Dispatcher.Invoke(RedrawRadar);
        }

        private void UpdateHeadYawHud(float yaw)
        {
            TxtHeadYaw.Text = $"人头朝向: {yaw:F1}°";
        }

        private void RadarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawRadar();
        }

        /// <summary>
        /// 核心绘制方法：极坐标同心圆、方向坐标轴、听者人头模型、音源小球与连线
        /// </summary>
        private void RedrawRadar()
        {
            if (RadarCanvas.ActualWidth <= 0 || RadarCanvas.ActualHeight <= 0) return;

            RadarCanvas.Children.Clear();
            _ballHitBoxes.Clear();

            double width = RadarCanvas.ActualWidth;
            double height = RadarCanvas.ActualHeight;
            Point center = new(width / 2.0, height / 2.0);

            _baseRadiusPixels = (float)(Math.Min(width, height) * 0.28);
            if (_baseRadiusPixels < 40f) _baseRadiusPixels = 40f;

            var channels = SpatialAudioEngine.Instance.Channels;
            EmptyHintPanel.Visibility = channels.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

            // 1. 绘制极坐标同心距离圆与标签 (0.5m, 1.0m, 1.5m, 2.0m)
            float[] distanceLevels = { 0.5f, 1.0f, 1.5f, 2.0f };
            foreach (float d in distanceLevels)
            {
                double r = d * _baseRadiusPixels;
                Ellipse circle = new()
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = d == 1.0f ? new SolidColorBrush(Color.FromArgb(90, 76, 194, 255)) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = d == 1.0f ? 1.5 : 1.0,
                    StrokeDashArray = d == 1.0f ? null : new DoubleCollection { 4, 4 },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(circle, center.X - r);
                Canvas.SetTop(circle, center.Y - r);
                RadarCanvas.Children.Add(circle);

                // 距离刻度文字
                TextBlock lbl = new()
                {
                    Text = $"{d:F1}m",
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 200, 200, 200)),
                    FontSize = 10,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(lbl, center.X + 6);
                Canvas.SetTop(lbl, center.Y - r - 12);
                RadarCanvas.Children.Add(lbl);
            }

            // 2. 绘制十字十字坐标轴与方位文字 (前/后/左/右)
            Line horizAxis = new()
            {
                X1 = 20,
                Y1 = center.Y,
                X2 = width - 20,
                Y2 = center.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            Line vertAxis = new()
            {
                X1 = center.X,
                Y1 = 20,
                X2 = center.X,
                Y2 = height - 20,
                Stroke = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            RadarCanvas.Children.Add(horizAxis);
            RadarCanvas.Children.Add(vertAxis);

            AddDirectionLabel("前 (0°)", center.X, 10, HorizontalAlignment.Center);
            AddDirectionLabel("后 (180°)", center.X, height - 24, HorizontalAlignment.Center);
            AddDirectionLabel("左 (-90°)", 10, center.Y - 8, HorizontalAlignment.Left);
            AddDirectionLabel("右 (+90°)", width - 50, center.Y - 8, HorizontalAlignment.Right);

            // 3. 绘制各直播间音源小球及连线
            foreach (var kv in channels)
            {
                var chan = kv.Value;
                DrawSourceBall(chan, center);
            }

            // 4. 绘制中心人头模型 (置于最顶层，随 ListenerHeadYaw 旋转)
            DrawCenterHead(center, SpatialAudioEngine.Instance.ListenerHeadYaw);
        }

        private void AddDirectionLabel(string text, double x, double y, HorizontalAlignment align)
        {
            TextBlock tb = new()
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 180, 190)),
                FontSize = 11,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            RadarCanvas.Children.Add(tb);
        }

        /// <summary>
        /// 绘制中心人头模型（含左右耳机、前方朝向箭头、前方听觉视锥）
        /// </summary>
        private void DrawCenterHead(Point center, float yawDegrees)
        {
            double headSize = 54;
            Grid headContainer = new()
            {
                Width = headSize,
                Height = headSize,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(yawDegrees),
                IsHitTestVisible = false
            };

            // 前方主听觉视锥 (前向扇形指示)
            Polygon cone = new()
            {
                Points = new PointCollection
                {
                    new Point(headSize / 2.0, headSize / 2.0),
                    new Point(headSize / 2.0 - 45, -35),
                    new Point(headSize / 2.0 + 45, -35)
                },
                Fill = new SolidColorBrush(Color.FromArgb(30, 76, 194, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(60, 76, 194, 255)),
                StrokeThickness = 1
            };
            headContainer.Children.Add(cone);

            // 头部主体圆形
            Ellipse headCircle = new()
            {
                Width = 42,
                Height = 42,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new LinearGradientBrush(
                    Color.FromArgb(255, 42, 44, 54),
                    Color.FromArgb(255, 26, 27, 34),
                    new Point(0, 0),
                    new Point(1, 1)
                ),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255)),
                StrokeThickness = 2
            };
            headContainer.Children.Add(headCircle);

            // 左耳耳机
            Border earL = new()
            {
                Width = 7,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 161, 214)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 0, 0)
            };
            // 右耳耳机
            Border earR = new()
            {
                Width = 7,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 161, 214)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 1, 0)
            };
            headContainer.Children.Add(earL);
            headContainer.Children.Add(earR);

            // 正前方朝向小箭头/鼻尖
            Polygon nose = new()
            {
                Points = new PointCollection
                {
                    new Point(headSize / 2.0 - 5, 8),
                    new Point(headSize / 2.0 + 5, 8),
                    new Point(headSize / 2.0, 0)
                },
                Fill = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255))
            };
            headContainer.Children.Add(nose);

            Canvas.SetLeft(headContainer, center.X - headSize / 2.0);
            Canvas.SetTop(headContainer, center.Y - headSize / 2.0);
            RadarCanvas.Children.Add(headContainer);
        }

        /// <summary>
        /// 绘制直播间音源可拖拽小球、信息气泡及连线
        /// </summary>
        private void DrawSourceBall(SpatialAudioChannel chan, Point center)
        {
            double r = chan.Distance * _baseRadiusPixels;
            double rad = chan.Azimuth * (Math.PI / 180.0);

            // 极坐标转换为笛卡尔画布坐标 (0° 为上方，顺时针为正)
            double ballX = center.X + r * Math.Sin(rad);
            double ballY = center.Y - r * Math.Cos(rad);

            double ballRadius = 22;
            Rect hitRect = new(ballX - ballRadius, ballY - ballRadius, ballRadius * 2, ballRadius * 2);
            _ballHitBoxes[chan.RoomId] = hitRect;

            // 连接听者人头到音源小球的虚线
            Line tether = new()
            {
                X1 = center.X,
                Y1 = center.Y,
                X2 = ballX,
                Y2 = ballY,
                Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 161, 214)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            RadarCanvas.Children.Add(tether);

            // 音源发光小球
            Grid ballGrid = new()
            {
                Width = ballRadius * 2,
                Height = ballRadius * 2,
                IsHitTestVisible = false
            };

            Ellipse outerGlow = new()
            {
                Fill = new RadialGradientBrush(
                    Color.FromArgb(160, 0, 161, 214),
                    Color.FromArgb(0, 0, 161, 214)
                )
            };
            ballGrid.Children.Add(outerGlow);

            Ellipse ball = new()
            {
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new LinearGradientBrush(
                    Color.FromArgb(255, 0, 161, 214),
                    Color.FromArgb(255, 0, 114, 198),
                    new Point(0, 0),
                    new Point(1, 1)
                ),
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            ballGrid.Children.Add(ball);

            // 球内文字/图标（首字或小音符）
            string initial = string.IsNullOrWhiteSpace(chan.Title) ? "播" : chan.Title.Substring(0, 1);
            TextBlock ballTxt = new()
            {
                Text = initial,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ballGrid.Children.Add(ballTxt);

            Canvas.SetLeft(ballGrid, ballX - ballRadius);
            Canvas.SetTop(ballGrid, ballY - ballRadius);
            RadarCanvas.Children.Add(ballGrid);

            // 小球下方的坐标与房间名浮动标签
            Border tagBorder = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 20, 20, 26)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };

            StackPanel tagStack = new() { Orientation = Orientation.Vertical };
            TextBlock titleTb = new()
            {
                Text = chan.Title,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                MaxWidth = 130,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            TextBlock coordTb = new()
            {
                Text = $"{chan.Azimuth:F0}° | {chan.Distance:F1}m",
                Foreground = new SolidColorBrush(Color.FromArgb(220, 76, 194, 255)),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tagStack.Children.Add(titleTb);
            tagStack.Children.Add(coordTb);
            tagBorder.Child = tagStack;

            Canvas.SetLeft(tagBorder, ballX - 45);
            Canvas.SetTop(tagBorder, ballY + ballRadius + 3);
            RadarCanvas.Children.Add(tagBorder);
        }

        #region 鼠标交互核心事件处理

        private void RadarCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(RadarCanvas);

            // 1. 中键按下：进入旋转人头朝向模式
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                _isRotatingHead = true;
                RadarCanvas.CaptureMouse();
                RotateHeadTowards(mousePos);
                e.Handled = true;
                return;
            }

            // 2. 左键按下：检查是否命中某个音源小球进行拖拽
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                foreach (var kv in _ballHitBoxes)
                {
                    if (kv.Value.Contains(mousePos))
                    {
                        if (e.ClickCount == 2)
                        {
                            // 双击小球：快速复位到正前方 1.0m
                            if (SpatialAudioEngine.Instance.Channels.TryGetValue(kv.Key, out var c))
                            {
                                c.Azimuth = 0f;
                                c.Distance = 1.0f;
                                RedrawRadar();
                            }
                            return;
                        }

                        _isDraggingBall = true;
                        _draggingRoomId = kv.Key;
                        RadarCanvas.CaptureMouse();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private void RadarCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition(RadarCanvas);

            // 中键拖动中：实时根据鼠标位置旋转人头
            if (_isRotatingHead && e.MiddleButton == MouseButtonState.Pressed)
            {
                RotateHeadTowards(mousePos);
                return;
            }

            // 左键拖动音源小球中：实时计算极坐标并更新
            if (_isDraggingBall && e.LeftButton == MouseButtonState.Pressed)
            {
                if (SpatialAudioEngine.Instance.Channels.TryGetValue(_draggingRoomId, out var chan))
                {
                    double width = RadarCanvas.ActualWidth;
                    double height = RadarCanvas.ActualHeight;
                    Point center = new(width / 2.0, height / 2.0);

                    double dx = mousePos.X - center.X;
                    double dy = center.Y - mousePos.Y; // 笛卡尔坐标 Y 向上为正

                    double azimuth = Math.Atan2(dx, dy) * (180.0 / Math.PI);
                    double distPixels = Math.Sqrt(dx * dx + dy * dy);
                    double baseRadius = _baseRadiusPixels > 10f ? _baseRadiusPixels : 100f;
                    double distance = distPixels / baseRadius;

                    if (!double.IsNaN(azimuth) && !double.IsInfinity(azimuth))
                    {
                        chan.Azimuth = (float)azimuth;
                    }
                    if (!double.IsNaN(distance) && !double.IsInfinity(distance))
                    {
                        chan.Distance = (float)Math.Clamp(distance, 0.2, 3.0);
                    }

                    RedrawRadar();
                }
            }
        }

        private void RadarCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRotatingHead && e.MiddleButton == MouseButtonState.Released)
            {
                _isRotatingHead = false;
                RadarCanvas.ReleaseMouseCapture();
            }

            if (_isDraggingBall && e.LeftButton == MouseButtonState.Released)
            {
                _isDraggingBall = false;
                RadarCanvas.ReleaseMouseCapture();
            }
        }

        private void RadarCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (e.MiddleButton != MouseButtonState.Pressed) _isRotatingHead = false;
            if (e.LeftButton != MouseButtonState.Pressed) _isDraggingBall = false;
        }

        private void RadarCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(RadarCanvas);
            // 若滚轮在某个小球上，微调该小球的距离
            foreach (var kv in _ballHitBoxes)
            {
                if (kv.Value.Contains(mousePos))
                {
                    if (SpatialAudioEngine.Instance.Channels.TryGetValue(kv.Key, out var chan))
                    {
                        float delta = e.Delta > 0 ? -0.1f : 0.1f;
                        chan.Distance = Math.Clamp(chan.Distance + delta, 0.2f, 3.0f);
                        RedrawRadar();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 根据鼠标当前在画布中的位置，计算并旋转人头朝向
        /// </summary>
        private void RotateHeadTowards(Point mousePos)
        {
            double width = RadarCanvas.ActualWidth;
            double height = RadarCanvas.ActualHeight;
            Point center = new(width / 2.0, height / 2.0);

            double dx = mousePos.X - center.X;
            double dy = center.Y - mousePos.Y;

            double angleDeg = Math.Atan2(dx, dy) * (180.0 / Math.PI);
            if (!double.IsNaN(angleDeg) && !double.IsInfinity(angleDeg))
            {
                SpatialAudioEngine.Instance.ListenerHeadYaw = (float)angleDeg;
            }
        }

        #endregion

        #region 控制条按钮与事件

        private void BtnResetHead_Click(object sender, RoutedEventArgs e)
        {
            SpatialAudioEngine.Instance.ListenerHeadYaw = 0f;
        }

        private void BtnAutoArrange_Click(object sender, RoutedEventArgs e)
        {
            var channels = SpatialAudioEngine.Instance.Channels.Values;
            int total = channels.Count;
            if (total == 0) return;

            if (total == 1)
            {
                foreach (var c in channels)
                {
                    c.Azimuth = 0f;
                    c.Distance = 1.0f;
                }
            }
            else
            {
                // 在听者四周 360° 或前方扇区均匀排布
                float step = 360f / total;
                int i = 0;
                foreach (var c in channels)
                {
                    c.Azimuth = (i * step) > 180f ? (i * step) - 360f : (i * step);
                    c.Distance = 1.0f;
                    i++;
                }
            }

            RedrawRadar();
        }

        private void ToggleSpatial_Checked(object sender, RoutedEventArgs e)
        {
            SpatialAudioEngine.Instance.IsEnabled = true;
        }

        private void ToggleSpatial_Unchecked(object sender, RoutedEventArgs e)
        {
            SpatialAudioEngine.Instance.IsEnabled = false;
        }

        #endregion
    }
}
