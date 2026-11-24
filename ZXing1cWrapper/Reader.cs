using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ZXing;

namespace ZXing1cWrapper
{
    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    [Guid("D107A07E-28F8-4A1C-8474-05A932E4B892")]
    public interface IBarcodeReader
    {
        string Decode(string filePath);
        string ShowCropDialog(string filePath);
        void SetCropArea(int x, int y, int width, int height);
        void ResetCropArea();
        void SetFormatsBarCode(string formatsCommaSeparated);
        void SetCropAreaPercent(double xPct, double yPct, double wPct, double hPct);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ZXingWrapper.Reader")]
    [Guid("18C44031-6BC0-42DE-81F1-2B47DB2F6B34")]
    public class Reader : IBarcodeReader
    {
        private Rectangle _cropArea = Rectangle.Empty;
        private List<BarcodeFormat> _formats = new List<BarcodeFormat>();
        private double _cropPx = 0, _cropPy = 0, _cropPw = 0, _cropPh = 0;

        public void SetCropArea(int x, int y, int width, int height)
        {
            _cropArea = new Rectangle(x, y, width, height);
            _cropPx = _cropPy = _cropPw = _cropPh = 0;
        }

        public void ResetCropArea()
        {
            _cropArea = Rectangle.Empty;
            _cropPx = _cropPy = _cropPw = _cropPh = 0;
        }

        public void SetCropAreaPercent(double xPct, double yPct, double wPct, double hPct)
        {
            _cropPx = xPct;
            _cropPy = yPct;
            _cropPw = wPct;
            _cropPh = hPct;
            _cropArea = Rectangle.Empty;
        }

        public void SetFormatsBarCode(string formatsCommaSeparated)
        {
            _formats.Clear();
            if (string.IsNullOrWhiteSpace(formatsCommaSeparated)) return;

            foreach (string f in formatsCommaSeparated.Split(','))
            {
                if (Enum.TryParse(f.Trim(), true, out BarcodeFormat format))
                    _formats.Add(format);
            }
        }

        public string Decode(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath), "Путь к файлу не указан.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".pdf":
                    return ProcessPdf(filePath);

                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    return ProcessImage(filePath);

                default:
                    throw new NotSupportedException($"Формат файла '{ext}' не поддерживается. Разрешены только PDF и изображения.");
            }
        }

        private string ProcessPdf(string pdfPath)
        {
            List<string> results = new List<string>();

            using (var doc = PdfDocument.Load(pdfPath))
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using (Bitmap pageBmp = (Bitmap)doc.Render(i, 300, 300, PdfRenderFlags.CorrectFromDpi))
                    {
                        string code = DecodeBitmap(pageBmp);
                        results.Add(string.IsNullOrEmpty(code) ? "NOT_FOUND" : code);
                    }
                }
            }

            return string.Join(";", results);
        }

        private string ProcessImage(string imagePath)
        {
            using (Bitmap img = (Bitmap)Image.FromFile(imagePath))
            {
                string code = DecodeBitmap(img);
                return string.IsNullOrEmpty(code) ? "NOT_FOUND" : code;
            }
        }

        private string DecodeBitmap(Bitmap original)
        {
            Bitmap cropped = null;
            bool isCloned = false;

            try
            {
                // 1. Вырезаем область (Кроп) - ОСТАВЛЯЕМ БЕЗ ИЗМЕНЕНИЙ
                if (_cropPw > 0 && _cropPh > 0)
                {
                    int startX = (int)(original.Width * _cropPx);
                    int startY = (int)(original.Height * _cropPy);
                    int cropW = (int)(original.Width * _cropPw);
                    int cropH = (int)(original.Height * _cropPh);

                    if (startX + cropW >= original.Width) cropW = original.Width - startX - 1;
                    if (startY + cropH >= original.Height) cropH = original.Height - startY - 1;

                    if (cropW > 0 && cropH > 0)
                    {
                        Rectangle crop = new Rectangle(startX, startY, cropW, cropH);
                        cropped = original.Clone(crop, original.PixelFormat);
                        isCloned = true;
                    }
                }
                else if (_cropArea != Rectangle.Empty)
                {
                    Rectangle crop = _cropArea;
                    crop.Intersect(new Rectangle(0, 0, original.Width, original.Height));

                    if (crop.Width > 0 && crop.Height > 0)
                    {
                        cropped = original.Clone(crop, original.PixelFormat);
                        isCloned = true;
                    }
                }

                if (cropped == null)
                {
                    cropped = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(cropped))
                        g.DrawImage(original, 0, 0, original.Width, original.Height);
                    isCloned = true;
                }

                var reader = new BarcodeReader
                {
                    AutoRotate = true
                };
                reader.Options.TryHarder = true;
                reader.Options.PureBarcode = false;
                reader.Options.TryInverted = false;
                if (_formats.Count > 0)
                    reader.Options.PossibleFormats = _formats;

                // ==========================================
                // ЗЕРКАЛЬНОЕ ОТРАЖЕНИЕ POWERSHELL-СКРИПТА
                // ==========================================

                // ШАГ 1: БЫСТРЫЙ ТЕСТ
                var result = reader.Decode(cropped);
                if (result != null) return result.Text;

                // ШАГ 2: БИНАРИЗАЦИЯ ОРИГИНАЛА (Прямо в исходной переменной cropped!)
                BinarizeInPlace(cropped, 200);
                result = reader.Decode(cropped);
                if (result != null) return result.Text;

                // ШАГ 3: МАСШТАБИРОВАНИЕ (Увеличиваем УЖЕ бинаризованную картинку)
                int scaledW = (int)(cropped.Width * 2.5);
                int scaledH = (int)(cropped.Height * 2.5);

                using (Bitmap scaled = new Bitmap(scaledW, scaledH, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        // HighQualityBicubic сгладит жесткие пиксели после первой бинаризации
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(cropped, 0, 0, scaledW, scaledH);
                    }

                    // ТЕСТ ЧИСТОГО АПСКЕЙЛА (БЕЗ ПОЛЕЙ)
                    result = reader.Decode(scaled);
                    if (result != null) return result.Text;

                    // ШАГ 4: БИНАРИЗАЦИЯ УВЕЛИЧЕННОЙ КОПИИ
                    BinarizeInPlace(scaled, 200);

                    // ДОБАВЛЕНИЕ БЕЛЫХ ПОЛЕЙ
                    int paddingX = 50;
                    int paddingY = 30;
                    int newWidth = scaledW + (paddingX * 2);
                    int newHeight = scaledH + (paddingY * 2);

                    using (Bitmap paddedAfter = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics gAfter = Graphics.FromImage(paddedAfter))
                        {
                            gAfter.Clear(Color.White);
                            // Отрисовываем бинаризованный scaled по центру
                            gAfter.DrawImage(scaled, paddingX, paddingY, scaledW, scaledH);
                        }

                        // ДЕКОДИРОВАНИЕ С БИНАРИЗАЦИЕЙ И ПОЛЯМИ
                        result = reader.Decode(paddedAfter);
                        if (result != null) return result.Text;
                    } // paddedAfter.Dispose() отрабатывает автоматически

                    // ШАГ 5: ИНВЕРСИЯ
                    // В скрипте инверсия ищется на $scaled (БЕЗ белых полей)
                    reader.Options.TryInverted = true;
                    result = reader.Decode(scaled);
                    if (result != null) return result.Text;
                }

                return null;
            }
            finally
            {
                if (isCloned && cropped != null)
                {
                    cropped.Dispose();
                }
            }
        }

        // алгоритм ImageProcessor, адаптированный под любой формат пикселей (24bpp и 32bpp)
        private static void BinarizeInPlace(Bitmap bmp, byte threshold)
        {
            int bytesPerPixel = Image.GetPixelFormatSize(bmp.PixelFormat) / 8;
            if (bytesPerPixel < 3) return; // На случай если картинка уже 1bpp/8bpp

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

            try
            {
                int stride = bmpData.Stride;
                int totalBytes = Math.Abs(stride) * bmp.Height;
                byte[] rgbValues = new byte[totalBytes];

                Marshal.Copy(bmpData.Scan0, rgbValues, 0, totalBytes);

                for (int y = 0; y < bmp.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int i = rowOffset + (x * bytesPerPixel);
                        // Формула люминесценции (B, G, R)
                        byte gray = (byte)(rgbValues[i] * 0.11 + rgbValues[i + 1] * 0.59 + rgbValues[i + 2] * 0.3);
                        byte bw = (gray < threshold) ? (byte)0 : (byte)255;
                        rgbValues[i] = rgbValues[i + 1] = rgbValues[i + 2] = bw;
                    }
                }

                Marshal.Copy(rgbValues, 0, bmpData.Scan0, totalBytes);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        public string ShowCropDialog(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            Image imgToShow = null;

            try
            {
                if (ext == ".pdf")
                {
                    using (var doc = PdfDocument.Load(filePath))
                    {
                        imgToShow = (Bitmap)doc.Render(0, 300, 300, PdfRenderFlags.CorrectFromDpi);
                    }
                }
                else
                {
                    imgToShow = Image.FromFile(filePath);
                }

                using (var form = new CropForm(imgToShow))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        var r = form.SelectedArea;
                        // return $"{r.X};{r.Y};{r.Width};{r.Height}";
                        // Переводим пиксели в проценты(от 0.0 до 1.0)
                        // 1. Точные доли (то, что пользователь выделил впритирку)
                        double baseX = (double)r.X / imgToShow.Width;
                        double baseY = (double)r.Y / imgToShow.Height;
                        double baseW = (double)r.Width / imgToShow.Width;
                        double baseH = (double)r.Height / imgToShow.Height;

                        // 2. Умное "раздутие" рамки (создаем огромную ловушку)
                        // 20% ширины листа и 10% высоты листа
                        double padX = 0.20;
                        double padY = 0.10;

                        // Сдвигаем левую и верхнюю границы (но не уходим в минус)
                        double outX = Math.Max(0, baseX - padX);
                        double outY = Math.Max(0, baseY - padY);

                        // Растягиваем ширину и высоту (но не вылезаем за правый/нижний край картинки)
                        double outW = Math.Min(1.0 - outX, baseW + (padX * 2));
                        double outH = Math.Min(1.0 - outY, baseH + (padY * 2));

                        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                        "{0:F4};{1:F4};{2:F4};{3:F4}", outX, outY, outW, outH);
                    }
                    return string.Empty;
                }
            }
            finally
            {
                imgToShow?.Dispose();
            }
        }
    }


    // --- ФОРМА ВЫДЕЛЕНИЯ ОБЛАСТИ МЫШКОЙ ---
    internal class CropForm : Form
    {
        public Rectangle SelectedArea { get; private set; }
        private PictureBox pb;
        private Point startPoint;
        private Rectangle currentRect;
        private bool isDragging;

        public CropForm(Image img)
        {
            Text = "Выделите область ШК мышью и нажмите Enter (Esc для отмены)";
            WindowState = FormWindowState.Maximized;
            AutoScroll = true;

            pb = new PictureBox
            {
                Image = img,
                SizeMode = PictureBoxSizeMode.AutoSize,
                Cursor = Cursors.Cross
            };

            pb.MouseDown += Pb_MouseDown;
            pb.MouseMove += Pb_MouseMove;
            pb.MouseUp += Pb_MouseUp;
            pb.Paint += Pb_Paint;

            Controls.Add(pb);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && currentRect.Width > 0 && currentRect.Height > 0)
            {
                SelectedArea = currentRect;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Pb_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                startPoint = e.Location;
                currentRect = new Rectangle(startPoint, new Size(0, 0));
            }
        }

        private void Pb_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                int x = Math.Min(startPoint.X, e.X);
                int y = Math.Min(startPoint.Y, e.Y);
                int w = Math.Max(startPoint.X, e.X) - x;
                int h = Math.Max(startPoint.Y, e.Y) - y;
                currentRect = new Rectangle(x, y, w, h);
                pb.Invalidate();
            }
        }

        private void Pb_MouseUp(object sender, MouseEventArgs e) => isDragging = false;

        private void Pb_Paint(object sender, PaintEventArgs e)
        {
            if (currentRect.Width > 0 && currentRect.Height > 0)
            {
                using (Pen pen = new Pen(Color.Red, 2))
                    e.Graphics.DrawRectangle(pen, currentRect);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, Color.Blue)))
                    e.Graphics.FillRectangle(brush, currentRect);
            }
        }
    }
}