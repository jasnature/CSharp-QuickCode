using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForm
{
    public class TileList : Control
    {
        protected VScrollBar _scrollControl = new VScrollBar();
        protected ObservableCollection<VListItem> _tileItems = new ObservableCollection<VListItem>();
        protected int _viewStart = 0;
        protected int _viewEnd = 10;
        protected byte _mouseState = 0;
        protected ToolTip _tip = new ToolTip() { ShowAlways = true };
        protected int _lastHoverIndex = -1;
        protected Cursor _handCur = Cursors.Hand;
        protected bool _isInvaRect = false;

        protected Rectangle ContentView { get; set; }
        protected int CalcLineInView { get; set; }
        protected int CalcColumnInView { get; set; }

        //draw
        protected Pen _hiPen = new Pen(Color.Salmon, 2);

        //System.Drawing.Bitmap memoryImg = null;

        #region Public

        protected StringFormat _strFormat = new StringFormat()
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        [Browsable(false)]
        public StringFormat TileContentFormat
        {
            get { return _strFormat; }
            set
            {
                _strFormat = value;
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ObservableCollection<VListItem> TileItems
        {
            get { return _tileItems; }
            set
            {
                _tileItems = value;
                //UpdateListBox();
            }
        }

        [DefaultValue(false)]
        public bool ShowBorder { get; set; }


        protected int _tileHeight = 21;

        [DefaultValue(21)]
        [Browsable(true)]
        public int TileHeight
        {
            get { return _tileHeight; }
            set
            {
                _tileHeight = value;
            }
        }

        protected int _tileWidth = 150;
        [DefaultValue(150)]
        [Browsable(true)]
        public int TileWidth
        {
            get { return _tileWidth; }
            set
            {
                _tileWidth = value;
            }
        }

        public bool EnableToolTip { get; set; }

        public Point MouseLastLocation { get; protected set; }

        protected int _selectIndex = -1;
        public int SelectIndex
        {
            get { return _selectIndex; }
            set
            {
                if (value < 0 || value >= this._tileItems.Count) return;

                _selectIndex = value;
                _selectTile = _tileItems[_selectIndex];

                NavigateSelectTile();
            }
        }

        protected VListItem _selectTile;

        [Browsable(false)]
        public VListItem SelectTile
        {
            get { return _selectTile; }
        }

        public event Action<VListItem, MouseEventArgs> ClickTile;

        #endregion


        public TileList()
        {
            SetStyle(ControlStyles.UserMouse | ControlStyles.OptimizedDoubleBuffer, true);
            _tileItems.CollectionChanged += _tileItems_CollectionChanged;

            AllowDrop = true;


        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateContentSize();
            Invalidate();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            _scrollControl.Value = 0;
            _scrollControl.Maximum = 0;
            _scrollControl.ValueChanged += scroll_ValueChanged;
            this.Controls.Add(_scrollControl);

            UpdateContentSize();

        }



        void _tileItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _scrollControl.Maximum = (this._tileItems.Count / CalcColumnInView);
            Invalidate();
        }

        protected void NavigateSelectTile()
        {
            if (_selectIndex < 0 || _selectIndex >= this._tileItems.Count) return;

            int skipPage = (_selectIndex / (CalcLineInView * CalcColumnInView)) * CalcLineInView;

            _scrollControl.Value = skipPage;

            Invalidate();
        }

        private void UpdateViewStart_End()
        {
            _viewStart = _scrollControl.Value * CalcColumnInView;

            _viewEnd = _viewStart + (CalcLineInView * CalcColumnInView);
        }

        #region DragDrop

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            //drgevent.Effect = drgevent.AllowedEffect;
            if (drgevent.AllowedEffect == DragDropEffects.Move)
            {

            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            drgevent.Effect = drgevent.AllowedEffect;
        }

        //protected override void OnDragEnter(DragEventArgs drgevent)
        //{
        //    base.OnDragEnter(drgevent);
        //    drgevent.Effect = drgevent.AllowedEffect;
        //}

        #endregion

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _mouseState = 1;
            MouseLastLocation = e.Location;
            int saveLastSelect = _selectIndex;

            if (_tileItems.Count > 0)
            {
                bool find = false;
                int index = _viewStart;
                for (; index < _viewEnd; index++)
                {
                    if (_tileItems.Count <= 0 || index >= _tileItems.Count) break;
                    VListItem item = _tileItems[index];
                    if (item.Area.Contains(MouseLastLocation))
                    {
                        find = true;
                        break;
                    }
                }

                if (find)
                {
                    _selectIndex = index;
                    _selectTile = _tileItems[index];

                    if (ClickTile != null)
                    {
                        ClickTile(_selectTile, e);
                    }
                    InvalidateTile(_selectTile.Area);

                    if (saveLastSelect > -1)
                        InvalidateTile(_tileItems[saveLastSelect].Area);

                    if (e.Button == System.Windows.Forms.MouseButtons.Middle && AllowDrop)
                    {
                        _tip.Hide(this);
                        DoDragDrop(_tileItems[index], DragDropEffects.Move);
                    }
                }
                else
                {
                    if (_selectTile != null)
                        InvalidateTile(_selectTile.Area);

                    _selectIndex = -1;
                    _selectTile = null;
                }


                this.Focus();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _mouseState = 0;

            if (_selectTile != null)
                InvalidateTile(_selectTile.Area);
        }

        protected void InvalidateTile(Rectangle invalidRect)
        {
            if (invalidRect.Width * invalidRect.Height <= this.TileWidth * this.TileHeight)
            {
                invalidRect.X -= (int)Math.Ceiling(Margin.Left / 2.0);
                invalidRect.Y -= (int)Math.Ceiling(Margin.Top / 2.0);
                invalidRect.Width += (int)(Margin.Left * 1.5);
                invalidRect.Height += (int)(Margin.Left * 1.5);
            }
            _isInvaRect = true;
            Invalidate(invalidRect);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            int result = _scrollControl.Value;

            if (e.Delta < 0)
            {
                if (result >= _scrollControl.Maximum - CalcLineInView - 1)
                {
                    result = _scrollControl.Maximum - CalcLineInView + 1;
                }
                else
                {
                    result += CalcLineInView;
                }
            }
            else
            {
                result -= CalcLineInView;

            }
            if (result < 0) result = 0;
            _scrollControl.Value = result;
        }

        void scroll_ValueChanged(object sender, EventArgs e)
        {
            UpdateViewStart_End();
            Invalidate();
        }

        private void UpdateContentSize()
        {
            ContentView = new Rectangle(Margin.Left, Margin.Top,
                this.Width - _scrollControl.Width - Margin.Left - Margin.Right,
                this.Height - Margin.Top - Margin.Bottom);

            CalcLineInView = ContentView.Height / (TileHeight + Margin.Top);
            CalcColumnInView = ContentView.Width / TileWidth;
            if (CalcColumnInView <= 0) CalcColumnInView = 1;

            UpdateViewStart_End();

            _scrollControl.LargeChange = CalcLineInView;
            _scrollControl.SmallChange = CalcLineInView;

            _scrollControl.Size = new System.Drawing.Size(10, this.Height - 1);
            _scrollControl.Location = new Point(this.Width - 11, 1);

            _scrollControl.Maximum = (this._tileItems.Count / CalcColumnInView);

            //if (memoryImg != null) memoryImg.Dispose();

            // memoryImg = new Bitmap(ContentView.Width, ContentView.Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_tileItems.Count > 0)
            {
                bool find = false;
                int index = _viewStart;
                for (; index < _viewEnd; index++)
                {
                    if (_tileItems.Count <= 0 || index >= _tileItems.Count) break;
                    if (_tileItems[index].Area.Contains(e.Location))
                    {
                        find = true;
                        break;
                    }
                }

                if (find && _tileItems[index] != null)
                {
                    if (index != _lastHoverIndex)//if same then once
                    {
                        if (_lastHoverIndex > -1)
                        {
                            _tileItems[_lastHoverIndex].FontStyle = FontStyle.Regular;
                            InvalidateTile(_tileItems[_lastHoverIndex].Area);
                        }

                        //curr hover
                        _lastHoverIndex = index;

                        Cursor = _handCur;

                        if (EnableToolTip)
                        {
                            _tip.ToolTipTitle = _tileItems[index].Text;
                            _tip.Show(_tileItems[index].Desc, this, _tileItems[index].Area.Left + 1, _tileItems[index].Area.Bottom + 1, 8000);
                        }


                        _tileItems[index].FontStyle = FontStyle.Underline;

                        InvalidateTile(_tileItems[index].Area);
                    }

                }
                else
                {
                    if (_lastHoverIndex > -1)
                    {
                        _tileItems[_lastHoverIndex].FontStyle = FontStyle.Regular;
                        InvalidateTile(_tileItems[_lastHoverIndex].Area);
                    }

                    if (Cursor.Handle != Cursors.Default.Handle)
                    {
                        //this.Parent.Text = DateTime.Now.Ticks + "";
                        Cursor = Cursors.Default;
                    }

                    _tip.Hide(this);
                    _lastHoverIndex = -1;
                }


            }
        }

        SolidBrush bluebg = new SolidBrush(Color.FromArgb(66, 105, 165));
        //SolidBrush bluebg1 = new SolidBrush(Color.FromArgb(0, 122, 204));
        //SolidBrush greenbg = new SolidBrush(Color.FromArgb(57, 130, 90));


        int grapCount = 0;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            grapCount++;
            int grapItem = 0;
            //Graphics g = Graphics.FromImage(memoryImg);
            Graphics g = e.Graphics;

            g.Clear(this.BackColor);

            if (ShowBorder)
            {
                if (Focused)
                {
                    g.DrawRectangle(SystemPens.Highlight, 0, 0, this.Width - 1, this.Height - 1);
                }
                else
                {
                    g.DrawRectangle(SystemPens.GrayText, 0, 0, this.Width - 1, this.Height - 1);
                }
            }

            int y = 0;
            Rectangle rectContent;
            VListItem item;

            for (int index = _viewStart; index < _viewEnd; ) //draw item count 
            {
                if (_tileItems.Count <= 0 || index >= _tileItems.Count) break;
                for (int col = 0; col < CalcColumnInView; col++)
                {
                    if (index >= _tileItems.Count)
                    {
                        break;
                    }

                    item = _tileItems[index];

                    //update item rect
                    rectContent = ContentView;
                    rectContent.X = (col * (TileWidth + Margin.Left)) + ContentView.Y;
                    rectContent.Y = (y * (TileHeight + Margin.Top)) + ContentView.X;
                    rectContent.Width = TileWidth;
                    rectContent.Height = TileHeight;

                    item.Area = rectContent;

                    if (_isInvaRect && !e.ClipRectangle.Contains(item.Area.Location))
                    {
                        index++;
                        continue;
                    }

                    grapItem++;

                    if (index == SelectIndex)//selected
                    {
                        if (_mouseState == 1)
                        {
                            rectContent.X += 1;
                            rectContent.Y += 1;
                            item.Area = rectContent;

                        }

                        g.FillRectangle(bluebg, item.Area);
                        g.DrawRectangle(_hiPen, item.Area);

                    }
                    else
                    {
                        g.FillRectangle(bluebg, item.Area);

                        //hover
                        if (item.FontStyle == FontStyle.Underline)
                        {
                            g.DrawRectangle(Pens.WhiteSmoke, item.Area);
                        }
                    }

                    using (var modFont = new Font(Font, item.FontStyle))
                    {
                        // Text
                        Rectangle textArea = item.Area;
                        if (Padding != Padding.Empty)
                        {
                            textArea.X += Padding.Left;
                            textArea.Y += Padding.Top;
                            textArea.Width -= (Padding.Left + Padding.Right);
                            textArea.Height -= (Padding.Top + Padding.Bottom);
                        }
                        g.DrawString(item.Text, modFont, Brushes.White, textArea, _strFormat);
                    }



                    index++;
                }

                y++;
            }


            //#if DEBUG
            this.Parent.Text = grapCount.ToString() + "-Item:" + grapItem;
            //#endif
            _isInvaRect = false;
            //g.DrawRectangle(Pens.Red, ContentView);

            //e.Graphics.DrawImage(memoryImg, 0, 0);
        }

        
    }
}
