// =====================================================================
//  Form1_Files.cs  -  TỆP: lưu / mở dự án (.lzr) và tự động lưu bản nháp
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Lưu / mở dự án   (trước đây: Form1_Project.cs)
//    2. Tự động lưu bản nháp (autosave)   (trước đây: Form1.AutoSave.cs)
// =====================================================================

// =====================================================================
// PHẦN 1: Lưu / mở dự án   (trước đây là file Form1_Project.cs)
// =====================================================================
// =====================================================================
//  Form1_Project.cs  -  Quản lý file dự án .lzr
//    Tệp > Mới / Mở / Lưu / Lưu thành, hỏi lưu khi thoát / mở / tạo mới,
//    tiêu đề cửa sổ hiện tên file và dấu * khi chưa lưu.
//
//  Cách nhận biết "chưa lưu": so "chữ ký nội dung" hiện tại với chữ ký lúc lưu.
//  Chữ ký chỉ gồm NỘI DUNG dự án (hình, chữ, ảnh, thông số laser),
//  KHÔNG gồm zoom/pan/lưới/đối tượng đang chọn -> kéo màn hình không bị tính là thay đổi.
//  Undo về đúng trạng thái đã lưu thì dấu * tự biến mất.
// =====================================================================
namespace test01_1014
{
    using System.Runtime.CompilerServices;
    using System.Text.Json;

    public partial class Form1
    {
        private const string AppTitle = "Laser Studio";
        private const string ProjectFilter = "Laser Project (*.lzr)|*.lzr|JSON File (*.json)|*.json";

        private string? currentFilePath;          // null = chưa đặt tên
        private int savedContentSig;              // chữ ký nội dung lúc lưu gần nhất
        private bool forceDirty;                  // true sau khi khôi phục bản nháp (chưa có file)
        private string lastTitle = "";
        private System.Windows.Forms.Timer? titleTimer;

        private string DisplayName => currentFilePath == null ? "Chưa đặt tên" : Path.GetFileName(currentFilePath);
        private bool IsDirty => forceDirty || ComputeContentSignature() != savedContentSig;

        // ------------------------------------------------------------------
        //  Khởi tạo: gọi 1 lần trong constructor, ngay sau InitializeComponent()
        // ------------------------------------------------------------------
        private void InitProjectFile()
        {
            // --- Menu Tệp: đổi tên + phím tắt + nối lại sự kiện ---
            // (tên biến trong Designer đang đặt lệch: openToolStripMenuItem thực ra là "New"...)
            var miNew = openToolStripMenuItem;
            var miOpen = saveToolStripMenuItem;
            var miSave = saveToolStripMenuItem1;
            var miSaveAs = saveAsToolStripMenuItem;

            // gỡ sự kiện cũ do Designer gắn
            miNew.Click -= NewtoolStripMenuItem1_Click;
            miOpen.Click -= openToolStripMenuItem_Click;
            miSave.Click -= saveToolStripMenuItem1_Click;
            miSaveAs.Click -= saveToolStripMenuItem_Click;

            fiieToolStripMenuItem.Text = "Tệp";
            miNew.Text = "Mới"; miNew.ShortcutKeys = Keys.Control | Keys.N;
            miOpen.Text = "Mở..."; miOpen.ShortcutKeys = Keys.Control | Keys.O;
            miSave.Text = "Lưu"; miSave.ShortcutKeys = Keys.Control | Keys.S;
            miSaveAs.Text = "Lưu thành..."; miSaveAs.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;

            miNew.Click += MenuNew;
            miOpen.Click += MenuOpen;
            miSave.Click += (s, e) => SaveProject();
            miSaveAs.Click += (s, e) => SaveProjectAs();

            // Menu Nhập Vector (SVG, DXF)
            var miImportVector = new ToolStripMenuItem("Nhập Vector (SVG, DXF)...")
            {
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.I
            };
            miImportVector.Click += (s, e) => MenuImportVector();

            // Chèn sau mục Mở
            int openIdx = fiieToolStripMenuItem.DropDownItems.IndexOf(miOpen);
            if (openIdx >= 0) fiieToolStripMenuItem.DropDownItems.Insert(openIdx + 1, miImportVector);
            else fiieToolStripMenuItem.DropDownItems.Add(miImportVector);

            // dòng kẻ + Thoát
            var miExit = new ToolStripMenuItem("Thoát") { ShortcutKeyDisplayString = "Alt+F4" };
            miExit.Click += (s, e) => Close();
            fiieToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            fiieToolStripMenuItem.DropDownItems.Add(miExit);

            // --- Trạng thái ban đầu: dự án trống = sạch ---
            MarkClean();

            // --- Cập nhật tiêu đề định kỳ (rẻ, 0.3s/lần) ---
            titleTimer = new System.Windows.Forms.Timer { Interval = 300 };
            titleTimer.Tick += (s, e) => UpdateTitle();
            titleTimer.Start();
        }

        // ------------------------------------------------------------------
        //  Tiêu đề + trạng thái sạch/bẩn
        // ------------------------------------------------------------------
        private void UpdateTitle()
        {
            string t = $"{DisplayName}{(IsDirty ? " *" : "")} - {AppTitle}";
            if (t != lastTitle) { lastTitle = t; Text = t; }
        }

        private void MarkClean()
        {
            savedContentSig = ComputeContentSignature();
            forceDirty = false;
            UpdateTitle();
        }

        // Gọi sau khi khôi phục bản nháp: chưa có file thật nên coi như "chưa lưu"
        private void MarkDirtyAfterRestore()
        {
            currentFilePath = null;
            forceDirty = true;
            UpdateTitle();
        }

        private int ComputeContentSignature()
        {
            var h = new HashCode();
            h.Add(projectItems.Count);
            h.Add(LayersSignature());
            foreach (var it in projectItems)
            {
                h.Add(it.GetType().Name);
                h.Add(it.X); h.Add(it.Y);
                h.Add(it.LaserPower); h.Add(it.LaserSpeed); h.Add(it.Passes);
                h.Add(ItemExtrasHash(it));

                if (it is VectorText t)
                {   // Width/Height của chữ tự đổi mỗi lần vẽ -> không đưa vào chữ ký
                    h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize);
                }
                else { h.Add(it.Width); h.Add(it.Height); }

                if (it is VectorLine l) { h.Add(l.X2); h.Add(l.Y2); }
                else if (it is VectorPolygon p) h.Add((int)p.Kind);
                else if (it is VectorPathItem pi) h.Add(pi.SubPaths.Count);
                else if (it is VectorImage im && im.LoadedImage != null)
                    h.Add(RuntimeHelpers.GetHashCode(im.LoadedImage));
            }
            return h.ToHashCode();
        }

        // ------------------------------------------------------------------
        //  Hỏi lưu: true = được phép tiếp tục, false = người dùng bấm Hủy / lưu thất bại
        // ------------------------------------------------------------------
        private bool ConfirmSaveChanges()
        {
            if (!IsDirty) return true;

            var rs = MessageBox.Show(this,
                $"Lưu thay đổi vào \"{DisplayName}\" không?",
                AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if (rs == DialogResult.Cancel) return false;
            if (rs == DialogResult.Yes) return SaveProject();
            return true;     // No: bỏ thay đổi
        }

        // ------------------------------------------------------------------
        //  Mới / Mở
        // ------------------------------------------------------------------
        private void MenuNew(object? sender, EventArgs e)
        {
            if (ReadOnlyView) SetActiveTab(null);
            if (!ConfirmSaveChanges()) return;

            CancelInteractions();
            projectItems.Clear();
            ResetLayers();
            selectedItem = null!;
            undoStack.Clear(); redoStack.Clear();
            currentFilePath = null;
            UpdatePropertiesToUI();
            ResetView();
            MarkClean();
        }

        private void MenuOpen(object? sender, EventArgs e)
        {
            if (ReadOnlyView) SetActiveTab(null);
            if (!ConfirmSaveChanges()) return;

            using var ofd = new OpenFileDialog
            {
                Filter = "Mọi file được hỗ trợ (*.lzr;*.json;*.svg;*.dxf)|*.lzr;*.json;*.svg;*.dxf|Laser Project (*.lzr)|*.lzr|SVG Vector (*.svg)|*.svg|AutoCAD DXF (*.dxf)|*.dxf|JSON File (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Mở file bản vẽ / dự án"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            string ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
            if (ext == ".svg" || ext == ".dxf")
            {
                ImportVectorFile(ofd.FileName, clearCurrent: true);
                return;
            }

            if (LoadProject(ofd.FileName))
            {
                currentFilePath = ofd.FileName;
                MarkClean();
            }
        }

        private void MenuImportVector()
        {
            if (ReadOnlyView) SetActiveTab(null);

            using var ofd = new OpenFileDialog
            {
                Filter = "File vector laser (*.svg;*.dxf)|*.svg;*.dxf|SVG Vector (*.svg)|*.svg|AutoCAD DXF (*.dxf)|*.dxf|All Files (*.*)|*.*",
                Title = "Nhập bản vẽ Vector (SVG / DXF)"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            ImportVectorFile(ofd.FileName, clearCurrent: false);
        }

        public void ImportVectorFile(string path, bool clearCurrent)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                var imported = new List<VectorPathItem>();

                if (ext == ".svg")
                {
                    var res = SvgImporter.Import(path, MmPerPx);
                    imported.AddRange(res.Items);
                }
                else if (ext == ".dxf")
                {
                    var res = DxfImporter.Import(path, MmPerPx);
                    imported.AddRange(res.Items);
                }
                else
                {
                    MessageBox.Show(this, "Định dạng file không được hỗ trợ: " + ext, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (imported.Count == 0)
                {
                    MessageBox.Show(this, "Không tìm thấy đối tượng vector nào hợp lệ trong file.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!clearCurrent) PushUndo();

                if (clearCurrent)
                {
                    CancelInteractions();
                    projectItems.Clear();
                    selectedItem = null!;
                    undoStack.Clear(); redoStack.Clear();
                    currentFilePath = null;
                }

                // Đặt vào giữa màn hình nếu nhập thêm vào dự án
                var center = ViewCenter();
                float minX = imported.Min(i => i.X);
                float minY = imported.Min(i => i.Y);
                float maxX = imported.Max(i => i.X + i.Width);
                float maxY = imported.Max(i => i.Y + i.Height);
                float curW = maxX - minX, curH = maxY - minY;

                float offsetX = center.X - (minX + curW / 2f);
                float offsetY = center.Y - (minY + curH / 2f);

                foreach (var it in imported)
                {
                    it.MoveTo(it.X + offsetX, it.Y + offsetY);
                    it.LaserPower = 80;
                    it.LaserSpeed = 1000;
                    it.Passes = 1;
                    it.IsSelected = false;
                    projectItems.Add(it);
                }

                // Chọn đối tượng cuối cùng được nhập
                if (imported.Count > 0)
                {
                    selectedItem = imported.Last();
                    selectedItem.IsSelected = true;
                }

                UpdatePropertiesToUI();
                pictureBox1.Invalidate();

                if (clearCurrent)
                {
                    ResetView();
                    MarkDirtyAfterRestore();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi khi nhập file vector:\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Đọc file vào bộ nhớ TRƯỚC, thành công mới thay dự án hiện tại (file hỏng không làm mất dự án đang mở)
        private bool LoadProject(string path)
        {
            try
            {
                ProjectDataDTO? dto = JsonSerializer.Deserialize<ProjectDataDTO>(File.ReadAllText(path));
                if (dto?.Items == null) throw new InvalidDataException("File không đúng định dạng dự án.");

                var items = new List<Vectoritem>();
                foreach (var d in dto.Items)
                {
                    Vectoritem? it = CreateItemFromDto(d);
                    if (it != null) items.Add(it);
                }

                CancelInteractions();
                projectItems.Clear();
                projectItems.AddRange(items);
                LayersFromDto(dto.Layers, items);
                selectedItem = null!;
                undoStack.Clear(); redoStack.Clear();
                UpdatePropertiesToUI();
                pictureBox1.Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không thể mở file dự án:\n" + ex.Message, AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Lưu / Lưu thành
        // ------------------------------------------------------------------
        private bool SaveProject() =>
            currentFilePath == null ? SaveProjectAs() : WriteProject(currentFilePath);

        private bool SaveProjectAs()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = ProjectFilter,
                DefaultExt = "lzr",
                AddExtension = true,
                OverwritePrompt = true,
                Title = "Lưu dự án thành",
                FileName = currentFilePath != null ? Path.GetFileName(currentFilePath) : "Dự án mới.lzr"
            };
            if (currentFilePath != null)
                sfd.InitialDirectory = Path.GetDirectoryName(currentFilePath) ?? "";

            if (sfd.ShowDialog(this) != DialogResult.OK) return false;
            return WriteProject(sfd.FileName);
        }

        private bool WriteProject(string path)
        {
            try
            {
                var dto = new ProjectDataDTO { Name = Path.GetFileNameWithoutExtension(path), Layers = LayersToDto() };
                foreach (var item in projectItems)
                {
                    VectorItemDTO? d = ToItemDto(item);
                    if (d != null) dto.Items.Add(d);
                }

                // ghi ra .tmp rồi đổi tên: mất điện giữa chừng không làm hỏng file cũ
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
                File.Move(tmp, path, true);

                currentFilePath = path;
                MarkClean();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi khi lưu dự án:\n" + ex.Message, AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static VectorItemDTO? ToItemDto(Vectoritem item)
        {
            var d = new VectorItemDTO
            {
                X = item.X,
                Y = item.Y,
                Width = item.Width,
                Height = item.Height,
                LaserPower = item.LaserPower,
                LaserSpeed = item.LaserSpeed,
                Passes = item.Passes
            };

            switch (item)
            {
                case VectorText t:
                    d.Type = "Text"; d.TextContent = t.TextContent; d.FontName = t.FontName; d.FontSize = t.FontSize;
                    break;
                case VectorImage im when im.LoadedImage != null:
                    d.Type = "Image"; d.ImageBase64 = EncodeImage(im.LoadedImage);
                    break;
                case VectorRectangle: d.Type = "Rectangle"; break;
                case VectorEllipse: d.Type = "Ellipse"; break;
                case VectorLine l: d.Type = "Line"; d.X2 = l.X2; d.Y2 = l.Y2; break;
                case VectorPolygon p: d.Type = "Polygon"; d.Shape = p.Kind.ToString(); break;
                case VectorPathItem pi:
                    d.Type = "Path";
                    d.PathDataJson = JsonSerializer.Serialize(pi.SubPaths);
                    break;
                default: return null;
            }
            FillDtoExtras(d, item);
            return d;
        }

        // Thoát bình thường (đã lưu hoặc đã chọn bỏ) thì xóa nháp: nháp chỉ để cứu khi sập máy/mất điện
        private static void DeleteDraft()
        {
            try { if (File.Exists(DraftPath)) File.Delete(DraftPath); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Không xóa được nháp: " + ex.Message); }
        }
    }
}

// =====================================================================
// PHẦN 2: Tự động lưu bản nháp (autosave)   (trước đây là file Form1.AutoSave.cs)
// =====================================================================
// =====================================================================
//  Form1.AutoSave.cs  -  Tự lưu bản nháp (autosave) cho Form1
//
//  Cơ chế:
//   - Cứ mỗi 1 giây kiểm tra "chữ ký" của toàn bộ trạng thái (đối tượng, kích thước,
//     chữ, thông số laser, zoom/pan, lưới, thông số G-code...).
//   - Nếu chữ ký đã đổi và ĐÃ ỔN ĐỊNH (người dùng ngừng thao tác ~1 giây) -> ghi nháp.
//     Nếu cứ thay đổi liên tục thì tối đa 15 giây cũng ghi 1 lần.
//   - Khi đóng form: ghi nháp lần cuối.
//   - Khi mở form: tự khôi phục bản nháp (nếu có).
//   => Không cần gắn vào từng thao tác, thao tác nào cũng được lưu.
//
//  File nháp: %AppData%\test01_1014\draft.lzr  (JSON, tương thích file .lzr)
//  Ghi an toàn: ghi ra file .tmp rồi mới đổi tên, mất điện giữa chừng không hỏng nháp.
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using test01_1014;

    /// <summary>Dữ liệu nháp = dự án + trạng thái giao diện.</summary>
    public class DraftDTO : ProjectDataDTO
    {
        public float Zoom { get; set; } = 1f;
        public float PanX { get; set; }
        public float PanY { get; set; }
        public bool ShowGrid { get; set; } = true;
        public int SelectedIndex { get; set; } = -1;

        // Thông số G-code (form mô phỏng)
        public float GMmPerPixel { get; set; } = 0.25f;
        public int GMaxS { get; set; } = 1000;
        public double GRasterStepMm { get; set; } = 0.1;
        public bool GImageBinary { get; set; } = false;
        public int GImageThreshold { get; set; } = 128;
        public bool GImageInvert { get; set; } = false;
        public int GRapidFeed { get; set; } = 3000;
    }

    public partial class Form1
    {
        // Dùng chung cho các lần mở form G-code để thông số được giữ lại
        private readonly GCodeSettings gcodeCfg = new GCodeSettings();

        private System.Windows.Forms.Timer? autoSaveTimer;
        private int lastSavedSig, pendingSig, changingTicks;

        // Cache Base64 của ảnh (mã hóa PNG khá nặng, ảnh thì hầu như không đổi)
        private static readonly ConditionalWeakTable<System.Drawing.Image, string> b64Cache = new();

        private static string DraftDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014");
        private static string DraftPath => Path.Combine(DraftDir, "draft.lzr");

        // ------------------------------------------------------------------
        //  Vòng đời form
        // ------------------------------------------------------------------
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (DesignMode) return;

            DraftDTO? dto = ReadDraft();
            if (dto != null && dto.Items.Count > 0)
            {
                var rs = MessageBox.Show(this,
                    $"Phát hiện bản làm việc chưa lưu từ lần trước ({dto.Items.Count} đối tượng).\n\n" +
                    "Bạn có muốn khôi phục không?",
                    "Khôi phục bản nháp",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

                if (rs == DialogResult.Yes) ApplyDraft(dto);
                else ArchiveDraft();                       // chọn Không: bắt đầu dự án trống
            }

            // Chỉ bắt đầu theo dõi autosave SAU khi người dùng đã chọn xong
            lastSavedSig = ComputeStateSignature();
            pendingSig = lastSavedSig;

            autoSaveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            autoSaveTimer.Tick += (s, ev) => AutoSaveTick();
            autoSaveTimer.Start();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (autoSaveTimer == null) return;                       // chưa bắt đầu autosave thì không làm gì

            if (!ConfirmSaveChanges()) { e.Cancel = true; return; }  // người dùng bấm Hủy: ở lại

            autoSaveTimer.Stop();
            DeleteDraft();                                           // thoát bình thường: không cần nháp nữa
        }

        private void AutoSaveTick()
        {
            int sig = ComputeStateSignature();
            if (sig == lastSavedSig) { changingTicks = 0; pendingSig = sig; return; }

            changingTicks++;
            if (sig == pendingSig || changingTicks >= 15)   // đã ổn định, hoặc chờ quá lâu
            {
                SaveDraft(sig);
                changingTicks = 0;
            }
            else
            {
                pendingSig = sig;                            // vẫn đang thao tác
            }
        }

        // ------------------------------------------------------------------
        //  Chữ ký trạng thái (rẻ: chỉ băm các con số, không đụng tới ảnh)
        // ------------------------------------------------------------------
        private int ComputeStateSignature()
        {
            var h = new HashCode();
            h.Add(zoom); h.Add(pan.X); h.Add(pan.Y); h.Add(showGrid);
            h.Add(projectItems.IndexOf(selectedItem));
            h.Add(LayersSignature());

            foreach (var it in projectItems)
            {
                h.Add(it.GetType().Name);
                h.Add(it.X); h.Add(it.Y); h.Add(it.Width); h.Add(it.Height);
                h.Add(it.LaserPower); h.Add(it.LaserSpeed); h.Add(it.Passes);
                h.Add(ItemExtrasHash(it));

                if (it is VectorLine l) { h.Add(l.X2); h.Add(l.Y2); }
                else if (it is VectorText t) { h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize); }
                else if (it is VectorPathItem pi) { h.Add(pi.SubPaths.Count); }
                else if (it is VectorImage im && im.LoadedImage != null)
                    h.Add(RuntimeHelpers.GetHashCode(im.LoadedImage));
            }

            h.Add(gcodeCfg.MmPerPixel); h.Add(gcodeCfg.MaxS); h.Add(gcodeCfg.RasterStepMm);
            h.Add(gcodeCfg.ImageBinary); h.Add(gcodeCfg.ImageThreshold); h.Add(gcodeCfg.ImageInvert);
            h.Add(gcodeCfg.RapidFeedForEstimate);
            return h.ToHashCode();
        }

        // ------------------------------------------------------------------
        //  Ghi nháp
        // ------------------------------------------------------------------
        private void SaveDraft(int sig)
        {
            try
            {
                DraftDTO dto = BuildDraft();
                string json = JsonSerializer.Serialize(dto);

                Directory.CreateDirectory(DraftDir);
                string tmp = DraftPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, DraftPath, true);

                lastSavedSig = sig;
                pendingSig = sig;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Autosave lỗi: " + ex.Message);   // lần sau tự thử lại
            }
        }

        private DraftDTO BuildDraft()
        {
            var dto = new DraftDTO
            {
                Layers = LayersToDto(),
                Zoom = zoom,
                PanX = pan.X,
                PanY = pan.Y,
                ShowGrid = showGrid,
                GMmPerPixel = gcodeCfg.MmPerPixel,
                GMaxS = gcodeCfg.MaxS,
                GRasterStepMm = gcodeCfg.RasterStepMm,
                GImageBinary = gcodeCfg.ImageBinary,
                GImageThreshold = gcodeCfg.ImageThreshold,
                GImageInvert = gcodeCfg.ImageInvert,
                GRapidFeed = gcodeCfg.RapidFeedForEstimate
            };

            foreach (var item in projectItems)
            {
                var d = new VectorItemDTO
                {
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    LaserPower = item.LaserPower,
                    LaserSpeed = item.LaserSpeed,
                    Passes = item.Passes
                };

                if (item is VectorText txt)
                {
                    d.Type = "Text"; d.TextContent = txt.TextContent;
                    d.FontName = txt.FontName; d.FontSize = txt.FontSize;
                }
                else if (item is VectorImage img && img.LoadedImage != null)
                {
                    d.Type = "Image";
                    d.ImageBase64 = EncodeImage(img.LoadedImage);
                }
                else if (item is VectorRectangle) d.Type = "Rectangle";
                else if (item is VectorEllipse) d.Type = "Ellipse";
                else if (item is VectorLine ln) { d.Type = "Line"; d.X2 = ln.X2; d.Y2 = ln.Y2; }
                else if (item is VectorPolygon pg) { d.Type = "Polygon"; d.Shape = pg.Kind.ToString(); }
                else if (item is VectorPathItem pi)
                {
                    d.Type = "Path";
                    d.PathDataJson = JsonSerializer.Serialize(pi.SubPaths);
                }
                else continue;

                FillDtoExtras(d, item);
                if (item == selectedItem) dto.SelectedIndex = dto.Items.Count;
                dto.Items.Add(d);
            }
            return dto;
        }

        private static string EncodeImage(System.Drawing.Image img)
        {
            if (b64Cache.TryGetValue(img, out string? cached) && cached != null) return cached;
            using var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            string s = Convert.ToBase64String(ms.ToArray());
            b64Cache.Add(img, s);
            return s;
        }

        // ------------------------------------------------------------------
        //  Khôi phục nháp
        // ------------------------------------------------------------------
        // Đọc file nháp; lỗi thì đổi tên thành .bad để kiểm tra, trả về null
        private DraftDTO? ReadDraft()
        {
            try
            {
                if (!File.Exists(DraftPath)) return null;
                DraftDTO? dto = JsonSerializer.Deserialize<DraftDTO>(File.ReadAllText(DraftPath));
                return dto?.Items == null ? null : dto;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Không đọc được bản nháp: " + ex.Message);
                try { File.Move(DraftPath, DraftPath + ".bad", true); } catch { }
                return null;
            }
        }

        // Người dùng chọn "Không": cất bản nháp cũ sang .old (không xóa hẳn)
        private void ArchiveDraft()
        {
            try { File.Move(DraftPath, DraftPath + ".old", true); }
            catch (Exception ex) { Debug.WriteLine("Không cất được bản nháp: " + ex.Message); }
        }

        // Người dùng chọn "Có": nạp dữ liệu vào form
        private void ApplyDraft(DraftDTO dto)
        {
            try
            {
                projectItems.Clear();
                selectedItem = null!;

                var created = new List<Vectoritem?>();
                foreach (var d in dto.Items)
                {
                    Vectoritem? it = CreateItemFromDto(d);
                    created.Add(it);
                    if (it != null) projectItems.Add(it);
                }

                LayersFromDto(dto.Layers, projectItems);
                foreach (var it in projectItems) it.IsSelected = false;
                if (dto.SelectedIndex >= 0 && dto.SelectedIndex < created.Count && created[dto.SelectedIndex] != null)
                {
                    selectedItem = created[dto.SelectedIndex]!;
                    selectedItem.IsSelected = true;
                }

                zoom = Math.Clamp(dto.Zoom, 0.1f, 20f);
                pan = new PointF(dto.PanX, dto.PanY);
                showGrid = dto.ShowGrid;

                gcodeCfg.MmPerPixel = dto.GMmPerPixel;
                gcodeCfg.MaxS = dto.GMaxS;
                gcodeCfg.RasterStepMm = dto.GRasterStepMm;
                gcodeCfg.ImageBinary = dto.GImageBinary;
                gcodeCfg.ImageThreshold = dto.GImageThreshold;
                gcodeCfg.ImageInvert = dto.GImageInvert;
                gcodeCfg.RapidFeedForEstimate = dto.GRapidFeed;

                undoStack.Clear();
                redoStack.Clear();
                UpdatePropertiesToUI();
                pictureBox1.Invalidate();
                MarkDirtyAfterRestore();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Lỗi khôi phục bản nháp: " + ex.Message);
            }
        }

        private static Vectoritem? CreateItemFromDto(VectorItemDTO d)
        {
            Vectoritem? it = null;
            switch (d.Type)
            {
                case "Text":
                    it = new VectorText
                    {
                        TextContent = string.IsNullOrEmpty(d.TextContent) ? "Văn bản" : d.TextContent,
                        FontName = string.IsNullOrEmpty(d.FontName) ? "Arial" : d.FontName,
                        FontSize = d.FontSize > 0 ? d.FontSize : 18f
                    };
                    break;
                case "Rectangle": it = new VectorRectangle(); break;
                case "Ellipse": it = new VectorEllipse(); break;
                case "Line": it = new VectorLine { X2 = d.X2, Y2 = d.Y2 }; break;
                case "Polygon":
                    if (Enum.TryParse<PolygonKind>(d.Shape, out var pk)) it = new VectorPolygon(pk);
                    break;
                case "Path":
                    if (!string.IsNullOrEmpty(d.PathDataJson))
                    {
                        try
                        {
                            var subs = JsonSerializer.Deserialize<List<VectorPathItem.SubPath>>(d.PathDataJson);
                            if (subs != null) it = new VectorPathItem(subs, string.IsNullOrEmpty(d.Name) ? "Đường dẫn Vector" : d.Name);
                        }
                        catch { }
                    }
                    if (it == null) it = new VectorPathItem();
                    break;
                case "Image":
                    if (!string.IsNullOrEmpty(d.ImageBase64))
                    {
                        byte[] bytes = Convert.FromBase64String(d.ImageBase64);
                        using var ms = new MemoryStream(bytes);
                        using var tmp = System.Drawing.Image.FromStream(ms);
                        var bmp = new System.Drawing.Bitmap(tmp);                // copy ra khỏi stream
                        it = !string.IsNullOrEmpty(d.BarcodeText) && Enum.TryParse<BarcodeKind>(d.BarcodeType, out var bk)
                            ? new VectorBarcode(bmp, d.BarcodeText, bk, d.BarcodeQuiet, d.BarcodeShowText)   // mã vạch: dựng lại từ ảnh đã lưu
                            : new VectorImage(bmp);
                    }
                    break;
            }
            if (it == null) return null;

            it.X = d.X; it.Y = d.Y;
            if (it is not VectorText) { it.Width = d.Width; it.Height = d.Height; }
            it.LaserPower = d.LaserPower; it.LaserSpeed = d.LaserSpeed; it.Passes = d.Passes;
            ApplyDtoExtras(it, d);
            return it;
        }
    }
}
