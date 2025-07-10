using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Point = netDxf.Entities.Point;
using HatchStyle = System.Drawing.Drawing2D.HatchStyle;

namespace NetToolLib
{
    /// <summary> 
    /// Dxf渲染image选项
    /// </summary>
    public class DxfRenderOptions
    {
        /// <summary>
        /// 是否绘制标注
        /// </summary>
        public bool DrawDimensions { get; set; } = true;

        /// <summary>
        /// 是否绘制文字
        /// </summary>
        public bool DrawText { get; set; } = true;

        /// <summary>
        /// 是否尝试绘制填充图案
        /// </summary>
        public bool DrawHatch { get; set; } = false;

        /// <summary>
        /// 是否保持DXF原始颜色
        /// </summary>
        public bool KeepOriginalColors { get; set; } = false;

        /// <summary>
        /// 图像宽度
        /// </summary>
        public int Width { get; set; } = 1200;

        /// <summary>
        /// 图像高度
        /// </summary>
        public int Height { get; set; } = 1600;

        /// <summary>
        /// 线条宽度
        /// </summary>
        public float LineWidth { get; set; } = 1f;


        /// <summary>
        /// 抗锯齿模式
        /// </summary>
        public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.AntiAlias;

        /// <summary>
        /// 文字渲染质量
        /// </summary>
        public TextRenderingHint TextRenderingHint { get; set; } = TextRenderingHint.AntiAlias;

        /// <summary>
        /// 边距（像素）
        /// </summary>
        public int Margin { get; set; } = 20;

        /// <summary>
        /// 是否裁剪周围无效边
        /// </summary>
        public bool CropEmptyEdges { get; set; } = false;

        /// <summary>
        /// 是否绘制调试边界框（红色矩形框）
        /// </summary>
        public bool DrawDebugBounds { get; set; } = false;

        /// <summary>
        /// 是否强制启用内置的边界重新计算，不使用cad文档的自动 MIN和MAX。
        /// </summary>
        public bool EnableReCalcBound { get; set; } = false;

        /// <summary>
        /// 是否显示Wipeout边框
        /// </summary>
        public bool ShowWipeoutFrame { get; set; } = false;
    }

    /// <summary>
    /// Dxf文档绘制到图像的渲染器
    /// 1：支持96%以上的DXF实体类型(暂不支持有：圆弧异形填充(支持线段，多段线，曲线填充)、少量特殊图形不支持)。
    /// 2：可以将DXF渲染到任意大小的图像上，自动计算并缩放cad图大小。
    /// 3：支持自定义画布后，可以添加dxf实体实时绘制。
    /// anthor：Jenkin Liu
    /// </summary>
    public class DxfImageRenderer : IDisposable
    {
        private DxfDocument _document;
        private DxfRenderOptions _options;
        private RectangleF _bounds;
        private float _scale;
        private PointF _offset;

        // 资源缓存
        private readonly Dictionary<Color, Pen> _penCache = new Dictionary<Color, Pen>();
        private readonly Dictionary<Color, SolidBrush> _brushCache = new Dictionary<Color, SolidBrush>();
        private readonly Dictionary<float, Font> _fontCache = new Dictionary<float, Font>();

        // 是否已释放资源
        private bool _disposed = false;

        // 并行处理选项
        private bool _enableParallelProcessing = false;

        // 当前绘制的Graphics对象
        private Graphics? _currentGraphics;

        /// <summary>
        /// 构造函数，默认 DxfDocument 。保存文件 使用 SaveDxfToFile 方法
        /// </summary>
        /// <param name="options">渲染选项</param>
        /// <param name="enableParallelProcessing">是否启用并行处理</param>
        public DxfImageRenderer(DxfRenderOptions? options = null, bool enableParallelProcessing = false)
            : this(new DxfDocument(), options, enableParallelProcessing)
        {
        }

        /// <summary>
        /// 构造函数，指定DxfDocument
        /// </summary>
        /// <param name="document">DXF文档</param>
        /// <param name="options">渲染选项</param>
        /// <param name="enableParallelProcessing">是否启用并行处理</param>
        public DxfImageRenderer(DxfDocument document, DxfRenderOptions? options = null, bool enableParallelProcessing = false)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _options = options ?? new DxfRenderOptions();
            _enableParallelProcessing = enableParallelProcessing;
            CalculateBounds();
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~DxfImageRenderer()
        {
            Dispose(false);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否为显式释放</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 释放托管资源
                ClearCaches();
            }

            _disposed = true;
        }

        #region Public method

        /// <summary>
        /// 渲染到图片文件
        /// </summary>
        /// <param name="filePath">输出文件路径</param>
        /// <param name="format">图像格式</param>
        public void RenderToPng(string filePath, ImageFormat format)
        {
            using (var bitmap = RenderToBitmap())
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                bitmap.Save(filePath, format);
            }
        }
        /// <summary>
        /// 渲染到位图
        /// </summary>
        /// <returns>位图对象</returns>
        public Bitmap RenderToBitmap()
        {
            var bitmap = new Bitmap(_options.Width, _options.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                RenderToGraphics(graphics);
            }

            // 如果需要裁剪无效边
            if (_options.CropEmptyEdges)
            {
                return CropImage(bitmap);
            }

            return bitmap;
        }

        /// <summary>
        /// 渲染到指定的Graphics对象
        /// </summary>
        /// <param name="graphics">目标Graphics对象</param>
        /// <param name="clearBackground">是否清除背景（默认true）</param>
        /// <param name="applyTransform">是否应用坐标变换（默认true）</param>
        public void RenderToGraphics(Graphics graphics)
        {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));

            // 设置当前Graphics对象
            _currentGraphics = graphics;

            // 设置渲染质量
            graphics.SmoothingMode = _options.SmoothingMode;
            graphics.TextRenderingHint = _options.TextRenderingHint;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            graphics.Clear(_options.KeepOriginalColors ? Color.Black : Color.White);

            // 设置坐标变换（如果需要）
            SetupTransform();

            // 绘制所有实体
            DrawEntities();

            // 绘制调试边界框
            if (_options.DrawDebugBounds)
            {
                DrawDebugBounds();
            }
        }

        /// <summary>
        /// 计算范围后，保存 dxf 文档到文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="format"></param>
        public void SaveDxfToFile(string filePath, DxfVersion ver = DxfVersion.AutoCad2004)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _document.Viewport.ShowGrid = false;
            _document.DrawingVariables.AcadVer = ver;

            CalculateBounds();

            _document.Viewport.ViewHeight = _bounds.Height + 100;
            _document.Viewport.ViewCenter = new Vector2(_bounds.X, _bounds.Y + (_bounds.Height / 2));

            //if (_document.Layouts.TryGetValue("Model", out Layout modelLayout))
            //{
            //    modelLayout.MinExtents = new Vector3(_bounds.X + _bounds.Width, _bounds.Y, 0);
            //    modelLayout.MaxExtents = new Vector3(_bounds.Y + _bounds.Height, _bounds.Y, 0);
            //}

            _document.Save(filePath);
        }

        /// <summary>
        /// 添加实体到dxfdocument文档，并绘制到当前图形上下文
        /// </summary>
        /// <param name="entityObject"></param>
        public void AddEntity(EntityObject entityObject)
        {
            DrawDxfEntity(entityObject);
            _document.Entities.Add(entityObject);
        }

        public void DrawEntity(EntityObject entityObject)
        {
            DrawDxfEntity(entityObject);
        }

        #endregion

        /// <summary>
        /// 解析 EntityObject 实体绘制到图形上
        /// </summary>
        /// <param name="entity"></param>
        private void DrawDxfEntity(EntityObject entity, float penWidth = 0)
        {
            var color = GetEntityColor(entity);
            var pen = GetPen(color);
            if (penWidth > 0)
            {
                pen.Width = penWidth;
            }

            var brush = GetBrush(color);

            if (entity is Line line)
            {
                DrawLine(line, pen);
            }
            else if (entity is Arc arc)
            {
                DrawArc(arc, pen);
            }
            else if (entity is Circle circle)
            {
                DrawCircle(circle, pen);
            }
            else if (entity is Ellipse ellipse)
            {
                DrawEllipse(ellipse, pen);
            }
            else if (entity is Point point)
            {
                DrawPoint(point, brush);
            }
            else if (entity is Polyline2D polyline2D)
            {
                DrawPolyline(polyline2D, pen);
            }
            else if (entity is MLine mline)
            {
                DrawMLine(mline, pen);
            }
            else if (entity is Text text)
            {
                DrawText(text, brush);
            }
            else if (entity is MText mtext)
            {
                DrawMText(mtext, brush);
            }
            else if (entity is Leader leader)
            {
                DrawLeader(leader, pen);
            }
            // 添加对各种标注类型的处理
            else if (_options.DrawDimensions && entity is Dimension)
            {
                if (entity is LinearDimension linearDimension)
                {
                    DrawLinearDimension(linearDimension, pen, brush);
                }
                else if (entity is AlignedDimension alignedDimension)
                {
                    DrawAlignedDimension(alignedDimension, pen, brush);
                }
                else if (entity is RadialDimension radialDimension)
                {
                    DrawRadialDimension(radialDimension, pen, brush);
                }
                else if (entity is DiametricDimension diametricDimension)
                {
                    DrawDiametricDimension(diametricDimension, pen, brush);
                }
                else if (entity is Angular2LineDimension angular2LineDimension)
                {
                    DrawAngular2LineDimension(angular2LineDimension, pen, brush);
                }
                else if (entity is OrdinateDimension ordinateDimension)
                {
                    DrawOrdinateDimension(ordinateDimension, pen, brush);
                }
                else if (entity is ArcLengthDimension arcLengthDimension)
                {
                    DrawArcLengthDimension(arcLengthDimension, pen, brush);
                }
            }
            else if (entity is Hatch hatch)
            {
                DrawHatch(hatch, pen, brush);
            }
            else if (entity is Insert insert)
            {
                DrawInsert(insert, pen, brush);
            }
            else if (entity is Spline spline)
            {
                DrawSpline(spline, pen);
            }
            else if (entity is netDxf.Entities.Image image)
            {
                DrawImage(image, pen, brush);
            }
            else if (entity is netDxf.Entities.Wipeout wipeout)
            {
                DrawWipeout(wipeout, pen, brush);
            }
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        private void ClearCaches()
        {
            // 清除画笔缓存
            foreach (var pen in _penCache.Values)
            {
                pen.Dispose();
            }
            _penCache.Clear();

            // 清除画刷缓存
            foreach (var brush in _brushCache.Values)
            {
                brush.Dispose();
            }
            _brushCache.Clear();

            // 清除字体缓存
            foreach (var font in _fontCache.Values)
            {
                font.Dispose();
            }
            _fontCache.Clear();
        }


        /// <summary>
        /// 计算绘图边界
        /// </summary>
        private void CalculateBounds()
        {
            float minX = 999999, minY = 999999;
            float maxX = 0, maxY = 0;
            bool getexy = true;
            HeaderVariable extMinVar;
            HeaderVariable extMaxVar;
            // 如果有自定义的EXTMIN和EXTMAX变量，先使用它们来初始化边界
            if (_document.DrawingVariables.TryGetCustomVariable("$EXTMIN", out extMinVar))
            {
                minX = (float)((Vector3)extMinVar.Value).X;
                minY = (float)((Vector3)extMinVar.Value).Y;
            }
            else
            {
                getexy = false;
            }

            if (_document.DrawingVariables.TryGetCustomVariable("$EXTMAX", out extMaxVar))
            {
                maxX = (float)((Vector3)extMaxVar.Value).X;
                maxY = (float)((Vector3)extMaxVar.Value).Y;
            }
            else
            {
                getexy = false;
            }

            if (_options.EnableReCalcBound || !(getexy && minX > -5999 && minY > -5999 && maxX < 5999 && maxY < 5999)
                || (getexy && minX == 0 && minY == 0 && maxX < 100 && maxY < 100))
            {
                minX = 999999;
                minY = 999999;
                maxX = 0;
                maxY = 0;

                foreach (var entity in _document.Entities.All)
                {
                    if (!ShouldDrawEntity(entity)) continue;

                    var bounds = GetEntityBounds(entity);
                    if (bounds.HasValue)
                    {
                        minX = Math.Min(minX, bounds.Value.Left);
                        minY = Math.Min(minY, bounds.Value.Top);
                        maxX = Math.Max(maxX, bounds.Value.Right);
                        maxY = Math.Max(maxY, bounds.Value.Bottom);
                    }
                }
            }


            if (maxX == 0 || maxY == 0 || minX >= 999999 || minY >= 99999)
            {
                _bounds = new RectangleF(0, 0, _options.Width, _options.Height);
            }
            else
            {
                _bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
        }

        /// <summary>
        /// 设置坐标变换
        /// </summary>
        /// <param name="graphics">图形对象</param>
        private void SetupTransform()
        {
            // 计算可用区域
            float availableWidth = _options.Width - 2 * _options.Margin;
            float availableHeight = _options.Height - 2 * _options.Margin;

            // 计算缩放比例
            float scaleX = availableWidth / _bounds.Width;
            float scaleY = availableHeight / _bounds.Height;

            // 使用较小的缩放比例，确保图像完全显示在视图内
            _scale = Math.Min(scaleX, scaleY);

            float scaledWidth = _bounds.Width * _scale;
            float scaledHeight = _bounds.Height * _scale;

            // 先翻转Y轴
            _currentGraphics?.ScaleTransform(1, -1);
            _currentGraphics?.TranslateTransform(0, -_options.Height);

            // 计算图形边界框的中心点
            float boundsCenterX = _bounds.X + _bounds.Width / 2;
            float boundsCenterY = _bounds.Y + _bounds.Height / 2;

            // 计算画布的中心点
            float canvasCenterX = _options.Width / 2;
            float canvasCenterY = _options.Height / 2;

            // 计算偏移量，使图形中心对齐到画布中心
            _offset = new PointF(
                canvasCenterX - boundsCenterX * _scale,
                canvasCenterY - boundsCenterY * _scale
            );
        }

        /// <summary>
        /// 裁剪图像，去除周围的空白区域
        /// </summary>
        /// <param name="sourceBitmap">原始图像</param>
        /// <returns>裁剪后的图像</returns>
        private Bitmap CropImage(Bitmap sourceBitmap)
        {
            // 使用已经计算好的_bounds和_scale来确定裁剪区域
            // 计算实际绘制区域在图像中的位置
            float scaledWidth = _bounds.Width * _scale;
            float scaledHeight = _bounds.Height * _scale;

            // 计算可用区域
            float availableWidth = _options.Width - 2 * _options.Margin;
            float availableHeight = _options.Height - 2 * _options.Margin;

            // 计算实际绘制区域的左上角坐标
            float startX = _options.Margin + (availableWidth - scaledWidth) / 2;
            float startY = _options.Margin + (availableHeight - scaledHeight) / 2;

            // 确保坐标不会超出边界
            startX = Math.Max(_options.Margin, startX);
            startY = Math.Max(_options.Margin, startY);

            // 计算裁剪区域
            int cropX = (int)Math.Floor(startX);
            int cropY = (int)Math.Floor(startY);
            int cropWidth = (int)Math.Ceiling(scaledWidth);
            int cropHeight = (int)Math.Ceiling(scaledHeight);

            // 确保裁剪区域不会超出图像边界
            cropWidth = Math.Min(cropWidth, sourceBitmap.Width - cropX);
            cropHeight = Math.Min(cropHeight, sourceBitmap.Height - cropY) + 30;

            // 如果裁剪区域无效，返回原图
            if (cropWidth <= 0 || cropHeight <= 0)
            {
                return sourceBitmap;
            }

            // 创建裁剪后的图像
            Bitmap croppedBitmap = new Bitmap(cropWidth, cropHeight);
            using (Graphics g = Graphics.FromImage(croppedBitmap))
            {
                g.DrawImage(sourceBitmap,
                            new Rectangle(0, 0, cropWidth, cropHeight),
                            new Rectangle(cropX, cropY, cropWidth, cropHeight),
                            GraphicsUnit.Pixel);
            }

            return croppedBitmap;
        }

        /// <summary>
        /// 转换坐标，移动到计算后的位置
        /// </summary>
        /// <param name="point">DXF坐标点</param>
        /// <returns>屏幕坐标点</returns>
        private PointF TransformPoint(netDxf.Vector2 point)
        {
            return new PointF(
                _offset.X + (float)point.X * _scale,
                _offset.Y + (float)point.Y * _scale
            );
        }

        /// <summary>
        /// 转换坐标，移动到计算后的位置
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        private PointF TransformPoint(netDxf.Vector3 point)
        {
            return new PointF(
                _offset.X + (float)point.X * _scale,
                _offset.Y + (float)point.Y * _scale
            );
        }

        /// <summary>
        /// 判断是否应该绘制实体
        /// </summary>
        /// <param name="entity">实体</param>
        /// <returns>是否绘制</returns>
        private bool ShouldDrawEntity(EntityObject entity)
        {
            // 检查图层是否可见，只绘制可见图层
            if (entity.Layer != null && !entity.Layer.IsVisible)
            {
                return false;
            }

            // 检查是否绘制标注
            if (entity is Dimension && !_options.DrawDimensions)
            {
                return false;
            }

            // 检查是否绘制文字
            if ((entity is Text || entity is MText) && !_options.DrawText)
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// 获取实体边界
        /// </summary>
        /// <param name="entity">实体</param>
        /// <returns>边界矩形</returns>
        private RectangleF? GetEntityBounds(EntityObject entity)
        {
            switch (entity)
            {
                case Line line:
                    return GetLineBounds(line);
                case Arc arc:
                    return GetArcBounds(arc);
                case Circle circle:
                    return GetCircleBounds(circle);
                case Ellipse ellipse:
                    return GetEllipseBounds(ellipse);
                case Text text:
                    // 文字边界，考虑文字位置、大小和对齐方式
                    float textHeight = (float)text.Height;
                    float textWidth = textHeight * (text.Value?.Length ?? 1) * 0.6f; // 更准确的宽度估算

                    float x = (float)text.Position.X;
                    float y = (float)text.Position.Y;

                    // 根据文字对齐方式调整位置
                    switch (text.Alignment)
                    {
                        case TextAlignment.TopLeft:
                        case TextAlignment.MiddleLeft:
                        case TextAlignment.BottomLeft:
                            // 左对齐，x坐标不变
                            break;
                        case TextAlignment.TopCenter:
                        case TextAlignment.MiddleCenter:
                        case TextAlignment.BottomCenter:
                            // 居中对齐
                            x -= textWidth / 2;
                            break;
                        case TextAlignment.TopRight:
                        case TextAlignment.MiddleRight:
                        case TextAlignment.BottomRight:
                            // 右对齐
                            x -= textWidth;
                            break;
                    }

                    // 根据垂直对齐调整y坐标
                    switch (text.Alignment)
                    {
                        case TextAlignment.TopLeft:
                        case TextAlignment.TopCenter:
                        case TextAlignment.TopRight:
                            // 顶部对齐
                            y -= textHeight;
                            break;
                        case TextAlignment.MiddleLeft:
                        case TextAlignment.MiddleCenter:
                        case TextAlignment.MiddleRight:
                            // 中间对齐
                            y -= textHeight / 2;
                            break;
                        case TextAlignment.BottomLeft:
                        case TextAlignment.BottomCenter:
                        case TextAlignment.BottomRight:
                            // 底部对齐，y坐标不变
                            break;
                    }

                    // 为单行文本也添加安全边距
                    float singleTextMargin = textHeight * 0.05f;
                    return new RectangleF(x - singleTextMargin, y - singleTextMargin,
                                        textWidth + singleTextMargin * 2, textHeight + singleTextMargin * 2);
                case MText mtext:

                    // 重新计算多行文本的高度
                    string plainText = mtext.Value ?? "";

                    if (string.IsNullOrEmpty(plainText))
                    {
                        return null;
                    }

                    // 多行文字边界，考虑文字位置、大小和对齐方式
                    float mtextHeight = (float)mtext.Height;
                    float mtextWidth = (float)mtext.RectangleWidth;

                    // 移除MText格式代码，但保留\\P换行符

                    // 情况1：按\\P分割计算行数
                    var pLines = plainText.Split(new string[] { "\\P" }, StringSplitOptions.None);
                    int linesFromP = pLines.Length;

                    // 情况2：按文字数量和宽度计算行数
                    int linesFromWidth = 1;
                    if (mtextWidth > 0)
                    {
                        // 计算总字符宽度
                        float charWidth = mtextHeight; // 单个字符宽度估算
                        string textForWidth = plainText.Replace("\\P", "");
                        float totalTextWidth = textForWidth.Length * charWidth;

                        if (totalTextWidth < mtextWidth)
                        {
                            mtextWidth = totalTextWidth;
                        }

                        linesFromWidth = (int)Math.Ceiling(totalTextWidth / mtextWidth);

                    }
                    else
                    {
                        // 如果没有指定宽度，根据最长行估算宽度
                        float maxLineWidth = 0;
                        foreach (var line in pLines)
                        {
                            float lineWidth = mtextHeight * line.Length * 0.7f;
                            if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
                        }
                        mtextWidth = Math.Max(maxLineWidth, mtextHeight * 3);
                        linesFromWidth = pLines.Length;
                    }

                    // 取两种情况的最大值作为最终行数
                    int finalLines = Math.Max(linesFromP, linesFromWidth);
                    mtextHeight = mtextHeight * finalLines;

                    float mx = (float)mtext.Position.X;
                    float my = (float)mtext.Position.Y;

                    // 根据MText的对齐方式调整位置
                    switch (mtext.AttachmentPoint)
                    {
                        case MTextAttachmentPoint.TopLeft:
                            my -= mtextHeight;
                            break;
                        case MTextAttachmentPoint.TopCenter:
                            mx -= mtextWidth / 2;
                            my -= mtextHeight;
                            break;
                        case MTextAttachmentPoint.TopRight:
                            mx -= mtextWidth;
                            my -= mtextHeight;
                            break;
                        case MTextAttachmentPoint.MiddleLeft:
                            my -= mtextHeight / 2;
                            break;
                        case MTextAttachmentPoint.MiddleCenter:
                            mx -= mtextWidth / 2;
                            my -= mtextHeight / 2;
                            break;
                        case MTextAttachmentPoint.MiddleRight:
                            mx -= mtextWidth;
                            my -= mtextHeight / 2;
                            break;
                        case MTextAttachmentPoint.BottomLeft:
                            // 默认位置
                            break;
                        case MTextAttachmentPoint.BottomCenter:
                            mx -= mtextWidth / 2;
                            break;
                        case MTextAttachmentPoint.BottomRight:
                            mx -= mtextWidth;
                            break;
                    }

                    // 为文本边界添加安全边距
                    float textMargin = mtextHeight * 0.5f;
                    return new RectangleF(mx - textMargin, my - textMargin,
                                        mtextWidth + textMargin * 2, mtextHeight + textMargin * 2);
                case Polyline2D polyline:
                    // 多段线边界计算
                    return GetPolylineBounds(polyline);
                case Dimension dim:

                    if (!_options.DrawDimensions)
                    {
                        return null;
                    }

                    if (dim is LinearDimension linearDim)
                    {
                        return CalculateDimensionBounds(linearDim.FirstReferencePoint, linearDim.SecondReferencePoint, linearDim);
                    }
                    else if (dim is AlignedDimension alignedDim)
                    {
                        return CalculateDimensionBounds(alignedDim.FirstReferencePoint, alignedDim.SecondReferencePoint, alignedDim);
                    }

                    return null;
                case netDxf.Entities.Image image:
                    // 图像边界，考虑图像位置、宽度和高度
                    float imageX = (float)image.Position.X;
                    float imageY = (float)image.Position.Y;
                    float imageWidth = (float)image.Width;
                    float imageHeight = (float)image.Height;

                    // 如果有旋转，需要考虑旋转后的边界
                    if (Math.Abs(image.Rotation) > 0.001)
                    {
                        // 计算四个角点（将度数转换为弧度进行三角函数计算）
                        double angleRad = -image.Rotation * (Math.PI / 180.0);
                        double cosAngle = Math.Cos(angleRad);
                        double sinAngle = Math.Sin(angleRad);

                        // 计算旋转后的四个角点
                        var points = new PointF[4];
                        points[0] = new PointF(imageX, imageY); // 左下
                        points[1] = new PointF(imageX + (float)(imageWidth * cosAngle), imageY + (float)(imageWidth * sinAngle)); // 右下
                        points[2] = new PointF(imageX + (float)(imageWidth * cosAngle - imageHeight * sinAngle), imageY + (float)(imageWidth * sinAngle + imageHeight * cosAngle)); // 右上
                        points[3] = new PointF(imageX - (float)(imageHeight * sinAngle), imageY + (float)(imageHeight * cosAngle)); // 左上

                        // 计算包围盒
                        float minX = points.Min(p => p.X);
                        float minY = points.Min(p => p.Y);
                        float maxX = points.Max(p => p.X);
                        float maxY = points.Max(p => p.Y);

                        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
                    }
                    else
                    {
                        // 无旋转，直接返回矩形
                        return new RectangleF(imageX, imageY, imageWidth, imageHeight);
                    }
                default:
                    return null;
            }
        }

        /// <summary>
        /// 获取线条边界
        /// </summary>
        private RectangleF GetLineBounds(Line line)
        {
            float minX = Math.Min((float)line.StartPoint.X, (float)line.EndPoint.X);
            float minY = Math.Min((float)line.StartPoint.Y, (float)line.EndPoint.Y);
            float maxX = Math.Max((float)line.StartPoint.X, (float)line.EndPoint.X);
            float maxY = Math.Max((float)line.StartPoint.Y, (float)line.EndPoint.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 获取圆弧边界
        /// </summary>
        private RectangleF GetArcBounds(Arc arc)
        {

            float radius = (float)arc.Radius;
            float centerX = (float)arc.Center.X;
            float centerY = (float)arc.Center.Y;

            // DXF中的角度已经是度数
            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;

            // 计算圆弧端点（需要转换为弧度进行三角函数计算）
            double startAngleRad = startAngle * (Math.PI / 180.0);
            double endAngleRad = endAngle * (Math.PI / 180.0);
            float startX = centerX + radius * (float)Math.Cos(startAngleRad);
            float startY = centerY + radius * (float)Math.Sin(startAngleRad);
            float endX = centerX + radius * (float)Math.Cos(endAngleRad);
            float endY = centerY + radius * (float)Math.Sin(endAngleRad);

            // 初始边界为端点
            float minX = Math.Min(startX, endX);
            float maxX = Math.Max(startX, endX);
            float minY = Math.Min(startY, endY);
            float maxY = Math.Max(startY, endY);

            // 检查圆弧是否跨越关键角度（0°, 90°, 180°, 270°）
            double[] keyAngles = { 0, 90, 180, 270 };

            foreach (double angle in keyAngles)
            {
                bool angleInRange;

                // 处理跨越0度的情况
                if (endAngle < startAngle)
                {
                    // 跨越0度：角度在[startAngle, 360)或[0, endAngle]范围内
                    angleInRange = (angle >= startAngle && angle <= 360) || (angle >= 0 && angle <= endAngle);
                }
                else
                {
                    // 正常情况：角度在[startAngle, endAngle]范围内
                    angleInRange = angle >= startAngle && angle <= endAngle;
                }

                if (angleInRange)
                {
                    double angleRad = angle * (Math.PI / 180.0);
                    float x = centerX + radius * (float)Math.Cos(angleRad);
                    float y = centerY + radius * (float)Math.Sin(angleRad);
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 获取圆边界
        /// </summary>
        private RectangleF GetCircleBounds(Circle circle)
        {
            float radius = (float)circle.Radius;
            float centerX = (float)circle.Center.X;
            float centerY = (float)circle.Center.Y;
            return new RectangleF(centerX - radius, centerY - radius, radius * 2, radius * 2);
        }

        /// <summary>
        /// 获取椭圆边界
        /// </summary>
        private RectangleF GetEllipseBounds(Ellipse ellipse)
        {
            float centerX = (float)ellipse.Center.X;
            float centerY = (float)ellipse.Center.Y;
            // MajorAxis和MinorAxis是直径，需要除以2得到半径
            float majorRadius = (float)ellipse.MajorAxis / 2;
            float minorRadius = (float)ellipse.MinorAxis / 2;

            // 检查是否为完整椭圆
            if (ellipse.IsFullEllipse)
            {
                // 完整椭圆的边界计算，需要考虑旋转
                return GetFullEllipseBounds(centerX, centerY, majorRadius, minorRadius, ellipse.Rotation);
            }
            else
            {
                // 椭圆弧边界计算
                return GetEllipticalArcBounds(ellipse, centerX, centerY, majorRadius, minorRadius);
            }
        }

        /// <summary>
        /// 获取椭圆弧边界
        /// </summary>
        private RectangleF GetEllipticalArcBounds(Ellipse ellipse, float centerX, float centerY, float majorRadius, float minorRadius)
        {
            // DXF中的角度已经是度数
            double startAngle = ellipse.StartAngle;
            double endAngle = ellipse.EndAngle;
            double rotation = ellipse.Rotation;

            // 如果结束角度小于起始角度，说明跨越了0度
            if (endAngle < startAngle)
            {
                endAngle += 360;
            }

            // 计算椭圆弧端点（需要转换为弧度进行三角函数计算）
            double startAngleRad = startAngle * (Math.PI / 180.0);
            double endAngleRad = endAngle * (Math.PI / 180.0);
            var startPoint = GetEllipsePoint(centerX, centerY, majorRadius, minorRadius, startAngleRad, rotation);
            var endPoint = GetEllipsePoint(centerX, centerY, majorRadius, minorRadius, endAngleRad, rotation);

            // 初始边界为端点
            float minX = Math.Min(startPoint.X, endPoint.X);
            float maxX = Math.Max(startPoint.X, endPoint.X);
            float minY = Math.Min(startPoint.Y, endPoint.Y);
            float maxY = Math.Max(startPoint.Y, endPoint.Y);

            // 检查椭圆弧是否跨越关键角度（0°, 90°, 180°, 270°）
            double[] keyAngles = { 0, 90, 180, 270, 360 };

            foreach (double angle in keyAngles)
            {
                if (angle >= startAngle && angle <= endAngle)
                {
                    double angleRad = angle * (Math.PI / 180.0);
                    var point = GetEllipsePoint(centerX, centerY, majorRadius, minorRadius, angleRad, rotation);
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 获取完整椭圆的边界（考虑旋转）
        /// </summary>
        private RectangleF GetFullEllipseBounds(float centerX, float centerY, float majorRadius, float minorRadius, double rotation)
        {
            if (Math.Abs(rotation) < 1e-10)
            {
                // 无旋转的情况
                return new RectangleF(centerX - majorRadius, centerY - minorRadius, majorRadius * 2, minorRadius * 2);
            }

            // 有旋转的情况，计算旋转后的边界
            double rotationRad = rotation * (Math.PI / 180.0);
            double cosRot = Math.Cos(rotationRad);
            double sinRot = Math.Sin(rotationRad);

            // 计算旋转后椭圆的边界
            double ux = majorRadius * cosRot;
            double uy = majorRadius * sinRot;
            double vx = minorRadius * (-sinRot);
            double vy = minorRadius * cosRot;

            double halfWidth = Math.Sqrt(ux * ux + vx * vx);
            double halfHeight = Math.Sqrt(uy * uy + vy * vy);

            return new RectangleF(
                centerX - (float)halfWidth,
                centerY - (float)halfHeight,
                (float)(halfWidth * 2),
                (float)(halfHeight * 2)
            );
        }

        /// <summary>
        /// 获取椭圆上指定角度的点（角度为弧度）
        /// </summary>
        private PointF GetEllipsePoint(float centerX, float centerY, float majorRadius, float minorRadius, double angle, double rotation)
        {
            // 椭圆参数方程
            double x = majorRadius * Math.Cos(angle);
            double y = minorRadius * Math.Sin(angle);

            // 应用旋转（rotation已经是度数，需要转换为弧度）
            double rotationRad = rotation * (Math.PI / 180.0);
            double cosRot = Math.Cos(rotationRad);
            double sinRot = Math.Sin(rotationRad);
            double rotatedX = x * cosRot - y * sinRot;
            double rotatedY = x * sinRot + y * cosRot;

            return new PointF(centerX + (float)rotatedX, centerY + (float)rotatedY);
        }



        /// <summary>
        /// 获取多段线边界
        /// </summary>
        private RectangleF? GetPolylineBounds(Polyline2D polyline)
        {
            if (polyline.Vertexes.Count == 0)
            {
                return null;
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            // 遍历所有顶点，找到边界
            foreach (var vertex in polyline.Vertexes)
            {
                float x = (float)vertex.Position.X;
                float y = (float)vertex.Position.Y;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            // 如果所有点都相同，返回一个小的矩形
            if (minX == maxX && minY == maxY)
            {
                return new RectangleF(minX - 0.5f, minY - 0.5f, 1.0f, 1.0f);
            }

            // 确保宽度和高度至少为正数
            float width = Math.Max(maxX - minX, 0.1f);
            float height = Math.Max(maxY - minY, 0.1f);

            return new RectangleF(minX, minY, width, height);
        }



        /// <summary>
        /// 计算标注边界（简化版本）
        /// </summary>
        private RectangleF CalculateDimensionBounds(Vector2 point1, Vector2 point2, Dimension dim)
        {
            // 获取标注的基本矩形范围
            float minX = Math.Min((float)point1.X, (float)point2.X);
            float minY = Math.Min((float)point1.Y, (float)point2.Y);
            float maxX = Math.Max((float)point1.X, (float)point2.X);
            float maxY = Math.Max((float)point1.Y, (float)point2.Y);

            //// 包含标注线位置
            minX = Math.Min(minX, (float)dim.TextReferencePoint.X);
            minY = Math.Min(minY, (float)dim.TextReferencePoint.Y);
            maxX = Math.Max(maxX, (float)dim.TextReferencePoint.X);
            maxY = Math.Max(maxY, (float)dim.TextReferencePoint.Y);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }


        /// <summary>
        /// 获取标注文字高度
        /// </summary>
        private float GetDimensionTextHeight(Dimension dim)
        {
            // 尝试从标注样式获取文字高度
            if (dim.StyleOverrides.TryGetValue(DimensionStyleOverrideType.TextHeight, out var textHeight))
            {
                if (textHeight.Value is double height)
                {
                    return (float)height;
                }
            }

            // 从标注样式获取
            if (dim.Style?.TextHeight > 0)
            {
                return (float)dim.Style.TextHeight;
            }

            // 默认文字高度
            return 2.5f;
        }

        /// <summary>
        /// 绘制所有实体
        /// </summary>
        private void DrawEntities()
        {
            if (_enableParallelProcessing)
            {
                // 按图层分组处理实体
                var entitiesByLayer = _document.Entities.All
                    .Where(ShouldDrawEntity)
                    .GroupBy(e => e.Layer?.Name ?? "")
                    .ToList();

                // 创建线程安全的图形对象
                var syncRoot = new object();

                // 并行处理每个图层
                Parallel.ForEach(entitiesByLayer, layerGroup =>
                {
                    foreach (var entity in layerGroup)
                    {
                        lock (syncRoot)
                        {
                            DrawDxfEntity(entity);
                        }
                    }
                });
            }
            else
            {
                // 串行处理
                foreach (var entity in _document.Entities.All)
                {
                    if (!ShouldDrawEntity(entity)) continue;

                    DrawDxfEntity(entity);
                }
            }
        }

        /// <summary>
        /// 获取实体颜色
        /// </summary>
        /// <param name="entity">实体</param>
        /// <returns>颜色</returns>
        private Color GetEntityColor(EntityObject entity)
        {
            if (!_options.KeepOriginalColors)
            {
                // 不保留原色时，根据实体类型设置不同颜色
                if (entity is Text || entity is MText)
                {
                    return Color.Black; // 文字为黑色
                }
                else if (entity is Dimension)
                {
                    return Color.DarkBlue; // 标注为深蓝色
                }
                else
                {
                    return Color.Black; // 其他为黑色
                }
            }

            // 保持原色时，获取实体的原始颜色
            if (entity.Color.IsByLayer && entity.Layer != null)
            {
                return ConvertDxfColor(entity.Layer.Color);
            }
            else
            {
                return ConvertDxfColor(entity.Color);
            }
        }

        /// <summary>
        /// 获取或创建画笔
        /// </summary>
        /// <param name="color">颜色</param>
        /// <returns>画笔</returns>
        private Pen GetPen(Color color)
        {
            lock (_penCache)
            {
                if (_penCache.TryGetValue(color, out var pen))
                {
                    return pen;
                }

                pen = new Pen(color, _options.LineWidth);
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                _penCache[color] = pen;
                return pen;
            }
        }

        /// <summary>
        /// 获取或创建画刷
        /// </summary>
        /// <param name="color">颜色</param>
        /// <returns>画刷</returns>
        private SolidBrush GetBrush(Color color)
        {
            lock (_brushCache)
            {
                if (_brushCache.TryGetValue(color, out var brush))
                {
                    return brush;
                }

                brush = new SolidBrush(color);
                _brushCache[color] = brush;
                return brush;
            }
        }

        /// <summary>
        /// 获取或创建字体
        /// </summary>
        /// <param name="size">字体大小</param>
        /// <returns>字体</returns>
        private Font GetFont(float size)
        {
            lock (_fontCache)
            {
                if (_fontCache.TryGetValue(size, out var font))
                {
                    return font;
                }

                font = new Font("Arial", size);
                _fontCache[size] = font;
                return font;
            }
        }

        /// <summary>
        /// 转换DXF颜色到System.Drawing.Color
        /// </summary>
        /// <param name="dxfColor">DXF颜色</param>
        /// <returns>System.Drawing.Color</returns>
        private Color ConvertDxfColor(netDxf.AciColor dxfColor)
        {
            if (dxfColor.IsByLayer || dxfColor.IsByBlock)
            {
                return _options.KeepOriginalColors ? Color.White : Color.Black; // 保持原色时使用白色，否则使用黑色
            }

            // 根据ACI颜色索引转换
            switch (dxfColor.Index)
            {
                case 1: return Color.Red;
                case 2: return Color.Yellow;
                case 3: return Color.Green;
                case 4: return Color.Cyan;
                case 5: return Color.Blue;
                case 6: return Color.Magenta;
                case 7: return Color.White;
                case 8: return Color.Gray;
                case 9: return Color.LightGray;
                default: return Color.Black;
            }
        }



        /// <summary>
        /// 绘制线条
        /// </summary>
        private void DrawLine(Line line, Pen pen)
        {
            var start = TransformPoint(line.StartPoint);
            var end = TransformPoint(line.EndPoint);
            _currentGraphics?.DrawLine(pen, start, end);
        }

        /// <summary>
        /// 绘制圆弧
        /// </summary>
        private void DrawArc(Arc arc, Pen pen)
        {
            var center = TransformPoint(arc.Center);
            float radius = (float)arc.Radius * _scale;
            float startAngle = (float)arc.StartAngle;
            float endAngle = (float)arc.EndAngle;
            float sweepAngle = CalculateSweepAngle(startAngle, endAngle);

            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            _currentGraphics?.DrawArc(pen, rect, startAngle, sweepAngle);
        }

        /// <summary>
        /// 绘制圆
        /// </summary>
        private void DrawCircle(Circle circle, Pen pen)
        {
            var center = TransformPoint(circle.Center);
            float radius = (float)circle.Radius * _scale;
            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            _currentGraphics?.DrawEllipse(pen, rect);
        }

        /// <summary>
        /// 执行带有图形状态保存和恢复的绘制操作
        /// </summary>
        /// <param name="drawAction">绘制操作</param>
        private void ExecuteWithGraphicsState(Action drawAction)
        {
            var state = _currentGraphics?.Save();
            try
            {
                drawAction();
            }
            finally
            {
                _currentGraphics?.Restore(state);
            }
        }

        /// <summary>
        /// 计算角度范围，处理跨越0度的情况
        /// </summary>
        /// <param name="startAngle">起始角度</param>
        /// <param name="endAngle">结束角度</param>
        /// <returns>扫描角度</returns>
        private float CalculateSweepAngle(float startAngle, float endAngle)
        {
            if (endAngle < startAngle)
            {
                endAngle += 360;
            }
            return endAngle - startAngle;
        }



        /// <summary>绘制椭圆</summary>
        private void DrawEllipse(Ellipse ellipse, Pen pen)
        {
            var center = TransformPoint(ellipse.Center);
            // MajorAxis和MinorAxis是直径，需要除以2得到半径
            var majorRadius = ellipse.MajorAxis * _scale / 2;
            var minorRadius = ellipse.MinorAxis * _scale / 2;

            ExecuteWithGraphicsState(() =>
            {
                _currentGraphics?.TranslateTransform((float)center.X, (float)center.Y);

                // 应用椭圆的旋转角度
                if (Math.Abs(ellipse.Rotation) > 1e-10)
                {
                    _currentGraphics?.RotateTransform((float)ellipse.Rotation);
                }

                var rect = new RectangleF((float)-majorRadius, (float)-minorRadius, (float)(majorRadius * 2), (float)(minorRadius * 2));

                // 检查是否为完整椭圆（起始角度等于结束角度）
                if (ellipse.IsFullEllipse)
                {
                    // 绘制完整椭圆
                    _currentGraphics?.DrawEllipse(pen, rect);
                }
                else
                {
                    // 绘制椭圆弧
                    float startAngle = (float)ellipse.StartAngle;
                    float endAngle = (float)ellipse.EndAngle;
                    float sweepAngle = CalculateEllipseSweepAngle(startAngle, endAngle);
                    _currentGraphics?.DrawArc(pen, rect, startAngle, sweepAngle);
                }
            });
        }

        /// <summary>
        /// 计算椭圆弧的扫描角度
        /// </summary>
        /// <param name="startAngle">起始角度</param>
        /// <param name="endAngle">结束角度</param>
        /// <returns>扫描角度</returns>
        private float CalculateEllipseSweepAngle(float startAngle, float endAngle)
        {
            float sweepAngle = endAngle - startAngle;

            // 处理跨越0度的情况
            if (sweepAngle < 0)
            {
                sweepAngle += 360;
            }

            // 如果扫描角度为0，表示完整椭圆
            if (Math.Abs(sweepAngle) < 1e-10)
            {
                sweepAngle = 360;
            }

            return sweepAngle;
        }

        /// <summary>绘制点</summary>
        private void DrawPoint(Point point, Brush brush)
        {
            var p = TransformPoint(point.Position);
            float size = (float)(0.5 * _scale);
            if (size < 2) size = 2;

            _currentGraphics?.FillEllipse(brush, p.X - size / 2, p.Y - size / 2, size, size);
        }

        /// <summary>
        /// 绘制轻量多段线（支持Bulge值的弧形段）
        /// </summary>
        private void DrawPolyline(Polyline2D polyline, Pen pen)
        {
            if (polyline.Vertexes.Count < 2) return;

            var vertices = polyline.Vertexes.ToList();

            // 如果是闭合多段线，需要处理最后一个顶点到第一个顶点的连接
            if (polyline.IsClosed && vertices.Count > 2)
            {
                vertices.Add(vertices[0]); // 添加第一个顶点作为结束点
            }

            // 逐段绘制，处理每个顶点的Bulge值
            for (int i = 0; i < vertices.Count - 1; i++)
            {
                var startVertex = vertices[i];
                var endVertex = vertices[i + 1];

                var startPoint = TransformPoint(startVertex.Position);
                var endPoint = TransformPoint(endVertex.Position);

                // 检查当前顶点的Bulge值
                if (Math.Abs(startVertex.Bulge) < 1e-10)
                {
                    // Bulge为0，绘制直线段
                    _currentGraphics?.DrawLine(pen, startPoint, endPoint);
                }
                else
                {
                    // Bulge不为0，绘制弧形段
                    DrawBulgeArc(pen, startPoint, endPoint, startVertex.Bulge);
                }
            }
        }

        /// <summary>
        /// 根据Bulge值绘制弧形段
        /// </summary>
        /// <param name="pen">画笔</param>
        /// <param name="startPoint">起始点</param>
        /// <param name="endPoint">结束点</param>
        /// <param name="bulge">膨出值</param>
        private void DrawBulgeArc(Pen pen, PointF startPoint, PointF endPoint, double bulge)
        {
            // 计算弧的几何参数
            double deltaX = endPoint.X - startPoint.X;
            double deltaY = endPoint.Y - startPoint.Y;
            double chordLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            if (chordLength < 1e-10) return; // 避免除零错误

            // 计算弧的半径
            double radius = chordLength * (1 + bulge * bulge) / (4 * Math.Abs(bulge));

            // 计算弦的中点
            double midX = (startPoint.X + endPoint.X) / 2;
            double midY = (startPoint.Y + endPoint.Y) / 2;

            // 计算弦的垂直方向
            double perpX = -deltaY / chordLength;
            double perpY = deltaX / chordLength;

            // 计算弧心到弦中点的距离
            double sagitta = Math.Abs(bulge) * chordLength / 2;
            double centerDistance = radius - sagitta;

            // 计算弧心坐标
            double centerX, centerY;
            if (bulge > 0)
            {
                // 逆时针弧
                centerX = midX + centerDistance * perpX;
                centerY = midY + centerDistance * perpY;
            }
            else
            {
                // 顺时针弧
                centerX = midX - centerDistance * perpX;
                centerY = midY - centerDistance * perpY;
            }

            // 计算起始角度和扫描角度
            double startAngle = Math.Atan2(startPoint.Y - centerY, startPoint.X - centerX) * 180 / Math.PI;
            double endAngle = Math.Atan2(endPoint.Y - centerY, endPoint.X - centerX) * 180 / Math.PI;

            double sweepAngle;
            if (bulge > 0)
            {
                // 逆时针
                sweepAngle = endAngle - startAngle;
                if (sweepAngle <= 0) sweepAngle += 360;
            }
            else
            {
                // 顺时针
                sweepAngle = startAngle - endAngle;
                if (sweepAngle <= 0) sweepAngle += 360;
                sweepAngle = -sweepAngle;
            }

            // 创建包围矩形
            float rectSize = (float)(radius * 2);
            RectangleF rect = new RectangleF(
                (float)(centerX - radius),
                (float)(centerY - radius),
                rectSize,
                rectSize
            );

            // 绘制弧
            _currentGraphics?.DrawArc(pen, rect, (float)startAngle, (float)sweepAngle);
        }

        /// <summary>
        /// 创建文本格式对象
        /// </summary>
        /// <param name="horizontalAlignment">水平对齐方式</param>
        /// <param name="verticalAlignment">垂直对齐方式</param>
        /// <returns>文本格式对象</returns>
        private StringFormat CreateTextFormat(StringAlignment horizontalAlignment = StringAlignment.Near, StringAlignment verticalAlignment = StringAlignment.Near)
        {
            var format = new StringFormat();
            format.Alignment = horizontalAlignment;
            format.LineAlignment = verticalAlignment;
            format.Trimming = StringTrimming.Word;
            format.FormatFlags = StringFormatFlags.NoClip;
            return format;
        }

        private string CleanText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // 替换换行符
                text = text.Replace("\\P", "\n");
                text = text.Replace("^J", "\n");
                text = text.Replace("^M", "\n");
                // 移除其他常见的DXF转义符
                text = text.Replace("\\~", ""); // 不间断空格
                text = text.Replace("\\\\", "\\"); // 双反斜杠转为单反斜杠
                text = text.Replace("\\{", "{"); // 左大括号
                text = text.Replace("\\}", "}"); // 右大括号
            }
            return text;
        }

        /// <summary>
        /// 计算文本高度，考虑换行
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="font">字体</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <param name="isRotated">是否旋转文本</param>
        /// <returns>文本高度</returns>
        private float CalculateTextHeight(string text, Font font, float maxWidth, bool isRotated = false)
        {
            if (_currentGraphics != null)
            {
                SizeF textSize = _currentGraphics.MeasureString(text, font);
                if (maxWidth <= 0 || textSize.Width <= maxWidth) return textSize.Height;

                // 计算换行后的高度
                float divisor = isRotated ? maxWidth : (maxWidth + 12);
                float lineCount = (float)Math.Ceiling(textSize.Width / divisor);
                return textSize.Height * lineCount;
            }
            return 0;
        }

        /// <summary>
        /// 绘制文本（带或不带最大宽度限制）
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="font">字体</param>
        /// <param name="brush">画刷</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="format">文本格式</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <param name="isRotated">是否旋转文本</param>
        private void DrawTextWithConstraints(string text, Font font, Brush brush, float x, float y, StringFormat format, float maxWidth = 0, bool isRotated = false)
        {
            if (maxWidth > 0)
            {
                float textHeight = CalculateTextHeight(text, font, maxWidth, isRotated);
                _currentGraphics?.DrawString(text, font, brush, new RectangleF(x, y, maxWidth, textHeight), format);
            }
            else
            {
                _currentGraphics?.DrawString(text, font, brush, x, y, format);
            }
        }

        /// <summary>
        /// 绘制旋转文本
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="font">字体</param>
        /// <param name="brush">画刷</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转角度</param>
        /// <param name="format">文本格式</param>
        /// <param name="maxWidth">最大宽度（可选）</param>
        /// <param name="offset">文本偏移量（负数为左/下，正数为右/上）</param>
        private void DrawRotatedText(string text, Font font, Brush brush, PointF position, float rotation, StringFormat format, float maxWidth = 0, PointF offset = default)
        {
            // 清理文本中的转义符
            text = CleanText(text);

            // 计算最终文本位置
            float textX = position.X + offset.X;
            float textY = _options.Height - position.Y - offset.Y;

            ExecuteWithGraphicsState(() =>
            {
                _currentGraphics?.ResetTransform();

                if (Math.Abs(rotation) > 0.001)
                {
                    _currentGraphics?.TranslateTransform(textX, textY);
                    _currentGraphics?.RotateTransform(rotation);
                    DrawTextWithConstraints(text, font, brush, 0, 0, format, maxWidth, true);
                }
                else
                {
                    DrawTextWithConstraints(text, font, brush, textX, textY, format, maxWidth, false);
                }
            });
        }

        /// <summary>
        /// 验证文本绘制条件
        /// </summary>
        /// <param name="textValue">文本内容</param>
        /// <returns>是否可以绘制</returns>
        private bool CanDrawText(string textValue)
        {
            return _options.DrawText && !string.IsNullOrEmpty(textValue);
        }

        /// <summary>
        /// 计算并获取字体，确保最小可见性
        /// </summary>
        /// <param name="textHeight">文本高度</param>
        /// <param name="minFontSize">最小字体大小</param>
        /// <returns>字体对象</returns>
        private Font CalculateAndGetFont(double textHeight, float minFontSize = 6.0f)
        {
            float fontSize = (float)textHeight * _scale;
            if (fontSize < minFontSize) fontSize = minFontSize;
            if (fontSize > 22) fontSize = 22;
            return GetFont(fontSize);
        }

        /// <summary>
        /// 获取标注专用字体
        /// </summary>
        /// <returns>标注字体对象</returns>
        private Font GetDimensionFont(DimensionStyle style)
        {
            float fontSize = (float)(style.TextHeight * style.DimScaleOverall) * _scale;
            if (fontSize < 6) fontSize = 6;
            if (fontSize > 16) fontSize = 16;
            return GetFont(fontSize);
        }

        /// <summary>
        /// 绘制延长线起始点的红色圆形标记
        /// </summary>
        /// <param name="point1">第一个标记点</param>
        /// <param name="point2">第二个标记点</param>
        private void DrawExtensionLineMarkers(PointF point1, PointF point2)
        {
            using (var redBrush = new SolidBrush(Color.Red))
            {
                _currentGraphics?.FillEllipse(redBrush, point1.X - 2, point1.Y - 2, 4, 4);
                _currentGraphics?.FillEllipse(redBrush, point2.X - 2, point2.Y - 2, 4, 4);
            }
        }

        /// <summary>绘制文字</summary>
        private void DrawText(Text text, Brush brush)
        {
            if (!CanDrawText(text.Value)) return;

            var pos = TransformPoint(new Vector3(text.Position.X, text.Position.Y + text.Height, 0));
            var font = CalculateAndGetFont(text.Height);
            var format = CreateTextFormat();

            DrawRotatedText(text.Value, font, brush, pos, (float)text.Rotation, format);
        }

        /// <summary>绘制多行文字</summary>
        private void DrawMText(MText mtext, Brush brush)
        {
            if (!CanDrawText(mtext.Value)) return;

            var pos = TransformPoint(mtext.Position);
            var font = CalculateAndGetFont(mtext.Height);

            float maxWidth = (float)((mtext.RectangleWidth > 0 ? (float)mtext.RectangleWidth * _scale : 0) + (mtext.Height * _scale));
            var format = CreateTextFormat();

            DrawRotatedText(mtext.Value, font, brush, pos, (float)mtext.Rotation, format, maxWidth);
        }

        /// <summary>绘制箭头</summary>
        private void DrawArrow(PointF point, float angle, Pen pen)
        {
            float arrowLength = pen.Width + 2;

            PointF arrowTip = new PointF(
                point.X - (float)Math.Cos(angle),
                point.Y - (float)Math.Sin(angle)
            );

            // 计算箭头的两个底边端点 - 使用更小的角度让箭头更细长
            float arrowAngle = (float)(Math.PI / 8);
            float x1 = arrowTip.X + arrowLength * (float)Math.Cos(angle + Math.PI - arrowAngle); // 箭头左边
            float y1 = arrowTip.Y + arrowLength * (float)Math.Sin(angle + Math.PI - arrowAngle);
            float x2 = arrowTip.X + arrowLength * (float)Math.Cos(angle + Math.PI + arrowAngle); // 箭头右边
            float y2 = arrowTip.Y + arrowLength * (float)Math.Sin(angle + Math.PI + arrowAngle);

            // 创建箭头的填充区域
            PointF[] arrowPoints = new PointF[] {
                arrowTip, // 箭头尖端
                new PointF(x1, y1),
                new PointF(x2, y2)
            };

            // 绘制填充箭头
            var arrowBrush = GetBrush(pen.Color);
            _currentGraphics?.FillPolygon(arrowBrush, arrowPoints);

            // 绘制箭头轮廓
            using (var arrowPen = new Pen(pen.Color, pen.Width * 1.8f)) // 进一步加粗箭头线条
            {
                _currentGraphics?.DrawPolygon(arrowPen, arrowPoints);
            }
        }

        /// <summary>
        /// 获取标注文本，包括前缀和后缀
        /// </summary>
        /// <param name="dimension">标注对象</param>
        /// <param name="measurementValue">测量值</param>
        /// <param name="prefix">前缀（如R表示半径）</param>
        /// <returns>格式化后的标注文本</returns>
        private string GetDimensionText(Dimension dimension, double measurementValue, string prefix = "")
        {

            if (!string.IsNullOrEmpty(dimension.UserText.Trim()))
            {
                return dimension.UserText;
            }

            // 格式化测量值
            string dimText = measurementValue.ToString("0.##");

            // 添加前缀（如R表示半径）
            if (!string.IsNullOrEmpty(prefix))
            {
                dimText = prefix + dimText;
            }

            // 获取样式中的后缀
            string? suffix = dimension.Style?.DimSuffix;
            if (dimension.StyleOverrides != null)
            {
                // 如果Style中没有后缀，尝试从StyleOverrides中获取
                if (dimension.StyleOverrides.TryGetValue(DimensionStyleOverrideType.DimSuffix, out var suffixOverride) && suffixOverride.Value != null)
                {
                    suffix = suffixOverride.Value.ToString();
                }
            }

            // 获取样式中的前缀
            string? stylePrefix = dimension.Style?.DimPrefix;
            if (dimension.StyleOverrides != null)
            {
                // 如果Style中没有前缀，尝试从StyleOverrides中获取
                if (dimension.StyleOverrides.TryGetValue(DimensionStyleOverrideType.DimPrefix, out var prefixOverride) && prefixOverride.Value != null)
                {
                    stylePrefix = prefixOverride.Value.ToString();
                }
            }

            // 添加样式前缀
            if (!string.IsNullOrEmpty(stylePrefix))
            {
                dimText = stylePrefix + dimText;
            }

            // 添加后缀
            if (!string.IsNullOrEmpty(suffix))
            {
                dimText = dimText + suffix;
            }

            dimText = CleanText(dimText);

            return dimText;
        }

        /// <summary>绘制线性标注，包括尺寸线、延伸线、箭头和文字</summary>
        private void DrawLinearDimension(LinearDimension dimension, Pen pen, Brush brush)
        {
            if (!_options.DrawDimensions) return;

            // 绘制尺寸线
            var start = TransformPoint(dimension.FirstReferencePoint);
            var end = TransformPoint(dimension.SecondReferencePoint);
            var dimLineStart = TransformPoint(dimension.TextReferencePoint);

            // 计算延伸线终点
            var extLine1Start = start;
            var extLine2Start = end;
            var extLine1End = new PointF();
            var extLine2End = new PointF();

            // 尺寸线的起点和终点
            PointF dimLineStartPoint = new PointF();
            PointF dimLineEndPoint = new PointF();

            if (Math.Abs(dimension.Rotation) < 0.001 || Math.Abs(dimension.Rotation - 180) < 0.001) // 水平标注
            {
                extLine1End = new PointF(extLine1Start.X, dimLineStart.Y);
                extLine2End = new PointF(extLine2Start.X, dimLineStart.Y);
                _currentGraphics?.DrawLine(pen, extLine1Start, extLine1End);
                _currentGraphics?.DrawLine(pen, extLine2Start, extLine2End);

                // 在延长线起始点绘制红色圆形标记
                DrawExtensionLineMarkers(extLine1Start, extLine2Start);

                // 绘制尺寸线
                dimLineStartPoint = new PointF(extLine1End.X, dimLineStart.Y);
                dimLineEndPoint = new PointF(extLine2End.X, dimLineStart.Y);
                _currentGraphics?.DrawLine(pen, dimLineStartPoint, dimLineEndPoint);

                // 绘制箭头 - 箭头从端点指向标注线内侧
                // 判断左右端点，箭头指向标注线中心（对调方向）
                if (dimLineStartPoint.X < dimLineEndPoint.X)
                {
                    // dimLineStartPoint在左，dimLineEndPoint在右
                    DrawArrow(dimLineStartPoint, (float)Math.PI, pen); // 左端箭头指向左侧
                    DrawArrow(dimLineEndPoint, 0, pen); // 右端箭头指向右侧
                }
                else
                {
                    // dimLineStartPoint在右，dimLineEndPoint在左
                    DrawArrow(dimLineStartPoint, 0, pen); // 右端箭头指向右侧
                    DrawArrow(dimLineEndPoint, (float)Math.PI, pen); // 左端箭头指向左侧
                }
            }
            else if (Math.Abs(dimension.Rotation - 90) < 0.001 || Math.Abs(dimension.Rotation - 270) < 0.001) // 垂直标注
            {
                extLine1End = new PointF(dimLineStart.X, extLine1Start.Y);
                extLine2End = new PointF(dimLineStart.X, extLine2Start.Y);
                _currentGraphics?.DrawLine(pen, extLine1Start, extLine1End);
                _currentGraphics?.DrawLine(pen, extLine2Start, extLine2End);

                // 在延长线起始点绘制红色圆形标记
                DrawExtensionLineMarkers(extLine1Start, extLine2Start);

                // 绘制尺寸线
                dimLineStartPoint = new PointF(dimLineStart.X, extLine1End.Y);
                dimLineEndPoint = new PointF(dimLineStart.X, extLine2End.Y);
                _currentGraphics?.DrawLine(pen, dimLineStartPoint, dimLineEndPoint);

                // 绘制箭头 - 箭头从端点指向标注线内侧
                // 判断上下端点，箭头指向标注线中心（对调方向）
                if (dimLineStartPoint.Y < dimLineEndPoint.Y)
                {
                    // dimLineStartPoint在上，dimLineEndPoint在下
                    DrawArrow(dimLineStartPoint, (float)(Math.PI * 3 / 2), pen); // 上端箭头指向上侧
                    DrawArrow(dimLineEndPoint, (float)Math.PI / 2, pen); // 下端箭头指向下侧
                }
                else
                {
                    // dimLineStartPoint在下，dimLineEndPoint在上
                    DrawArrow(dimLineStartPoint, (float)Math.PI / 2, pen); // 下端箭头指向下侧
                    DrawArrow(dimLineEndPoint, (float)(Math.PI * 3 / 2), pen); // 上端箭头指向上侧
                }
            }

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);

            // 获取完整的标注文本，包括前缀和后缀
            double measureValue;
            if (Math.Abs(dimension.Rotation) < 0.001 || Math.Abs(dimension.Rotation - 180) < 0.001) // 水平标注
            {
                measureValue = Math.Abs(dimension.SecondReferencePoint.X - dimension.FirstReferencePoint.X);
            }
            else // 垂直标注
            {
                measureValue = Math.Abs(dimension.SecondReferencePoint.Y - dimension.FirstReferencePoint.Y);
            }

            string dimText = GetDimensionText(dimension, measureValue);
            var textSize = _currentGraphics?.MeasureString(dimText, font);

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 计算文本位置 - 文本应该位于标注线上，offset已经体现在标注线位置中
            var textPos = new PointF();
            float textRotation = 0;

            if (Math.Abs(dimension.Rotation) < 0.001 || Math.Abs(dimension.Rotation - 180) < 0.001) // 水平标注
            {
                textPos = new PointF(
                    (extLine1End.X + extLine2End.X) / 2,
                    dimLineStart.Y);
                textRotation = 0;
            }
            else if (Math.Abs(dimension.Rotation - 90) < 0.001 || Math.Abs(dimension.Rotation - 270) < 0.001) // 垂直标注
            {
                textPos = new PointF(
                    dimLineStart.X,
                    (extLine1End.Y + extLine2End.Y) / 2);
                textRotation = -90;
            }

            // 绘制旋转文本 - 不需要额外偏移，因为offset已经体现在标注线位置中
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, new PointF(0, 0));
        }

        /// <summary>绘制对齐标注，包括尺寸线、延伸线、箭头和文字</summary>
        private void DrawAlignedDimension(AlignedDimension dimension, Pen pen, Brush brush)
        {
            if (!_options.DrawDimensions) return;

            // 绘制尺寸线
            var start = TransformPoint(dimension.FirstReferencePoint);
            var end = TransformPoint(dimension.SecondReferencePoint);
            var dimLinePos = TransformPoint(dimension.DimLinePosition);

            // 计算对齐方向
            var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            // 计算从起点到标注线位置的向量
            var dimVectorX = dimLinePos.X - start.X;
            var dimVectorY = dimLinePos.Y - start.Y;

            // 对于对齐标注，应该沿着被标注线段的方向绘制
            // 不强制转换为水平或垂直，而是保持原有的角度

            // 计算标注线的方向向量（从起点到标注线位置的垂直向量）
            var dimLineVector = new PointF(dimLinePos.X - start.X, dimLinePos.Y - start.Y);
            var segmentVector = new PointF(end.X - start.X, end.Y - start.Y);

            // 计算垂直于线段的单位向量
            var segmentLength = Math.Sqrt(segmentVector.X * segmentVector.X + segmentVector.Y * segmentVector.Y);
            var perpX = (float)(-segmentVector.Y / segmentLength);
            var perpY = (float)(segmentVector.X / segmentLength);

            // 计算标注线到被标注线段的距离
            var dotProduct = dimLineVector.X * perpX + dimLineVector.Y * perpY;
            var dimLineDistance = Math.Abs(dotProduct);

            // 计算标注线上的两个端点
            var extLine1End = new PointF(
                start.X + perpX * dotProduct,
                start.Y + perpY * dotProduct);
            var extLine2End = new PointF(
                end.X + perpX * dotProduct,
                end.Y + perpY * dotProduct);

            // 绘制延伸线
            float extLineOffset = 2 * _scale;
            var extLine1Start = start;
            var extLine2Start = end;

            // 延伸线应该超出标注线一点
            var extLine1ExtEnd = new PointF(
                extLine1End.X + perpX * (dotProduct > 0 ? extLineOffset : -extLineOffset),
                extLine1End.Y + perpY * (dotProduct > 0 ? extLineOffset : -extLineOffset));
            var extLine2ExtEnd = new PointF(
                extLine2End.X + perpX * (dotProduct > 0 ? extLineOffset : -extLineOffset),
                extLine2End.Y + perpY * (dotProduct > 0 ? extLineOffset : -extLineOffset));

            _currentGraphics?.DrawLine(pen, extLine1Start, extLine1ExtEnd);
            _currentGraphics?.DrawLine(pen, extLine2Start, extLine2ExtEnd);

            // 在延长线起始点绘制红色圆形标记
            DrawExtensionLineMarkers(extLine1Start, extLine2Start);

            // 延伸线端点已在上面计算完成

            // 绘制尺寸线
            _currentGraphics?.DrawLine(pen, extLine1End, extLine2End);

            // 计算箭头角度
            float arrowAngle = (float)Math.Atan2(extLine2End.Y - extLine1End.Y, extLine2End.X - extLine1End.X);

            // 绘制箭头 - 箭头从端点指向标注线内侧（对调方向）
            // 起点箭头指向起点方向，终点箭头指向终点方向
            DrawArrow(extLine1End, (float)(arrowAngle + Math.PI), pen); // 起点箭头指向外侧
            DrawArrow(extLine2End, arrowAngle, pen); // 终点箭头指向外侧

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);

            // 获取完整的标注文本，包括前缀和后缀
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue);

            var textSize = _currentGraphics?.MeasureString(dimText, font);

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 计算文字位置 - 在尺寸线中心，offset已经体现在标注线位置中
            var textPos = new PointF(
                (extLine1End.X + extLine2End.X) / 2,
                (extLine1End.Y + extLine2End.Y) / 2);

            // 判断是否需要旋转文字
            // 如果角度接近垂直（接近90度或270度），则旋转文字
            bool isVertical = Math.Abs(Math.Sin(angle)) > 0.7; // sin值大于0.7表示角度接近垂直
            float textRotation = isVertical ? -90 : 0;

            // 绘制旋转文本 - 不需要额外偏移，因为offset已经体现在标注线位置中
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, new PointF(0, 0));
        }

        /// <summary>绘制径向标注，包括半径线、箭头和文字</summary>
        private void DrawRadialDimension(RadialDimension dimension, Pen pen, Brush brush)
        {
            if (!_options.DrawDimensions) return;

            var center = TransformPoint(dimension.CenterPoint);
            var textPoint = TransformPoint(dimension.TextReferencePoint);

            // 计算半径方向
            var angle = Math.Atan2(textPoint.Y - center.Y, textPoint.X - center.X);
            var radius = (float)dimension.Measurement * _scale;

            // 计算圆上的点
            var circlePoint = new PointF(
                center.X + (float)(radius * Math.Cos(angle)),
                center.Y + (float)(radius * Math.Sin(angle)));

            // 绘制标注线
            _currentGraphics?.DrawLine(pen, center, textPoint);

            // 绘制箭头 - 确保箭头指向圆心
            DrawArrow(textPoint, (float)angle, pen); // 箭头指向圆心

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);

            // 获取完整的标注文本，包括前缀和后缀
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue, "R");

            SizeF? textSize = _currentGraphics?.MeasureString(dimText, font);

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 计算文本位置
            float textOffset = 3 * _scale; // 文本偏移量
            if (textOffset > 6) textOffset = 6; // 限制最大偏移量
            var textPos = new PointF(textPoint.X, textPoint.Y);

            // 计算标注线长度
            float lineLength = (float)Math.Sqrt(
                Math.Pow(textPoint.X - center.X, 2) +
                Math.Pow(textPoint.Y - center.Y, 2));

            // 检查文本字符数是否少于6个字
            bool isShortText = dimText.Length <= 6;

            // 检查文本大小是否需要偏移
            if (!isShortText && (textSize?.Width > lineLength * 0.3 || textSize?.Height > lineLength * 0.3))
            {
                // 文本较长且较大，需要偏移到标注线外侧
                float offsetX = (float)Math.Cos(angle) * textOffset;
                float offsetY = (float)Math.Sin(angle) * textOffset;
                textPos.X += offsetX;
                textPos.Y += offsetY;
            }

            // 判断是否需要旋转文字
            // 如果角度接近垂直（接近90度或270度），则旋转文字
            bool isVertical = Math.Abs(Math.Sin(angle)) > 0.7; // sin值大于0.7表示角度接近垂直
            float textRotation = isVertical ? -90 : 0;

            // 计算文本偏移量（RadialDimension没有Offset属性）
            float offsetValue = 3 * _scale;
            if (offsetValue > 6) offsetValue = 6;
            PointF textOffset1 = new PointF(0, offsetValue); // 默认向上偏移

            // 绘制旋转文本
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, textOffset1);
        }

        /// <summary>绘制直径标注，包括直径线、箭头和文字</summary>
        private void DrawDiametricDimension(DiametricDimension dimension, Pen pen, Brush brush)
        {
            // 检查是否需要绘制标注
            if (!_options.DrawDimensions)
                return;

            var center = TransformPoint(dimension.CenterPoint);
            var textPoint = TransformPoint(dimension.TextReferencePoint);

            // 计算直径方向
            var angle = Math.Atan2(textPoint.Y - center.Y, textPoint.X - center.X);
            var radius = (float)dimension.Measurement / 2 * _scale; // 直径的一半是半径

            // 计算圆上的两个点
            var point1 = new PointF(
                center.X + (float)(radius * Math.Cos(angle)),
                center.Y + (float)(radius * Math.Sin(angle)));
            var point2 = new PointF(
                center.X - (float)(radius * Math.Cos(angle)),
                center.Y - (float)(radius * Math.Sin(angle)));

            // 绘制标注线（直径线）
            _currentGraphics?.DrawLine(pen, point1, point2);

            // 计算文字到圆周的距离
            var distanceToText = Math.Sqrt(Math.Pow(textPoint.X - center.X, 2) + Math.Pow(textPoint.Y - center.Y, 2));

            // 如果文字不在圆周上，绘制引线连接到文字
            if (distanceToText > radius * 1.1) // 允许一定的误差范围
            {
                // 计算圆周上最接近文字的点
                var leaderStartPoint = new PointF(
                    center.X + (float)(radius * Math.Cos(angle)),
                    center.Y + (float)(radius * Math.Sin(angle)));

                // 绘制从圆周到文字的引线
                _currentGraphics?.DrawLine(pen, leaderStartPoint, textPoint);
            }

            // 绘制箭头 - 确保箭头方向正确
            DrawArrow(point1, (float)angle, pen); // 指向圆心
            DrawArrow(point2, (float)(angle + Math.PI), pen); // 指向圆心

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);

            // 获取完整的标注文本，包括前缀和后缀
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue, "Ø");

            SizeF? textSize = _currentGraphics?.MeasureString(dimText, font);

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 计算文本位置
            float textOffset = 3 * _scale; // 文本偏移量
            if (textOffset > 6) textOffset = 6; // 限制最大偏移量
            var textPos = new PointF(textPoint.X, textPoint.Y);

            // 计算标注线长度
            float lineLength = (float)Math.Sqrt(
                Math.Pow(textPoint.X - center.X, 2) +
                Math.Pow(textPoint.Y - center.Y, 2));

            // 检查文本字符数是否少于6个字
            bool isShortText = dimText.Length <= 6;

            // 检查文本大小是否需要偏移
            if (!isShortText && (textSize?.Width > lineLength * 0.3 || textSize?.Height > lineLength * 0.3))
            {
                // 文本较长且较大，需要偏移到标注线外侧
                float offsetX = (float)Math.Cos(angle) * textOffset;
                float offsetY = (float)Math.Sin(angle) * textOffset;
                textPos.X += offsetX;
                textPos.Y += offsetY;
            }

            // 判断是否需要旋转文字
            // 如果角度接近垂直（接近90度或270度），则旋转文字
            bool isVertical = Math.Abs(Math.Sin(angle)) > 0.7; // sin值大于0.7表示角度接近垂直
            float textRotation = isVertical ? -90 : 0;

            // 计算文本偏移量（RadialDimension没有Offset属性）
            float offsetValue = 3 * _scale;
            if (offsetValue > 6) offsetValue = 6;
            PointF textOffset1 = new PointF(0, offsetValue); // 默认向上偏移

            // 绘制旋转文本
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, textOffset1);
        }

        /// <summary>绘制角度标注，包括弧线、延伸线、箭头和文字</summary>
        private void DrawAngular2LineDimension(Angular2LineDimension dimension, Pen pen, Brush brush)
        {
            // 检查是否需要绘制标注
            if (!_options.DrawDimensions)
                return;

            var start1 = TransformPoint(dimension.StartFirstLine);
            var end1 = TransformPoint(dimension.EndFirstLine);
            var start2 = TransformPoint(dimension.StartSecondLine);
            var end2 = TransformPoint(dimension.EndSecondLine);
            var arcCenter = TransformPoint(dimension.CenterPoint);
            var textPoint = TransformPoint(dimension.TextReferencePoint);

            // 计算两条线从圆心出发的方向角度
            var angle1 = Math.Atan2(end1.Y - arcCenter.Y, end1.X - arcCenter.X);
            var angle2 = Math.Atan2(end2.Y - arcCenter.Y, end2.X - arcCenter.X);

            // 计算文本点相对于圆心的角度和半径
            var textAngle = Math.Atan2(textPoint.Y - arcCenter.Y, textPoint.X - arcCenter.X);
            var radius = (float)Math.Sqrt(Math.Pow(textPoint.X - arcCenter.X, 2) + Math.Pow(textPoint.Y - arcCenter.Y, 2));

            // 标准化角度到0-2π范围
            while (angle1 < 0) angle1 += 2 * Math.PI;
            while (angle2 < 0) angle2 += 2 * Math.PI;
            while (textAngle < 0) textAngle += 2 * Math.PI;

            // 计算角度差
            double angleDiff = Math.Abs(angle2 - angle1);
            if (angleDiff > Math.PI)
            {
                angleDiff = 2 * Math.PI - angleDiff;
            }

            // 确定弧线的起始和结束角度
            double startAngle, endAngle, sweepAngle;

            // 简化逻辑：直接使用两个角度的较小值作为起始角度
            if (Math.Abs(angle2 - angle1) <= Math.PI)
            {
                // 小角度情况
                startAngle = Math.Min(angle1, angle2);
                endAngle = Math.Max(angle1, angle2);
                sweepAngle = endAngle - startAngle;
            }
            else
            {
                // 大角度情况，跨越0度线
                startAngle = Math.Max(angle1, angle2);
                endAngle = Math.Min(angle1, angle2) + 2 * Math.PI;
                sweepAngle = endAngle - startAngle;
            }

            // 绘制弧线
            var rect = new RectangleF(arcCenter.X - radius, arcCenter.Y - radius, radius * 2, radius * 2);
            var startAngleDeg = (float)(startAngle * 180 / Math.PI);
            var sweepAngleDeg = (float)(sweepAngle * 180 / Math.PI);

            _currentGraphics?.DrawArc(pen, rect, startAngleDeg, sweepAngleDeg);

            // 计算延伸线的起点和终点
            // 延伸线应该从两条线的交点（圆心）延伸到弧线位置
            var extLineLength = radius + 10 * _scale; // 延伸线长度稍微超过弧线

            // 第一条延伸线
            var extLine1Start = arcCenter;
            var extLine1End = new PointF(
                arcCenter.X + (float)(extLineLength * Math.Cos(startAngle)),
                arcCenter.Y + (float)(extLineLength * Math.Sin(startAngle)));

            // 第二条延伸线
            var extLine2Start = arcCenter;
            var extLine2End = new PointF(
                arcCenter.X + (float)(extLineLength * Math.Cos(endAngle)),
                arcCenter.Y + (float)(extLineLength * Math.Sin(endAngle)));

            // 绘制延伸线
            _currentGraphics?.DrawLine(pen, extLine1Start, extLine1End);
            _currentGraphics?.DrawLine(pen, extLine2Start, extLine2End);

            // 计算弧上的箭头位置
            var arcPoint1 = new PointF(
                arcCenter.X + (float)(radius * Math.Cos(startAngle)),
                arcCenter.Y + (float)(radius * Math.Sin(startAngle)));
            var arcPoint2 = new PointF(
                arcCenter.X + (float)(radius * Math.Cos(endAngle)),
                arcCenter.Y + (float)(radius * Math.Sin(endAngle)));

            // 绘制箭头 - 箭头方向沿着弧线切线方向
            DrawArrow(arcPoint1, (float)(startAngle + Math.PI / 2), pen); // 第一个箭头
            DrawArrow(arcPoint2, (float)(endAngle - Math.PI / 2), pen); // 第二个箭头

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue, "");
            SizeF? textSize = _currentGraphics?.MeasureString(dimText, font);

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 计算文本位置和旋转
            var textPos = new PointF(textPoint.X, textPoint.Y);
            float textOffset = 5 * _scale;
            if (textOffset > 8) textOffset = 8;

            // 计算弧长
            float arcLength = (float)(radius * sweepAngle);
            bool isShortText = dimText.Length <= 6;

            // 如果文本太长，将其偏移到弧线外侧
            if (!isShortText || textSize?.Width > arcLength * 0.6)
            {
                // 计算中间角度作为偏移方向
                double midAngle = (startAngle + endAngle) / 2;
                float offsetX = (float)Math.Cos(midAngle) * textOffset;
                float offsetY = (float)Math.Sin(midAngle) * textOffset;
                textPos.X += offsetX;
                textPos.Y += offsetY;
            }

            // 计算文本旋转角度
            double midAngle1 = (startAngle + endAngle) / 2;
            bool isVertical = Math.Abs(Math.Sin(midAngle1)) > 0.7;
            float textRotation = isVertical ? -90 : 0;

            // 绘制旋转文本 - 不需要额外偏移，因为offset已经体现在标注线位置中
            DrawRotatedText(dimText + "°", font, brush, textPos, textRotation, format, 0, new PointF(0, 0));
        }


        /// <summary>绘制坐标标注，包括引线和文字</summary>
        private void DrawOrdinateDimension(OrdinateDimension dimension, Pen pen, Brush brush)
        {
            // 检查是否需要绘制标注
            if (!_options.DrawDimensions)
                return;

            var origin = TransformPoint(dimension.Origin);
            var feature = TransformPoint(dimension.FeaturePoint);
            var leader = TransformPoint(dimension.TextReferencePoint);

            // 绘制引线
            if (dimension.Rotation == 0) // X方向标注
            {
                _currentGraphics?.DrawLine(pen, feature, new PointF(feature.X, leader.Y));
                _currentGraphics?.DrawLine(pen, new PointF(feature.X, leader.Y), leader);
            }
            else // Y方向标注
            {
                _currentGraphics?.DrawLine(pen, feature, new PointF(leader.X, feature.Y));
                _currentGraphics?.DrawLine(pen, new PointF(leader.X, feature.Y), leader);
            }

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);

            // 获取完整的标注文本，包括前缀和后缀
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue, "");

            var textSize = _currentGraphics?.MeasureString(dimText, font);

            // 计算文本位置
            var textPos = new PointF(leader.X, leader.Y);
            float textOffset = 3 * _scale; // 文本偏移量
            if (textOffset > 6) textOffset = 6; // 限制最大偏移量


            // 创建文本格式
            StringFormat format;
            float textRotation = 0;

            if (dimension.Rotation == 0) // X方向标注
            {
                // 水平标注，文本放在引线终点右侧
                textPos.X += textOffset;
                format = CreateTextFormat(StringAlignment.Near, StringAlignment.Center);
            }
            else // Y方向标注
            {
                // 垂直标注，文本放在引线终点上方
                textPos.Y -= textOffset;
                format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);
                textRotation = -90; // 垂直文本需要旋转
            }

            // 计算文本偏移量（OrdinateDimension没有Offset属性）
            float offsetValue = 3 * _scale;
            if (offsetValue > 6) offsetValue = 6;
            PointF textOffsetPoint = new PointF(0, offsetValue); // 默认向上偏移

            // 绘制旋转文本
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, textOffsetPoint);
        }

        /// <summary>绘制弧长标注，包括弧线、延伸线、箭头和文字</summary>
        private void DrawArcLengthDimension(ArcLengthDimension dimension, Pen pen, Brush brush)
        {
            if (!_options.DrawDimensions) return;

            // 获取弧的基本参数
            var center = TransformPoint(dimension.CenterPoint);
            var radius = (float)(dimension.Radius * _scale);
            var startAngle = (float)dimension.StartAngle;
            var endAngle = (float)dimension.EndAngle;
            var offset = (float)(dimension.Offset * _scale);

            // 计算标注弧的半径（Offset是弧心到标注线的距离）
            var dimRadius = Math.Abs(offset);
            if (dimRadius <= 0) dimRadius = radius * 1.2f; // 如果offset为零，使用默认偏移

            // 计算弧的起始和结束点
            var startRad = startAngle * Math.PI / 180.0;
            var endRad = endAngle * Math.PI / 180.0;

            var arcStartPoint = new PointF(
                center.X + (float)(radius * Math.Cos(startRad)),
                center.Y + (float)(radius * Math.Sin(startRad)));
            var arcEndPoint = new PointF(
                center.X + (float)(radius * Math.Cos(endRad)),
                center.Y + (float)(radius * Math.Sin(endRad)));

            // 计算标注弧的起始和结束点（在延伸线处理后重新计算）
            var dimArcStartPoint = new PointF(
                center.X + (float)(dimRadius * Math.Cos(startRad)),
                center.Y + (float)(dimRadius * Math.Sin(startRad)));
            var dimArcEndPoint = new PointF(
                center.X + (float)(dimRadius * Math.Cos(endRad)),
                center.Y + (float)(dimRadius * Math.Sin(endRad)));

            // 绘制延伸线（从弧端点到标注弧）- 参考对齐标注的延伸线处理
            float extLineOffset = 2 * _scale;
            if (Math.Abs(offset) > extLineOffset)
            {
                // 延伸线起点：弧的端点
                var extStartPoint1 = arcStartPoint;
                var extStartPoint2 = arcEndPoint;

                // 延伸线终点：标注弧上的对应点，稍微超出标注弧
                var extEndPoint1 = new PointF(
                    center.X + (float)((dimRadius + Math.Sign(offset) * extLineOffset) * Math.Cos(startRad)),
                    center.Y + (float)((dimRadius + Math.Sign(offset) * extLineOffset) * Math.Sin(startRad)));
                var extEndPoint2 = new PointF(
                    center.X + (float)((dimRadius + Math.Sign(offset) * extLineOffset) * Math.Cos(endRad)),
                    center.Y + (float)((dimRadius + Math.Sign(offset) * extLineOffset) * Math.Sin(endRad)));

                _currentGraphics?.DrawLine(pen, extStartPoint1, extEndPoint1);
                _currentGraphics?.DrawLine(pen, extStartPoint2, extEndPoint2);

                // 在延长线起始点绘制标记
                DrawExtensionLineMarkers(extStartPoint1, extStartPoint2);
            }

            // 绘制标注弧线（使用最终的dimRadius）
            var sweepAngle = endAngle - startAngle;
            if (sweepAngle < 0) sweepAngle += 360;
            if (offset < 0) sweepAngle = 360 - sweepAngle; // 负偏移时测量反向弧长

            var arcRect = new RectangleF(
                center.X - (float)Math.Abs(dimRadius),
                center.Y - (float)Math.Abs(dimRadius),
                (float)Math.Abs(dimRadius) * 2,
                (float)Math.Abs(dimRadius) * 2);

            _currentGraphics?.DrawArc(pen, arcRect, startAngle, sweepAngle);

            // 重新计算标注弧的起始和结束点（使用最终的dimRadius）
            dimArcStartPoint = new PointF(
                center.X + (float)(dimRadius * Math.Cos(startRad)),
                center.Y + (float)(dimRadius * Math.Sin(startRad)));
            dimArcEndPoint = new PointF(
                center.X + (float)(dimRadius * Math.Cos(endRad)),
                center.Y + (float)(dimRadius * Math.Sin(endRad)));

            // 弧长标注不绘制箭头

            // 绘制文字
            var font = GetDimensionFont(dimension.Style);
            double measureValue = dimension.Measurement;
            string dimText = GetDimensionText(dimension, measureValue);

            // 使用DXF中的实际文字位置
            var textRefPoint = dimension.TextReferencePoint;
            var textPos = new PointF(
                (float)(textRefPoint.X * _scale + _offset.X),
                (float)(textRefPoint.Y * _scale + _offset.Y));

            // 文字保持水平显示
            float textRotation = 0; // 水平显示

            // 创建文本格式
            var format = CreateTextFormat(StringAlignment.Center, StringAlignment.Center);

            // 绘制旋转文本
            DrawRotatedText(dimText, font, brush, textPos, textRotation, format, 0, new PointF(0, 0));
        }

        /// <summary>
        /// 绘制调试边界框
        /// </summary>
        private void DrawDebugBounds()
        {
            // 创建红色画笔绘制边界框
            using (var debugPen = new Pen(Color.Red, 2.0f))
            {
                // 绘制整体边界框
                var boundsRect = new RectangleF(
                    _bounds.X * _scale + _offset.X,
                    _bounds.Y * _scale + _offset.Y,
                    _bounds.Width * _scale,
                    _bounds.Height * _scale
                );
                _currentGraphics?.DrawRectangle(debugPen, boundsRect.X, boundsRect.Y, boundsRect.Width, boundsRect.Height);

                // 绘制每个实体的边界框
                foreach (var entity in _document.Entities.All)
                {
                    if (!ShouldDrawEntity(entity)) continue;

                    var entityBounds = GetEntityBounds(entity);
                    if (entityBounds.HasValue)
                    {
                        var entityRect = new RectangleF(
                            entityBounds.Value.X * _scale + _offset.X,
                            entityBounds.Value.Y * _scale + _offset.Y,
                            entityBounds.Value.Width * _scale,
                            entityBounds.Value.Height * _scale
                        );

                        // 使用较细的线条绘制单个实体边界框
                        using (var entityPen = new Pen(Color.Red, 1.0f))
                        {
                            entityPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                            _currentGraphics?.DrawRectangle(entityPen, entityRect.X, entityRect.Y, entityRect.Width, entityRect.Height);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 绘制引线
        /// </summary>
        private void DrawLeader(Leader leader, Pen pen)
        {
            if (leader.Vertexes == null || leader.Vertexes.Count < 2)
                return;

            // 转换所有顶点
            var points = leader.Vertexes.Select(v => TransformPoint(new Vector2(v.X, v.Y))).ToArray();

            // 绘制引线段
            for (int i = 0; i < points.Length - 1; i++)
            {
                _currentGraphics?.DrawLine(pen, points[i], points[i + 1]);
            }

            // 在起点绘制箭头（如果有箭头）
            if (leader.ShowArrowhead && points.Length >= 2)
            {
                var startPoint = points[0];
                var secondPoint = points[1];
                // 计算从第二点指向起点的角度，使箭头尖端指向起点
                var angle = Math.Atan2(startPoint.Y - secondPoint.Y, startPoint.X - secondPoint.X);
                DrawArrow(startPoint, (float)angle, pen);
            }
        }





        #region Hatch绘制方法

        /// <summary>
        /// 绘制填充实体
        /// </summary>
        private void DrawHatch(Hatch hatch, Pen pen, Brush brush)
        {
            if (!_options.DrawHatch || hatch.BoundaryPaths.Count == 0) return;

            try
            {
                // 根据填充类型选择绘制方式
                if (hatch.Pattern.Fill == HatchFillType.SolidFill)
                {
                    var paths = CreateHatchPaths(hatch);
                    if (paths.Count == 0) return;

                    ExecuteWithGraphicsState(() =>
                    {
                        foreach (var path in paths)
                        {
                            try
                            {
                                _currentGraphics?.FillPath(brush, path);
                            }
                            catch
                            {
                                // 如果填充失败，忽略这个路径
                            }
                        }
                    });

                    // 清理路径资源
                    foreach (var path in paths)
                    {
                        path?.Dispose();
                    }
                }
                else
                {
                    var paths = CreateHatchPaths(hatch);
                    if (paths.Count == 0) return;
                    string hpname = hatch.Pattern.Name;

                    ExecuteWithGraphicsState(() =>
                    {
                        var hatchStyle = GetHatchStyleFromPattern(hpname);
                        var hatchBrush = new HatchBrush(hatchStyle, pen.Color, Color.Transparent);

                        foreach (var path in paths)
                        {
                            try
                            {
                                _currentGraphics?.FillPath(hatchBrush, path);
                            }
                            catch
                            {
                                // 如果填充失败，绘制边界线
                                _currentGraphics?.DrawPath(pen, path);
                            }
                        }
                        hatchBrush.Dispose();
                    });

                    // 清理路径资源
                    foreach (var path in paths)
                    {
                        path?.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 创建填充路径
        /// </summary>
        private List<GraphicsPath> CreateHatchPaths(Hatch hatch)
        {
            var paths = new List<GraphicsPath>();

            foreach (var boundaryPath in hatch.BoundaryPaths)
            {

                var path = new GraphicsPath();

                try
                {

                    // 使用边缘定义创建路径
                    foreach (var edge in boundaryPath.Edges)
                    {
                        AddEdgeToPath(path, edge);
                    }

                    // 如果路径是闭合的，确保闭合
                    if (boundaryPath.PathType.HasFlag(HatchBoundaryPathTypeFlags.External) ||
                        boundaryPath.PathType.HasFlag(HatchBoundaryPathTypeFlags.Outermost))
                    {
                        path.CloseFigure();
                    }

                }
                catch
                {
                    path?.Dispose();
                    path = null;
                }

                if (path != null)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>
        /// 将边缘添加到路径
        /// </summary>
        private void AddEdgeToPath(GraphicsPath path, HatchBoundaryPath.Edge edge)
        {
            try
            {
                switch (edge.Type)
                {
                    case HatchBoundaryPath.EdgeType.Line:
                        if (edge is HatchBoundaryPath.Line lineEdge)
                        {
                            var start = TransformPoint(new Vector2(lineEdge.Start.X, lineEdge.Start.Y));
                            var end = TransformPoint(new Vector2(lineEdge.End.X, lineEdge.End.Y));
                            path.AddLine(start, end);
                        }
                        break;

                    case HatchBoundaryPath.EdgeType.Spline:
                        if (edge is HatchBoundaryPath.Spline splineEdge)
                        {
                            if (splineEdge.ControlPoints != null && splineEdge.ControlPoints.Length >= 2)
                            {
                                // 将样条曲线转换为实体对象，然后获取近似点
                                var splineEntity = splineEdge.ConvertTo() as netDxf.Entities.Spline;
                                if (splineEntity != null)
                                {
                                    // 使用更多的点来近似样条曲线，确保平滑度
                                    int precision = Math.Max(20, splineEdge.ControlPoints.Length * 5);

                                    // 使用公开的NurbsEvaluator方法计算样条曲线上的点
                                    var curvePoints = netDxf.Entities.Spline.NurbsEvaluator(
                                        splineEntity.ControlPoints,
                                        splineEntity.Weights,
                                        splineEntity.Knots,
                                        splineEntity.Degree,
                                        splineEntity.IsClosed,
                                        splineEntity.IsClosedPeriodic,
                                        precision);

                                    if (curvePoints.Count >= 2)
                                    {
                                        var approximatePoints = new PointF[curvePoints.Count];
                                        for (int i = 0; i < curvePoints.Count; i++)
                                        {
                                            var transformedPoint = TransformPoint(new Vector2(curvePoints[i].X, curvePoints[i].Y));
                                            approximatePoints[i] = transformedPoint;
                                        }
                                        path.AddLines(approximatePoints);
                                    }
                                }
                                else
                                {
                                    // 备用方案：直接连接控制点
                                    var points = new List<PointF>();
                                    foreach (var controlPoint in splineEdge.ControlPoints)
                                    {
                                        var point = TransformPoint(new Vector2(controlPoint.X, controlPoint.Y));
                                        points.Add(point);
                                    }

                                    if (points.Count >= 2)
                                    {
                                        path.AddLines(points.ToArray());
                                    }
                                }
                            }
                        }
                        break;

                    case HatchBoundaryPath.EdgeType.Polyline:
                        if (edge is HatchBoundaryPath.Polyline polylineEdge)
                        {
                            if (polylineEdge.Vertexes.Count() < 2) return;

                            var points = new List<PointF>();
                            foreach (var vertex in polylineEdge.Vertexes)
                            {
                                var point = TransformPoint(new Vector2(vertex.X, vertex.Y));
                                points.Add(point);
                            }

                            if (points.Count >= 2)
                            {
                                if (polylineEdge.IsClosed)
                                {
                                    path.AddPolygon(points.ToArray());
                                }
                                else
                                {
                                    path.AddLines(points.ToArray());
                                }
                            }
                        }
                        break;
                }
            }
            catch
            {
                // 忽略无法处理的边缘
            }
        }

        /// <summary>
        /// 绘制插入块
        /// </summary>
        /// <param name="insert">插入块实体</param>
        /// <param name="pen">画笔</param>
        /// <param name="brush">画刷</param>
        private void DrawInsert(Insert insert, Pen pen, Brush brush)
        {
            if (insert.Block == null) return;
            if (_currentGraphics == null) return;

            // 保存当前Graphics状态
            GraphicsState state = _currentGraphics.Save();

            // 临时保存当前的变换参数
            var originalOffset = _offset;
            var originalScale = _scale;

            try
            {
                // 转换插入点到屏幕坐标
                var insertPoint = TransformPoint(insert.Position);

                // 应用变换到Graphics对象
                // 1. 移动到插入点
                _currentGraphics.TranslateTransform(insertPoint.X, insertPoint.Y);

                // 2. 应用旋转（如果有）
                if (Math.Abs(insert.Rotation) > 0)
                {
                    _currentGraphics.RotateTransform((float)insert.Rotation);
                }

                // 3. 应用缩放
                _currentGraphics.ScaleTransform((float)insert.Scale.X, (float)insert.Scale.Y);

                // 4. 移动块原点到坐标原点
                _currentGraphics.TranslateTransform(
                    -(float)insert.Block.Origin.X * _scale,
                    -(float)insert.Block.Origin.Y * _scale);

                // 重置偏移量，因为变换已经通过Graphics矩阵处理
                _offset = new PointF(0, 0);
                // 保持原始缩放，因为块内实体的缩放已经通过Graphics矩阵处理
                // _scale 保持不变

                // 递归绘制块内的所有实体

                var scaleFactor = Math.Min(insert.Scale.X, insert.Scale.Y);

                foreach (var entity in insert.Block.Entities)
                {
                    if (ShouldDrawEntity(entity))
                    {
                        DrawDxfEntity(entity, _options.LineWidth / (float)scaleFactor);
                    }
                }
            }
            finally
            {
                // 恢复Graphics状态和变换参数
                _currentGraphics.Restore(state);
                _offset = originalOffset;
                _scale = originalScale;
            }
        }

        #endregion

        /// <summary>
        /// 绘制样条曲线
        /// </summary>
        /// <param name="spline">样条曲线实体</param>
        /// <param name="pen">画笔</param>
        private void DrawSpline(Spline spline, Pen pen)
        {
            try
            {
                // 将样条曲线转换为多边形顶点进行绘制
                // 精度设置：根据缩放级别调整精度，确保平滑度
                int precision = Math.Max(50, (int)(100 * _scale));
                if (precision > 500) precision = 500; // 限制最大精度避免性能问题

                var vertices = spline.PolygonalVertexes(precision);
                if (vertices.Count < 2) return;

                // 转换顶点到屏幕坐标
                var points = vertices.Select(v => TransformPoint(v)).ToArray();

                // 绘制样条曲线
                if (points.Length >= 2)
                {
                    if (spline.IsClosed || spline.IsClosedPeriodic)
                    {
                        // 闭合样条曲线
                        _currentGraphics?.DrawPolygon(pen, points);
                    }
                    else
                    {
                        // 开放样条曲线
                        _currentGraphics?.DrawLines(pen, points);
                    }
                }
            }
            catch (Exception)
            {
                // 如果样条曲线转换失败，尝试绘制控制点连线作为备选方案
                if (spline.ControlPoints.Length >= 2)
                {
                    var controlPoints = spline.ControlPoints.Select(cp => TransformPoint(cp)).ToArray();
                    if (controlPoints.Length >= 2)
                    {
                        _currentGraphics?.DrawLines(pen, controlPoints);
                    }
                }
            }
        }

        /// <summary>
        /// 绘制图像
        /// </summary>
        /// <param name="image">图像实体</param>
        /// <param name="pen">画笔</param>
        /// <param name="brush">画刷</param>
        private void DrawImage(netDxf.Entities.Image image, Pen pen, Brush brush)
        {
            try
            {
                // 获取图像的插入点和尺寸
                var insertionPoint = TransformPoint(image.Position);
                float width = (float)image.Width * _scale;
                float height = (float)image.Height * _scale;

                // 获取旋转角度（已经是度数）
                float rotationAngle = -(float)image.Rotation;

                // 计算最终图像位置（参考DrawRotatedText方法的坐标处理）
                float imageX = insertionPoint.X;
                float imageY = _options.Height - insertionPoint.Y - height; // 翻转Y坐标并调整高度

                // 使用ExecuteWithGraphicsState包装图形操作
                ExecuteWithGraphicsState(() =>
                {
                    _currentGraphics?.ResetTransform();

                    // 应用旋转变换
                    if (Math.Abs(rotationAngle) > 0.001f)
                    {
                        _currentGraphics?.TranslateTransform(imageX, imageY);
                        _currentGraphics?.RotateTransform(rotationAngle);

                        // 尝试加载并绘制图像
                        DrawImageContent(image, new PointF(0, 0), width, height, pen, brush);
                    }
                    else
                    {
                        // 无旋转时直接绘制
                        DrawImageContent(image, new PointF(imageX, imageY), width, height, pen, brush);
                    }
                });
            }
            catch (Exception)
            {
                // 如果出现任何错误，绘制一个简单的占位符
                var insertionPoint = TransformPoint(image.Position);
                float width = (float)image.Width * _scale;
                float height = (float)image.Height * _scale;
                float imageY = _options.Height - insertionPoint.Y - height; // 翻转Y坐标
                DrawImagePlaceholder(new PointF(insertionPoint.X, imageY), width, height, pen, brush);
            }
        }

        /// <summary>
        /// 绘制图像内容
        /// </summary>
        private void DrawImageContent(netDxf.Entities.Image image, PointF position, float width, float height, Pen pen, Brush brush)
        {
            // 尝试加载并绘制图像
            if (image.Definition != null && !string.IsNullOrEmpty(image.Definition.File))
            {
                string imagePath = image.Definition.File;

                // 如果是相对路径，尝试与DXF文件路径组合
                if (!Path.IsPathRooted(imagePath) && !string.IsNullOrEmpty(_document.Name))
                {
                    string dxfDirectory = Path.GetDirectoryName(_document.Name) ?? "";
                    imagePath = Path.Combine(dxfDirectory, imagePath);
                }

                // 检查文件是否存在
                if (File.Exists(imagePath))
                {
                    using (var bitmap = new Bitmap(imagePath))
                    {
                        // 绘制图像
                        var destRect = new RectangleF(position.X, position.Y, width, height);
                        _currentGraphics?.DrawImage(bitmap, destRect);
                    }
                }
                else
                {
                    // 如果图像文件不存在，绘制一个占位符矩形
                    DrawImagePlaceholder(position, width, height, pen, brush);
                }
            }
            else
            {
                // 如果没有图像定义，绘制占位符
                DrawImagePlaceholder(position, width, height, pen, brush);
            }
        }

        /// <summary>
        /// 绘制图像占位符
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="pen">画笔</param>
        /// <param name="brush">画刷</param>
        private void DrawImagePlaceholder(PointF position, float width, float height, Pen pen, Brush brush)
        {
            if (_currentGraphics == null) return;
            // 绘制矩形边框
            var rect = new RectangleF(position.X, position.Y, width, height);
            _currentGraphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            // 绘制对角线
            _currentGraphics.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
            _currentGraphics.DrawLine(pen, rect.Right, rect.Top, rect.Left, rect.Bottom);

            using (var font = new Font("Arial", Math.Max(8, height / 10)))
            {
                var textSize = _currentGraphics.MeasureString("IMG", font);
                var textPosition = new PointF(
                    position.X + (width - textSize.Width) / 2,
                    position.Y + (height - textSize.Height) / 2
                );
                _currentGraphics.DrawString("IMG NoFound", font, brush, textPosition);
            }
        }

        /// <summary>
        /// 根据CAD填充模式名称和密度获取对应的GDI+ HatchStyle
        /// </summary>
        /// <param name="patternName">CAD填充模式名称</param>
        /// <param name="scale">填充密度/比例</param>
        /// <returns>对应的HatchStyle</returns>
        private HatchStyle GetHatchStyleFromPattern(string patternName)
        {
            if (string.IsNullOrEmpty(patternName))
                return HatchStyle.Cross;

            string pattern = patternName.ToLower();

            switch (pattern)
            {
                case "ansi31":
                    return HatchStyle.BackwardDiagonal;

                case "ansi32":
                case "steel":
                    return HatchStyle.LightUpwardDiagonal;

                case "ansi33":
                case "brass":
                case "ansi35":
                case "fire":
                    return HatchStyle.LightVertical;

                case "ansi34":
                case "plastic":
                case "ansi36":
                case "line":
                case "plast":
                    return HatchStyle.LightHorizontal;

                case "ansi37":
                case "mudst":
                case "triang":
                case "angle":
                    return HatchStyle.DiagonalCross;

                case "ansi38":
                case "sand":
                    return HatchStyle.DiagonalBrick;

                case "ar-b816":
                case "brick":
                case "ar-hbone":
                case "herringbone":
                case "escher":
                    return HatchStyle.Weave;

                case "sacncr":
                case "ar-conc":
                case "concrete":
                case "box":
                case "grate":
                case "hex":
                case "honey":
                case "net":
                case "square":

                    return HatchStyle.LargeGrid;

                case "ar-parq1":
                case "parquet":
                case "hound":
                    return HatchStyle.Plaid;

                case "ar-rroof":
                case "roof":
                case "ar-rshke":
                case "shake":
                    return HatchStyle.Shingle;

                case "ar-sand":
                case "cork":
                case "dots":
                    return HatchStyle.LargeConfetti;

                case "clay":
                case "net3":
                    return HatchStyle.SmallGrid;

                case "cross":
                    return HatchStyle.Cross;

                case "dash":
                    return HatchStyle.DashedHorizontal;

                case "dolmit":
                case "earth":
                case "swamp":
                    return HatchStyle.Sphere;

                case "flex":
                case "insul":
                    return HatchStyle.Wave;

                case "grass":
                    return HatchStyle.Percent30;

                case "stars":
                    return HatchStyle.SmallConfetti;

                case "trans":
                    return HatchStyle.Percent05;

                case "zigzag":
                    return HatchStyle.ZigZag;

                default:
                    return HatchStyle.Horizontal;
            }
        }

        /// <summary>
        /// 绘制Wipeout实体
        /// </summary>
        /// <param name="wipeout">Wipeout实体</param>
        /// <param name="pen">画笔</param>
        /// <param name="brush">画刷</param>
        private void DrawWipeout(Wipeout wipeout, Pen pen, Brush brush)
        {
            if (wipeout?.ClippingBoundary == null || _currentGraphics == null)
                return;

            var clippingBoundary = wipeout.ClippingBoundary;
            var vertexes = clippingBoundary.Vertexes;

            if (vertexes == null || vertexes.Count < 2)
                return;

            // 创建白色填充画刷用于遮挡效果
            using (var wipeoutBrush = new SolidBrush(_options.KeepOriginalColors ? Color.Black : Color.White))
            {
                if (clippingBoundary.Type == netDxf.ClippingBoundaryType.Rectangular)
                {
                    // 矩形边界
                    if (vertexes.Count >= 2)
                    {
                        var corner1 = TransformPoint(new netDxf.Vector3(vertexes[0].X, vertexes[0].Y, wipeout.Elevation));
                        var corner2 = TransformPoint(new netDxf.Vector3(vertexes[1].X, vertexes[1].Y, wipeout.Elevation));

                        var rect = new RectangleF(
                            Math.Min(corner1.X, corner2.X),
                            Math.Min(corner1.Y, corner2.Y),
                            Math.Abs(corner2.X - corner1.X),
                            Math.Abs(corner2.Y - corner1.Y)
                        );

                        // 填充矩形区域
                        _currentGraphics.FillRectangle(wipeoutBrush, rect);

                        // 可选：绘制边框（如果需要显示边界）
                        if (_options.ShowWipeoutFrame)
                        {
                            _currentGraphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                        }
                    }
                }
                else if (clippingBoundary.Type == netDxf.ClippingBoundaryType.Polygonal)
                {
                    // 多边形边界
                    var points = new PointF[vertexes.Count];
                    for (int i = 0; i < vertexes.Count; i++)
                    {
                        points[i] = TransformPoint(new netDxf.Vector3(vertexes[i].X, vertexes[i].Y, wipeout.Elevation));
                    }

                    // 填充多边形区域
                    _currentGraphics.FillPolygon(wipeoutBrush, points);

                    // 可选：绘制边框（如果需要显示边界）
                    if (_options.ShowWipeoutFrame)
                    {
                        _currentGraphics.DrawPolygon(pen, points);
                    }
                }
            }
        }

        /// <summary>
        /// 绘制MLine实体
        /// </summary>
        /// <param name="mline">MLine实体</param>
        /// <param name="pen">画笔</param>
        private void DrawMLine(netDxf.Entities.MLine mline, Pen pen)
        {
            // 使用MLine的Explode方法获取基本实体（线段和弧）
            var entities = mline.Explode();

            foreach (var entity in entities)
            {
                // 获取实体的颜色
                var entityColor = GetEntityColor(entity);
                var entityPen = GetPen(entityColor);

                // 根据实体类型调用相应的绘制方法
                if (entity is netDxf.Entities.Line line)
                {
                    DrawLine(line, entityPen);
                }
                //else if (entity is netDxf.Entities.Arc arc)
                //{
                //    DrawArc(arc, entityPen);
                //}

            }
        }
    }
}
