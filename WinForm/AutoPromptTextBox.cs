using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinForm
{
 public class AutoPromptTextBox : RichTextBox
    {
        protected VListView lbPromptListBox = new VListView() { Visible = false, Height = 220, Width = 300 };

        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        public event Func<Message, Keys, bool> ProcessCmdKeyNotify;

        protected List<LightWord> notifyWords = new List<LightWord>(28);


        int lastCharIndex = -1;

        string lastSearchString = "";
        int lastSearchWordIndex = -1;

        public AutoPromptTextBox()
        {
            //SetStyle(ControlStyles.UserPaint| ControlStyles.ResizeRedraw, true);
            lbPromptListBox.KeyUp += lbPromptListBox_KeyUp;
            lbPromptListBox.MouseDoubleClick += lbPromptListBox_MouseDoubleClick;

            this.ProcessCmdKeyNotify += this_ProcessCmdKeyNotify;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            lbPromptListBox.Visible = false;
        }

        void lbPromptListBox_KeyUp(object sender, KeyEventArgs e)
        {
            int keyValue = e.KeyValue;
            this.Focus();
            if (e.KeyCode == Keys.Enter && lastCharIndex > 0)
            {
                InsertAutoCompletedWord();
            }
            else if (e.KeyCode == Keys.Back)
            {
                this.Select(lastCharIndex, 0);

            } if ((keyValue >= 65 && keyValue <= 90)
                 || (keyValue >= 48 && keyValue <= 57)
                 || (keyValue >= 96 && keyValue <= 106)
                 )
            {
                Clipboard.SetText(((char)keyValue).ToString().ToLower());
                CurrentInputControl.Paste();
                Clipboard.Clear();
            }

            HandleCommonKeys(sender, e.KeyCode);

        }


        void lbPromptListBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            InsertAutoCompletedWord();
        }

        //setp notify entry
        private void ShowKeywordsNotify(int keycode)
        {
            lastCharIndex = this.SelectionStart;
            Point lastNotifyPoint = this.GetPositionFromCharIndex(lastCharIndex);

            Point screenPoint = this.PointToScreen(lastNotifyPoint);

            screenPoint.Offset(0, 20);
            lbPromptListBox.Location = screenPoint;
            if (!lbPromptListBox.Visible)
            {
                lbPromptListBox.TopMost = true;
                lbPromptListBox.BringToFront();
                lbPromptListBox.Visible = true;
                this.Focus();
            }
        }

        public void EndNotifyWords()
        {
            //ShowNotify = false;
            lbPromptListBox.Hide();
            lbPromptListBox.ClearSelectItems();
            this.Focus();
        }

        private void HandleCommonKeys(object sender, Keys key)
        {
            if (key == Keys.Escape || key == Keys.Space)
            {
                EndNotifyWords();
            }

        }

        private int FindInputString()
        {
            lastSearchWordIndex = -1;
            for (int i = this.SelectionStart - 1; i >= 0; i--)
            {
                if (this.Text[i] == ' ' || this.Text[i] == '\n' || this.Text[i] == ',')
                {
                    lastSearchWordIndex = i;
                    break;
                }
            }
            lastSearchString = this.Text.Substring(lastSearchWordIndex + 1, this.SelectionStart - lastSearchWordIndex - 1);
            if (lastSearchString.Length > 1 && lastSearchString != "")
            {
                return notifyWords.FindIndex(p => p.Text.IndexOf(lastSearchString, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }

            return -1;
        }

        bool ishiwork = false;
        private void SearchInputAndHilightWord()
        {
            if (ishiwork) return;
            try
            {
                ishiwork = true;
                int currSelIndex = this.SelectionStart;
                int currCharIndex = this.GetFirstCharIndexOfCurrentLine();
                int currLineIndex = this.GetLineFromCharIndex(currCharIndex);

                string confirmWord = "";
                int searchLeft = currSelIndex;
                int searchRight = currSelIndex - 1;
                //left
                while (true)
                {
                    if (searchLeft <= 0 || currSelIndex == currCharIndex)
                    {
                        break;
                    }
                    searchLeft--;
                    if (this.Text[searchLeft] == ' ' || this.Text[searchLeft] == '\n' || this.Text[searchLeft] == ',' || this.Text[searchLeft] == '(')
                    {
                        searchLeft++;
                        break;
                    }
                }

                //right
                while (true)
                {
                    if (searchRight >= this.TextLength || currSelIndex == currCharIndex
                        || this.Text[searchRight] == ' ' || this.Text[searchRight] == ',' || this.Text[searchRight] == '\n' || this.Text[searchRight] == '(')
                    {
                        break;
                    }
                    searchRight++;
                }

                if (searchRight - searchLeft <= 0 || searchLeft < 0 || searchRight < 1) return;

                confirmWord = this.Text.Substring(searchLeft, searchRight - searchLeft);

                SetLightWord(confirmWord, searchLeft, searchRight - searchLeft);

                //ttt.Text = "L=" + currLineIndex + " C=" + currCharIndex + " Word=" + confirmWord;
                this.Select(currSelIndex, 0);

            }
            finally
            {
                ishiwork = false;
            }

        }

        private void SetLightWord(string confirmWord, int start, int len)
        {
            if (lbPromptListBox.Items.Count <= 0) return;

            LightWord isHi = notifyWords.FirstOrDefault(p => confirmWord.Equals(p.Text, StringComparison.CurrentCultureIgnoreCase));

            if (isHi == null && (SelectionColor == Color.Black || SelectionColor == SystemColors.WindowText)) return;

            //if (isHi != null && SelectionColor != Color.Black && SelectionColor != SystemColors.WindowText) return;

            this.Select(start, len);
            this.SelectionColor = isHi != null ? isHi.ForeColor : this.ForeColor;
            this.SelectionFont = isHi != null ? isHi.ForeFont : this.Font;
        }

        private void HiLightSetTextWord()
        {
            if (this.Lines.Length <= 0) return;

            int ss = this.SelectionStart;//currloc

            while (PasteBeforeLine < this.Lines.Length)
            {
                int currLineChar = this.GetFirstCharIndexFromLine(PasteBeforeLine);

                string currStr = this.Lines[PasteBeforeLine];

                int len = currStr.Length;
                int i = 0;
                int beforeLoc = 0;
                string confirmWord = null;
                while (i < len)
                {
                    if (currStr[i] == ' ' || currStr[i] == ',' || currStr[i] == '(')
                    {
                        if (i - beforeLoc > 0)
                        {
                            confirmWord = currStr.Substring(beforeLoc, i - beforeLoc);

                            SetLightWord(confirmWord, beforeLoc + currLineChar, confirmWord.Length);

                            beforeLoc = i + 1;
                        }
                    }
                    i++;
                }

                PasteBeforeLine++;
            }

            this.Select(ss, 0);
        }

        private void InsertAutoCompletedWord()
        {
            this.Select(lastSearchWordIndex + 1, lastSearchString.Length);
            //rtbScript.Text = text.Remove(lastSearchWordIndex + 1, lastSearchString.Length);

            LightWord re = lbPromptListBox.SelectedItem.Tag as LightWord;
            Clipboard.SetText(re.Text);
            EndNotifyWords();
            lastCharIndex += (re.Text.Length - lastSearchString.Length);
            this.Paste();
            this.Select(lastCharIndex, 0);
            Clipboard.Clear();
        }





        #region this event

        protected override void OnKeyUp(KeyEventArgs e)
        {
            var key = e.KeyCode;
            int keyValue = e.KeyValue;

            //some char input
            if ((keyValue >= 65 && keyValue <= 90)
                || (keyValue >= 48 && keyValue <= 57)
                || (keyValue >= 96 && keyValue <= 106 || key == Keys.Back)
                )
            {
                int fi = FindInputString();
                if (fi >= 0)
                {
                    lbPromptListBox.SelectItem(fi);
                    ShowKeywordsNotify(keyValue);
                }
                else
                {
                    EndNotifyWords();
                }
            }


            HandleCommonKeys(null, key);

            base.OnKeyUp(e);
        }

        bool isPaste = false;
        int PasteBeforeLine = 0;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteBeforeLine = this.Lines.Length - 1;
                if (PasteBeforeLine < 0) PasteBeforeLine = 0;
                isPaste = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (!isPaste)
            {
                SearchInputAndHilightWord();
                base.OnTextChanged(e);
            }
            else
            {
                HiLightSetTextWord();
            }

            isPaste = false;
        }

        public override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                base.Text = value;
                HiLightSetTextWord();
            }
        }

        private bool this_ProcessCmdKeyNotify(Message msg, Keys keyd)
        {
            if (lbPromptListBox.Visible)
            {
                if (keyd == Keys.Enter)
                {
                    InsertAutoCompletedWord();
                    return true;
                }
                else if (keyd == Keys.Down)
                {
                    if (lbPromptListBox.SelectedIndex < 0)
                        lbPromptListBox.SelectItem(0);

                    if (lbPromptListBox.SelectedIndex < lbPromptListBox.Items.Count - 1)
                    {
                        lbPromptListBox.SelectItem(lbPromptListBox.SelectedIndex + 1);
                    }

                    return true;
                }
                else if (keyd == Keys.Up)
                {
                    if (lbPromptListBox.SelectedIndex > 0)
                    {
                        lbPromptListBox.SelectItem(lbPromptListBox.SelectedIndex - 1);
                    }
                    return true;
                }

            }

            return false;
        }

        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (ProcessCmdKeyNotify != null && ProcessCmdKeyNotify(m, keyData))
            {
                return true;
            }

            return base.ProcessCmdKey(ref m, keyData);
        }



        #endregion

        #region public method

        public bool AddLightWord(string text, byte type = 1, string desc = null)
        {
            if (!notifyWords.Any(p => p.Text.Equals(text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var lw = new LightWord(text, type);
                if (!string.IsNullOrEmpty(desc))
                {
                    lw.Desc = desc;
                }
                notifyWords.Add(lw);

                return true;
            }
            return false;
        }

        public bool AddLightWord(LightWord lw)
        {
            if (!notifyWords.Any(p => p.Text.Equals(lw.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                notifyWords.Add(lw);

                return true;
            }
            return false;
        }

        public void AddLightWord(params LightWord[] lws)
        {
            notifyWords.AddRange(lws);
        }

        public void BuildWordPrompt()
        {
            lbPromptListBox.Items.Clear();
            lbPromptListBox.ClearSelectItems();

            lbPromptListBox.ShowIcons = true;
            notifyWords = notifyWords.OrderBy(p => p.Text).ToList();
            foreach (var lw in notifyWords)
            {
                var oo = new VListItem(lw.Desc) { Tag = lw, Icon = Properties.Resources.search };
                lbPromptListBox.Items.Add(oo);
                if (lbPromptListBox.Width < oo.Area.Width)
                {
                    lbPromptListBox.Width = oo.Area.Width + 8;
                }
            }
        }

        TextBoxBase CurrentInputControl = null;

        /// <summary>
        /// need form load completed then call method.
        /// </summary>
        public void EnableAutoPrompt(TextBoxBase inputControl)
        {
            CurrentInputControl = inputControl;
            if (lbPromptListBox is Form)
            {
                lbPromptListBox.TopMost = true;
                lbPromptListBox.Hide();
            }
            else
            {
                var cform = this.FindForm();
                if (cform != null)
                {
                    cform.Controls.Add(lbPromptListBox);
                }
                else
                {
                    throw new Exception("Enable Auto Prompt Failed.");
                }
            }

            BuildWordPrompt();
        }

        public void ClearLightWord()
        {
            notifyWords.Clear();
            lbPromptListBox.Items.Clear();
        }

        #endregion




        //protected override void OnPaint(PaintEventArgs e)
        //{
        //    //TextRenderer.DrawText(e.Graphics, this.Text, this.Font, , Color.Red);
        //    //ButtonRenderer.DrawButton(e.Graphics, new Rectangle(0, 0, 200, 30), System.Windows.Forms.VisualStyles.PushButtonState.Normal);
        //    //e.Graphics.DrawString(this.Text, this.Font, Brushes.Black,0,0);
        //    e.Graphics.DrawRectangle(Pens.Red, this.ClientRectangle);
        //    base.OnPaint(e);
        //}

        //const int WM_KEYDOWN = 0x0100;
        //const int WM_KEYUP = 0x0101;

        //protected override void WndProc(ref Message m)
        //{
        //    switch (m.Msg)
        //    {
        //        case WM_Paste:
        //        case WM_Copy:
        //            var a = 123;

        //            break;
        //    }


        //    base.WndProc(ref m);
        //}




    }


    public class LightWord
    {
        public LightWord(string text, byte type = 1)
        {
            ForeFont = new Font("", 9);
            Text = text;
            TypeId = type;
            switch (type)
            {
                case 1:
                    ForeColor = Color.Blue;
                    ForeFont = new Font("", 10, FontStyle.Bold);
                    break;
                case 2:
                    ForeColor = Color.DarkRed;
                    break;
                case 3:
                    ForeColor = Color.Red;
                    break;
            }
            Desc = Text;
        }

        public string Text { get; set; }
        public string Desc { get; set; }
        public byte TypeId { get; set; }
        public Color ForeColor { get; set; }
        public Font ForeFont { get; set; }

        public override bool Equals(object obj)
        {
            LightWord lw = obj as LightWord;
            if (lw == null) return false;

            return this.Text.Equals(lw.Text);
        }

        public override int GetHashCode()
        {
            return this.Text.GetHashCode();
        }
    }
}
