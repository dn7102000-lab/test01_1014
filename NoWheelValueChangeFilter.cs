using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace test01_1014
{
    /// <summary>
    /// Bộ lọc thông điệp Windows (IMessageFilter) chặn hành vi cuộn chuột (WM_MOUSEWHEEL)
    /// làm thay đổi giá trị trong các ô nhập thông số (NumericUpDown, ComboBox, TrackBar).
    /// Thay vào đó, cuộn chuột khi rê trên các ô này sẽ được chuyển thành cuộn thanh cuộn của panel/form chứa nó.
    /// </summary>
    public sealed class NoWheelValueChangeFilter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_VSCROLL = 0x0115;
        private const int SB_LINEUP = 0;
        private const int SB_LINEDOWN = 1;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL)
                return false;

            // Xác định control nằm ngay dưới con trỏ chuột
            Point screenPt = Cursor.Position;
            IntPtr hWndUnderMouse = WindowFromPoint(screenPt);
            if (hWndUnderMouse == IntPtr.Zero)
                return false;

            Control? target = Control.FromChildHandle(hWndUnderMouse) ?? Control.FromHandle(hWndUnderMouse);
            if (target == null)
                return false;

            // Kiểm tra xem control dưới chuột có phải là NumericUpDown (hoặc UpDownEdit con của nó),
            // ComboBox, hay TrackBar không
            bool isParamField = IsParameterInputField(target, out Control? topField);
            if (!isParamField || topField == null)
                return false;

            // Chặn control tự đổi giá trị
            // Thay vào đó, gửi cuộn lên ScrollableControl cha (ví dụ PropertyPalette panel, dialog form, flow panel)
            short delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            ScrollParentContainer(topField, delta);

            return true; // Đã xử lý (chặn không cho NumericUpDown / ComboBox / TrackBar nhận WM_MOUSEWHEEL)
        }

        private static bool IsParameterInputField(Control c, out Control? topField)
        {
            topField = null;
            Control? cur = c;
            while (cur != null)
            {
                if (cur is NumericUpDown || cur is ComboBox || cur is TrackBar)
                {
                    topField = cur;
                    return true;
                }
                // Nếu là control con bên trong NumericUpDown (UpDownEdit, UpDownButtons)
                if (cur.Parent is NumericUpDown np)
                {
                    topField = np;
                    return true;
                }
                cur = cur.Parent;
            }
            return false;
        }

        private static void ScrollParentContainer(Control child, short delta)
        {
            Control? parent = child.Parent;
            while (parent != null)
            {
                if (parent is ScrollableControl sc && (sc.AutoScroll || sc.VerticalScroll.Visible))
                {
                    // Cuộn container
                    int lines = Math.Max(1, Math.Abs(delta) / 40);
                    IntPtr scrollCmd = delta > 0 ? (IntPtr)SB_LINEUP : (IntPtr)SB_LINEDOWN;
                    for (int i = 0; i < lines; i++)
                    {
                        SendMessage(sc.Handle, WM_VSCROLL, scrollCmd, IntPtr.Zero);
                    }
                    return;
                }
                parent = parent.Parent;
            }
        }
    }
}
