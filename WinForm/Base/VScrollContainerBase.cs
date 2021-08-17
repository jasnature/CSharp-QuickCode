using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForm.Base
{

    public abstract class VScrollContainerBase : Form //Control 
    {
        #region Event Region

        public event EventHandler ViewportChanged;
        public event EventHandler ContentSizeChanged;

        #endregion

        #region Field Region

        protected readonly SmallScrollBar _vScrollBar;
        protected readonly SmallScrollBar _hScrollBar;

        private Size _visibleSize;
        private Size _contentSize;

        private Rectangle _viewport;

        private Point _offsetMousePosition;

        private int _maxDragChange = 0;

        private bool _hideScrollBars = true;

        #endregion

        #region Constructor Region

        protected VScrollContainerBase()
        {
            SetStyle(ControlStyles.Selectable |
                     ControlStyles.UserMouse, true);

            _vScrollBar = new SmallScrollBar { ScrollDirection = ScrollOrientation.VerticalScroll };
            _hScrollBar = new SmallScrollBar { ScrollDirection = ScrollOrientation.HorizontalScroll };

            Controls.Add(_vScrollBar);
            Controls.Add(_hScrollBar);

            _vScrollBar.ValueChanged += delegate { UpdateViewport(); };
            _hScrollBar.ValueChanged += delegate { UpdateViewport(); };

            _vScrollBar.MouseDown += delegate { Select(); };
            _hScrollBar.MouseDown += delegate { Select(); };

            base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            base.ShowInTaskbar = false;
            base.ShowIcon = false;
            base.ControlBox = false;
        }

        protected override void OnLoad(EventArgs e)
        {
            Region = new Region(GetPath(new Size(Width, Height), 10));
            base.OnLoad(e);

        }

        #endregion

        private GraphicsPath GetPath(Size sz, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            Rectangle arcRect = new Rectangle(new Point(0, 0), new Size(radius, radius));

            path.AddArc(arcRect, 180, 90);
            arcRect.X = sz.Width - radius;

            path.AddArc(arcRect, 270, 90);
            arcRect.Y = sz.Height - radius;

            path.AddArc(arcRect, 0, 90);
            arcRect.X = 0;

            path.AddArc(arcRect, 90, 90);

            path.CloseFigure();

            return path;
        }


        #region Property Region


        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Rectangle Viewport
        {
            get { return _viewport; }
            private set
            {
                _viewport = value;

                if (ViewportChanged != null)
                    ViewportChanged(this, null);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Size ContentSize
        {
            get { return _contentSize; }
            set
            {
                _contentSize = value;
                UpdateScrollBars();

                if (ContentSizeChanged != null)
                    ContentSizeChanged(this, null);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Point OffsetMousePosition
        {
            get { return _offsetMousePosition; }
        }

        [Category("Behavior")]
        [Description("Determines the maximum scroll change when dragging.")]
        [DefaultValue(0)]
        public int MaxDragChange
        {
            get { return _maxDragChange; }
            set
            {
                _maxDragChange = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsDragging { get; private set; }

        [Category("Behavior")]
        [Description("Determines whether scrollbars will remain visible when disabled.")]
        [DefaultValue(true)]
        public bool HideScrollBars
        {
            get { return _hideScrollBars; }
            set
            {
                _hideScrollBars = value;
                UpdateScrollBars();
            }
        }

        #endregion



        #region Method Region

        private void UpdateScrollBars()
        {
            if (_vScrollBar.Maximum != ContentSize.Height)
                _vScrollBar.Maximum = ContentSize.Height;

            if (_hScrollBar.Maximum != ContentSize.Width)
                _hScrollBar.Maximum = ContentSize.Width;

            var scrollSize = Consts.ScrollBarSize;

            _vScrollBar.Location = new Point(ClientSize.Width - scrollSize, 0);
            _vScrollBar.Size = new Size(scrollSize, ClientSize.Height);

            _hScrollBar.Location = new Point(0, ClientSize.Height - scrollSize);
            _hScrollBar.Size = new Size(ClientSize.Width, scrollSize);

            if (DesignMode)
                return;

            // Do this twice in case changing the visibility of the scrollbars
            // causes the VisibleSize to change in such a way as to require a second scrollbar.
            // Probably a better way to detect that scenario...
            SetVisibleSize();
            SetScrollBarVisibility();
            //SetVisibleSize();
            //SetScrollBarVisibility();

            //if (_vScrollBar.Visible)
            _hScrollBar.Width -= scrollSize;

            if (_hScrollBar.Visible){
                _vScrollBar.Height -= scrollSize;
            }
            else{
                _vScrollBar.Maximum -= (scrollSize*2);
            }

            _vScrollBar.ViewSize = _visibleSize.Height;
            _hScrollBar.ViewSize = _visibleSize.Width;

            UpdateViewport();
        }

        private void SetScrollBarVisibility()
        {
            _vScrollBar.Enabled = _visibleSize.Height < ContentSize.Height;
            _hScrollBar.Enabled = _visibleSize.Width < ContentSize.Width;

            if (_hideScrollBars)
            {
                //_vScrollBar.Visible = _vScrollBar.Enabled;
                _hScrollBar.Visible = _hScrollBar.Enabled;
            }
        }

        private void SetVisibleSize()
        {
            var scrollSize = Consts.ScrollBarSize;

            _visibleSize = new Size(ClientSize.Width, ClientSize.Height);

            //if (_vScrollBar.Visible)
            _visibleSize.Width -= scrollSize;

            if (_hScrollBar.Visible)
                _visibleSize.Height -= scrollSize;
        }

        private void UpdateViewport()
        {
            var left = 0;
            var top = 0;
            var width = ClientSize.Width;
            var height = ClientSize.Height;

            if (_hScrollBar.Visible)
            {
                left = _hScrollBar.Value;
                height -= _hScrollBar.Height;
            }

            //if (_vScrollBar.Visible)
            //{
            top = _vScrollBar.Value;
            width -= _vScrollBar.Width;
            //}
            
            Viewport = new Rectangle(left, top, width, height - Consts.ScrollBarSize);

            var pos = PointToClient(MousePosition);
            _offsetMousePosition = new Point(pos.X + Viewport.Left, pos.Y + Viewport.Top);

            Invalidate();
        }

        public void ScrollTo(Point point)
        {
            HScrollTo(point.X);
            VScrollTo(point.Y);
        }

        public void VScrollTo(int value)
        {
            //if (_vScrollBar.Visible)
            _vScrollBar.Value = value;
        }

        public void HScrollTo(int value)
        {
            if (_hScrollBar.Visible)
                _hScrollBar.Value = value;
        }

        public Point PointToView(Point point)
        {
            return new Point(point.X - Viewport.Left, point.Y - Viewport.Top);
        }

        public Rectangle RectangleToView(Rectangle rect)
        {
            return new Rectangle(new Point(rect.Left - Viewport.Left, rect.Top - Viewport.Top), rect.Size);
        }

        #endregion

        #region Event Handler Region

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            UpdateScrollBars();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);

            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);

            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            UpdateScrollBars();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            _offsetMousePosition = new Point(e.X + Viewport.Left, e.Y + Viewport.Top);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
                Select();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var horizontal = false;

            if (_hScrollBar.Visible && ModifierKeys == Keys.Control)
                horizontal = true;

            if (_hScrollBar.Visible && _hScrollBar.Focused)
                horizontal = true;

            if (!horizontal)
            {
                int scrollPixel = this.ContentSize.Height / 60;

                if (e.Delta > 0)
                    _vScrollBar.ScrollByPhysical(scrollPixel);
                else if (e.Delta < 0)
                    _vScrollBar.ScrollByPhysical(-scrollPixel);
            }
            else
            {
                if (e.Delta > 0)
                    _hScrollBar.ScrollByPhysical(3);
                else if (e.Delta < 0)
                    _hScrollBar.ScrollByPhysical(-3);
            }
        }

        protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Allows arrow keys to trigger OnKeyPress
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    e.IsInputKey = true;
                    break;
            }
        }



        #endregion






    }
}
