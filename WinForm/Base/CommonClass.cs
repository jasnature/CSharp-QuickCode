using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinForm.Base
{

 public sealed class Consts
    {
        public static int Padding = 10;

        public static int ScrollBarSize = 7;
        public static int ArrowButtonSize = 6;
        public static int MinimumThumbSize = 10;

        public static int CheckBoxSize = 12;
        public static int RadioButtonSize = 12;

        public const int ToolWindowHeaderSize = 25;
        public const int DocumentTabAreaSize = 24;
        public const int ToolWindowTabAreaSize = 21;
    }

  public sealed class VColors
    {
        //all bg light
        public static Color GreyBackground
        {
            get { return Color.FromArgb(239, 239, 241); }//灰白色
        }

        public static Color BlueSelection
        {
            get { return Color.FromArgb(0, 122, 255); }
        }

        public static Color UnForceSelection
        {
            get { return Color.FromArgb(0, 122, 206); }
        }

        public static Color BlueHighlight
        {
            get { return Color.FromArgb(0, 122, 206); }
        }

        public static Color UnActiveBar
        {
            get {  return Color.FromArgb(122, 128, 132); } 
        }

        public static Color OddBackground
        {
            get { return Color.FromArgb(210, 206, 220); }
        }

        public static Color ActiveControlBackground
        {
            get { return Color.FromArgb(0, 122, 206); }
        }

        public static Color LightDefaultText
        {
            get { return Color.FromArgb(33, 33, 33); }
        }

        public static Color ActiveLightText
        {
            get { return Color.FromArgb(252, 252, 252); }//白色
        }
    }
    
    
    
    


}
