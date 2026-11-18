using System.Windows.Forms;

namespace test01_1014
{
    // PictureBox có thể nhận focus để bắt sự kiện lăn chuột (zoom) và phím tắt.
    // Thêm các "móc" để công cụ vẽ / snap hoạt động mà không phải sửa các hàm chuột của Form1:
    //   PreMouseDown / PreMouseUp : chạy TRƯỚC; trả về true = đã xử lý, Form1 không nhận sự kiện
    //   PostMouseMove             : chạy SAU khi Form1 xử lý di chuyển chuột
    //   OverlayPaint              : vẽ thêm lên trên cùng, sau khi Form1 đã vẽ xong
    public class CanvasBox : PictureBox
    {
        public Func<MouseEventArgs, bool>? PreMouseDown;
        public Func<MouseEventArgs, bool>? PreMouseUp;
        public Action<MouseEventArgs>? PostMouseMove;
        public event PaintEventHandler? OverlayPaint;

        public CanvasBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (PreMouseDown?.Invoke(e) == true) return;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (PreMouseUp?.Invoke(e) == true) return;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PostMouseMove?.Invoke(e);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);                 // Form1 vẽ lưới, thước, đối tượng ở đây (sự kiện Paint)
            OverlayPaint?.Invoke(this, pe);   // rồi mới tới lớp phủ của em
        }
    }
}