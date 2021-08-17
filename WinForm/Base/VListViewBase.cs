using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForm.Base
{
    public abstract class VListViewBase : VScrollContainerBase
    {
        #region Constructor Region

        protected VListViewBase()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint , true);
        }

        #endregion

        #region Paint Region

        protected abstract void PaintContent(Graphics g);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            //Draw background
            using (var b = new SolidBrush(BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            //Offset the graphics based on the viewport, render the control contents, then reset it.
            g.TranslateTransform(Viewport.Left * -1, Viewport.Top * -1);

            PaintContent(g);

            g.TranslateTransform(Viewport.Left, Viewport.Top);
            
            //g.DrawRectangle(Pens.Black, 0, 0, this.Size.Width+30, this.Size.Height+30);

        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            //base.OnPaintBackground(e);
            //stop base event use OnPaint draw it.
        }

        #endregion
    }
}
