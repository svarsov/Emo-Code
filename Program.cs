using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

// =============================================================
// Приложение: Image Text Embed/Extract (Windows Forms)
//
// Описание:
// Това е GUI приложение за вграждане и извличане на UTF-8 текст в/от
// PNG и JPEG изображения. Поддържа два подхода за вграждане:
//  1) In-place: опитва се да презапише съществуващ metadata (iTXt chunk за PNG,
//     COM segment за JPEG) без промяна на общия размер на файла. Това е полезно
//     когато желаете да запазите битово идентичен размер (напр. да не се модифицира
//     индекс/хеш). Ограничението е пространството в съществуващия chunk/segment.
//  2) Resize: вкарване на нов iTXt chunk (PNG) или COM segment (JPEG), което променя
//     размера на файла и винаги работи, но променя байтовата дължина на файла.
//
// Технически детайли (обобщение):
// - PNG: форматът се дели на chunk-ове. Всеки chunk има структуриран формат:
//   [length:4 BE][type:4 ASCII][data:length][crc:4 BE]
//   iTXt е текстов chunk форматиран като: keyword\0 compressionFlag compressionMethod languageTag\0 translatedKeyword\0 text(UTF-8)
//   Тук не се поддържа компресирана iTXt (compressionFlag != 0) — кодът очаква ненатиснат текст.
// - JPEG: метаданните като COM (comment) са сегменти със следната структура:
//   0xFF 0xFE [len:2 BE] data[len-2]
//   Лесно е да се вмъкне COM сегмент точно след SOI (0xFFD8) — това променя размера.
//   При in-place търсим съществуващ COM и заменяме payload-а ако има достатъчно място.
//
// Ограничения и предпазни мерки:
// - При in-place вграждане няма възстановителна версия — ако операцията не успее,
//   файлът не се променя. При успешна in-place операция CRC (за PNG) се презаписва.
// - При resize операция оригиналният файл се презаписва без резервно копие; ако искате
//   резервно копие, направете копие на пътя преди вграждане.
// - JPEG COM сегментът може да съдържа до 65533 байта data (2 байта за дължина включени).
// - Кодът не обработва всички възможни PNG сценарии (напр. множество iTXt chunk-ове,
//   или компресирани iTXt) — при нужда от по-широка съвместимост можете да използвате
//   библиотека за PNG манипулации.
//
// Примери:
// - Вгради текст без промяна на размер (ако има място): стартирай приложението -> Browse -> избери image -> напиши текст -> Embed (in-place)
// - Вгради текст и разреши промяна на размера: избери Embed (allow size change)
// - Извлечи вграден текст: избери image -> Extract
//
// Език/технологии: C# (.NET Windows Forms)
// Автор: генерирано/адаптирано автоматично (пояснения на български)
// =============================================================

// Точка на влизане в приложението
// Този клас съдържа метода Main който инициализира и стартира
// Windows Forms приложението. Няма конзолен изход — приложението
// работи изцяло като GUI (WinExe). Ако е необходимо дебъгване,
// логове могат да се показват в полето 'Output' на формата.
static class ProgramEntry
{
    [STAThread]
    static void Main()
    {
        // Run Windows Forms UI without creating a console window
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

// (BMP LSB helpers were merged into the main ImageHelpers class below)

class MainForm : Form
{
    // Основна форма (UI) на приложението.
    // Елементите на интерфейса са:
    // - txtPath: текстово поле за пътя до изображението
    // - btnBrowse: бутон за избор на файл (диалог)
    // - txtText: многострочно поле за въвеждане на текста, който да бъде вграден
    // - btnEmbedInPlace: опция за вграждане без промяна на размера (опитва да презапише съществуващ chunk/segment)
    // - btnEmbedResize: опция за вграждане, която разрешава промяна на размера (вкарва нов chunk/segment)
    // - btnExtract: извлича вграден текст от изображението
    // - txtOutput: read-only поле за съобщения/резултат
    TextBox txtPath;
    Button btnBrowse;
    TextBox txtText;
    Button btnEmbedInPlace;
    Button btnEmbedResize;
    Button btnExtract;
    Button btnEmbedLSB;
    Button btnExtractLSB;
    PictureBox picPreview;
    TextBox txtOutput;

    public MainForm()
    {
        Text = "Image Text Embed/Extract";
        Width = 700;
        Height = 500;

        var lblPath = new Label { Left = 10, Top = 15, Text = "Image path:", AutoSize = true };
        txtPath = new TextBox { Left = 100, Top = 10, Width = 450 };
        btnBrowse = new Button { Left = 560, Top = 8, Width = 100, Text = "Browse..." };
        btnBrowse.Click += BtnBrowse_Click;

        var lblText = new Label { Left = 10, Top = 50, Text = "Text to embed:", AutoSize = true };
        txtText = new TextBox { Left = 100, Top = 45, Width = 560, Height = 120, Multiline = true, ScrollBars = ScrollBars.Vertical };

        btnEmbedInPlace = new Button { Left = 100, Top = 180, Width = 200, Text = "Embed (in-place)" };
        btnEmbedInPlace.Click += BtnEmbedInPlace_Click;
        btnEmbedResize = new Button { Left = 310, Top = 180, Width = 200, Text = "Embed (allow size change)" };
        btnEmbedResize.Click += BtnEmbedResize_Click;
        btnExtract = new Button { Left = 520, Top = 180, Width = 140, Text = "Extract" };
        btnExtract.Click += BtnExtract_Click;
        btnEmbedLSB = new Button { Left = 100, Top = 210, Width = 200, Text = "Embed LSB (BMP)" };
        btnEmbedLSB.Click += BtnEmbedLSB_Click;
        btnExtractLSB = new Button { Left = 310, Top = 210, Width = 200, Text = "Extract LSB (BMP)" };
        btnExtractLSB.Click += BtnExtractLSB_Click;

        var lblOutput = new Label { Left = 320, Top = 220, Text = "Output:", AutoSize = true };

        // Picture preview (supports PNG, JPEG, BMP)
        picPreview = new PictureBox { Left = 10, Top = 245, Width = 300, Height = 200, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };
        txtOutput = new TextBox { Left = 320, Top = 245, Width = 350, Height = 200, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

        Controls.AddRange(new Control[] { lblPath, txtPath, btnBrowse, lblText, txtText, btnEmbedInPlace, btnEmbedResize, btnExtract, btnEmbedLSB, btnExtractLSB, lblOutput, txtOutput, picPreview });
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        // Обработчик за бутона "Browse...". Показва стандартен диалог
        // за избор на файл и попълва txtPath с избрания път.
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtPath.Text = dlg.FileName;
            try { LoadPreview(dlg.FileName); } catch { /* ignore preview errors */ }
        }
    }

    void LoadPreview(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") { picPreview.Image = null; return; }
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            picPreview.Image?.Dispose();
            picPreview.Image = new Bitmap(img);
        }
        catch { picPreview.Image = null; }
    }

    private void BtnEmbedLSB_Click(object? sender, EventArgs e)
    {
        // Вграждане чрез LSB в BMP файл (по подобие на предоставения C код)
        var path = txtPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ShowOutput("Файлът не съществува."); return; }
        var text = txtText.Text ?? string.Empty;
        try
        {
            var outPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "-lsb" + Path.GetExtension(path));
            ImageHelpers.EmbedTextBmpLSBFile(path, outPath, text);
            ShowOutput("LSB вграждане успешно. Записан файл: " + outPath);
        }
        catch (Exception ex) { ShowOutput("Грешка: " + ex.Message); }
    }

    private void BtnExtractLSB_Click(object? sender, EventArgs e)
    {
        // Извличане чрез LSB от BMP файл
        var path = txtPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ShowOutput("Файлът не съществува."); return; }
        try
        {
            var extracted = ImageHelpers.ExtractTextBmpLSBFile(path);
            if (extracted == null) ShowOutput("Не е намерен вграден LSB текст или файлът не е BMP.");
            else ShowOutput("LSB извлечен текст:\r\n" + extracted);
        }
        catch (Exception ex) { ShowOutput("Грешка: " + ex.Message); }
    }

    private void BtnEmbedInPlace_Click(object? sender, EventArgs e)
    {
        // Обработчик за бутона "Embed (in-place)".
        // Опитва се да вгради текста в съществуващите metadata полета
        // (iTXt за PNG или COM за JPEG) без да променя размера на файла.
        // Ако няма достатъчно място в съществуващия chunk/segment, операцията
        // няма да промени файла и ще върне съобщение.
        var path = txtPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ShowOutput("Файлът не съществува."); return; }
        var text = txtText.Text ?? string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (ImageHelpers.IsPng(bytes))
            {
                if (ImageHelpers.TryEmbedTextPngInPlace(bytes, text, out var outBytes) && ImageHelpers.WriteIfDifferent(path, outBytes)) ShowOutput("Успешно вграден текст (file size preserved).");
                else ShowOutput("Не беше възможно да вградим текста без промяна на размера. Използвайте 'Embed (allow size change)'.");
            }
            else if (ImageHelpers.IsJpeg(bytes))
            {
                if (ImageHelpers.TryEmbedTextJpegInPlace(bytes, text, out var outBytes) && ImageHelpers.WriteIfDifferent(path, outBytes)) ShowOutput("Успешно вграден текст (file size preserved).");
                else ShowOutput("Не беше възможно да вградим текста без промяна на размера. Използвайте 'Embed (allow size change)'.");
            }
            else ShowOutput("Неподдържан формат. Поддържаме само PNG и JPEG.");
        }
        catch (Exception ex) { ShowOutput($"Грешка: {ex.Message}"); }
    }

    private void BtnEmbedResize_Click(object? sender, EventArgs e)
    {
        // Обработчик за бутона "Embed (allow size change)".
        // Винаги вкарва нова metadata част (iTXt chunk за PNG или
        // COM segment за JPEG), което променя размера на файла.
        var path = txtPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ShowOutput("Файлът не съществува."); return; }
        var text = txtText.Text ?? string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (ImageHelpers.IsPng(bytes))
            {
                var newBytes = ImageHelpers.EmbedTextPng(bytes, text);
                File.WriteAllBytes(path, newBytes);
                ShowOutput("Успешно вграден текст (file size changed).");
            }
            else if (ImageHelpers.IsJpeg(bytes))
            {
                var newBytes = ImageHelpers.EmbedTextJpeg(bytes, text);
                File.WriteAllBytes(path, newBytes);
                ShowOutput("Успешно вграден текст (file size changed).");
            }
            else ShowOutput("Неподдържан формат. Поддържаме само PNG и JPEG.");
        }
        catch (Exception ex) { ShowOutput($"Грешка: {ex.Message}"); }
    }

    private void BtnExtract_Click(object? sender, EventArgs e)
    {
        // Обработчик за бутона "Extract". Извлича текст от първия
        // намерен iTXt chunk (PNG) или COM segment (JPEG) и показва
        // резултата в полето txtOutput.
        var path = txtPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { ShowOutput("Файлът не съществува."); return; }
        try
        {
            var bytes = File.ReadAllBytes(path);
            string? extracted = null;
            if (ImageHelpers.IsPng(bytes)) extracted = ImageHelpers.ExtractTextFromPng(bytes);
            else if (ImageHelpers.IsJpeg(bytes)) extracted = ImageHelpers.ExtractTextFromJpeg(bytes);
            else { ShowOutput("Неподдържан формат."); return; }

            if (extracted == null) ShowOutput("Не е намерен вграден текст."); else ShowOutput("Извлечен текст:\r\n" + extracted);
        }
        catch (Exception ex) { ShowOutput($"Грешка: {ex.Message}"); }
    }

    void ShowOutput(string s)
    {
        txtOutput.Text = s;
    }
}

// Helpers moved into a static helper class to avoid mixing top-level statements and type declarations
static class ImageHelpers
{
    // Проверки за типа на файла (PNG / JPEG) чрез magic bytes
    public static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
    public static bool IsJpeg(byte[] b) => b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8;

    // Забележка: PNG е структуриран в chunk-ове: [length(4)][type(4)][data(length)][crc(4)].
    // iTXt chunk е текстов chunk, който може да съдържа UTF-8 текст. Тук опитваме
    // да намерим съществуващ iTXt и да презапишем текстовата част без да разместваме
    // останалата структура. Ако няма достатъчно място — връщаме false.

    // Допълнителни бележки за CRC и ендианност:
    // - Дължините и CRC стойностите в PNG са в big-endian (мрежов) формат.
    // - След като променим data полето на chunk-а, трябва да преизчислим CRC за
    //   (type + data) и да запишем 4-байтовата стойност в big-endian в края на chunk-а.
    // - Crc32() тук е стандартна IEEE CRC-32 (полином 0xEDB88320), съвместима с PNG CRC.

    public static bool TryEmbedTextPngInPlace(byte[] pngBytes, string text, out byte[] outBytes)
    {
        // Важни детайли за парсване:
        // - len: 4-байтов big-endian стойност (дължина на data полето)
        // - type: 4 ASCII символа (напр. "iTXt", "IEND")
        // - data: len байта, следвани от 4-байтов CRC
        // Търсим chunk с тип "iTXt" и се опитваме да намерим началото на
        // text payload-а вътре в data полето (след keyword, нулев байт,
        // флагове и евентуални езикови полета). Ако текстът може да се побере
        // го записваме и преизчисляваме CRC за chunk-а.

        // Ограничения/предпазни мерки за този метод:
        // - Не поддържаме компресирани iTXt (compressionFlag != 0).
        // - Ако PNG има множество iTXt chunk-ове, този метод ще замени първия намерен.
        // - Тъй като вграждането е in-place, не добавяме/премахваме байтове; ако няма
        //   достатъчно място, връщаме false и не модифицираме файла.

        outBytes = (byte[])pngBytes.Clone();
        int pos = 8; // skip PNG signature
        while (pos + 8 <= outBytes.Length)
        {
            uint len = ReadBigEndianUInt32(outBytes, pos);
            string type = Encoding.ASCII.GetString(outBytes, pos + 4, 4);
            int dataStart = pos + 8;
            int dataEnd = dataStart + (int)len;
            if (dataEnd + 4 > outBytes.Length) break;

            if (type == "iTXt")
            {
                int i = dataStart;
                // keyword
                while (i < dataEnd && outBytes[i] != 0) i++;
                i++; // null
                if (i + 2 > dataEnd) return false;
                byte compressionFlag = outBytes[i++];
                byte compressionMethod = outBytes[i++];
                // language tag
                while (i < dataEnd && outBytes[i] != 0) i++; i++;
                // translated keyword
                while (i < dataEnd && outBytes[i] != 0) i++; i++;
                int textStart = i;
                int available = (int)len - (textStart - dataStart);
                var txt = Encoding.UTF8.GetBytes(text);
                if (txt.Length <= available)
                {
                    // clear existing text area then copy
                    Array.Clear(outBytes, textStart, available);
                    Array.Copy(txt, 0, outBytes, textStart, txt.Length);
                    // recompute CRC
                    var typeBytes = Encoding.ASCII.GetBytes("iTXt");
                    var data = new byte[len];
                    Array.Copy(outBytes, dataStart, data, 0, data.Length);
                    var crc = Crc32(typeBytes.Concat(data).ToArray());
                    WriteUInt32ToBuffer(outBytes, dataEnd, crc);
                    return true;
                }
                return false;
            }

            if (type == "IEND") break;
            pos = dataEnd + 4; // skip CRC
        }
        return false;
    }

    public static bool TryEmbedTextJpegInPlace(byte[] jpgBytes, string text, out byte[] outBytes)
    {
        // JPEG е структуриран в сегменти започващи с 0xFF, последвано от маркер.
        // COM сегментът използва маркер 0xFE и има 2-байтова дължина (включително тези два байта).
        // Тук търсим първия COM сегмент и ако текстът може да се побере в наличното място
        // презаписваме payload-а без да променяме общата дължина на сегмента.

        // Ограничения за JPEG in-place:
        // - Ако няма предварително COM сегмент с достатъчен размер, in-place не може да вгради
        //   текста. В такъв случай използвайте EmbedTextJpeg() който вкарва нов COM след SOI.
        // - Този код не премества или компресира сегменти; той само презаписва bytes в рамките
        //   на вече съществуващ сегмент.

        outBytes = (byte[])jpgBytes.Clone();
        int pos = 2; // after SOI
        while (pos + 4 <= outBytes.Length)
        {
            if (outBytes[pos] != 0xFF) { pos++; continue; }
            byte marker = outBytes[pos + 1];
            if (marker == 0xDA || marker == 0xD9) break; // SOS or EOI
            if (marker == 0xFE) // COM
            {
                int len = (outBytes[pos + 2] << 8) | outBytes[pos + 3];
                int dataLen = len - 2;
                int dataPos = pos + 4;
                var txt = Encoding.UTF8.GetBytes(text);
                if (txt.Length <= dataLen)
                {
                    Array.Clear(outBytes, dataPos, dataLen);
                    Array.Copy(txt, 0, outBytes, dataPos, txt.Length);
                    return true;
                }
                return false;
            }
            if (pos + 4 > outBytes.Length) break;
            int segLen = (outBytes[pos + 2] << 8) | outBytes[pos + 3];
            pos += 2 + segLen;
        }
        return false;
    }

    public static bool WriteIfDifferent(string path, byte[] newBytes)
    {
        var original = File.ReadAllBytes(path);
        if (original.Length != newBytes.Length) return false;
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, newBytes);
        File.Replace(tmp, path, null);
        return true;
    }

    public static byte[] EmbedTextPng(byte[] pngBytes, string text)
    {
        // Ако не можем да вградим in-place, създаваме нов PNG като вмъкнем
        // нов iTXt chunk непосредствено преди IEND chunk-а. Този метод строи
        // валиден iTXt chunk (keyword + flags + UTF-8 text) и го поставя преди IEND.

        // Този подход гарантира, че новият PNG ще бъде валиден. Недостатък:
        // размерът на файла се променя и CRC за новия chunk се пресмята при строенето.
        const int sigLen = 8;
        if (pngBytes.Length < sigLen) throw new InvalidOperationException("Invalid PNG");
        int pos = sigLen;
        using var ms = new MemoryStream();
        ms.Write(pngBytes, 0, sigLen);

        while (pos + 8 <= pngBytes.Length)
        {
            uint len = ReadBigEndianUInt32(pngBytes, pos);
            string type = Encoding.ASCII.GetString(pngBytes, pos + 4, 4);
            int chunkStart = pos;
            int dataStart = pos + 8;
            int dataEnd = dataStart + (int)len;
            int crcStart = dataEnd;
            if (dataEnd + 4 > pngBytes.Length) throw new InvalidOperationException("Invalid PNG chunk");

            if (type == "IEND")
            {
                var chunk = BuildPngITXtChunk("stegotext", text);
                ms.Write(chunk, 0, chunk.Length);
                // write IEND and the rest
                ms.Write(pngBytes, chunkStart, 8 + (int)len + 4);
                ms.Write(pngBytes, crcStart + 4, pngBytes.Length - (crcStart + 4));
                return ms.ToArray();
            }

            // copy this chunk
            ms.Write(pngBytes, chunkStart, 8 + (int)len + 4);
            pos = crcStart + 4;
        }

        throw new InvalidOperationException("IEND not found");
    }

    public static byte[] BuildPngITXtChunk(string keyword, string text)
    {
        // keyword\0 compression_flag(0) compression_method(0) language_tag\0 translated_keyword\0 text(UTF-8)
        //
        // Подробно:
        // - keyword: ASCII низ (напр. "stegotext") завършващ с 0x00
        // - compression_flag: 0 означава незаписан (no compression)
        // - compression_method: 0 (по подразбиране)
        // - language_tag и translated_keyword: могат да са празни (само 0x00)
        // - text: UTF-8 байтове след всички предишни полета
        var kw = Encoding.ASCII.GetBytes(keyword);
        var txt = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        ms.Write(kw, 0, kw.Length);
        ms.WriteByte(0);
        ms.WriteByte(0); // compression flag
        ms.WriteByte(0); // compression method
        ms.WriteByte(0); // language tag empty + null
        ms.WriteByte(0); // translated keyword empty + null
        ms.Write(txt, 0, txt.Length);
        var data = ms.ToArray();

        using var outMs = new MemoryStream();
        WriteBigEndianUInt32(outMs, (uint)data.Length);
        var type = Encoding.ASCII.GetBytes("iTXt");
        outMs.Write(type, 0, 4);
        outMs.Write(data, 0, data.Length);
        uint crc = Crc32(type.Concat(data).ToArray());
        WriteBigEndianUInt32(outMs, crc);
        return outMs.ToArray();
    }

    public static byte[] EmbedTextJpeg(byte[] jpgBytes, string text)
    {
        // Ако няма достатъчно място за in-place замяна, тук вкарваме COM сегмент
        // веднага след SOI (Start Of Image) маркера (0xFFD8). Това променя размера
        // на JPEG файла, но е прост начин да запазим текста в метаданни.
        if (!IsJpeg(jpgBytes)) throw new InvalidOperationException("Not a JPEG");
        int pos = 2; // after SOI
        var data = Encoding.UTF8.GetBytes(text);
        if (data.Length > 65533) throw new InvalidOperationException("Text too long for JPEG comment segment.");
        using var ms = new MemoryStream();
        ms.Write(jpgBytes, 0, pos);
        // COM marker
        ms.WriteByte(0xFF);
        ms.WriteByte(0xFE);
        ushort len = (ushort)(data.Length + 2);
        ms.WriteByte((byte)(len >> 8));
        ms.WriteByte((byte)(len & 0xFF));
        ms.Write(data, 0, data.Length);
        ms.Write(jpgBytes, pos, jpgBytes.Length - pos);
        return ms.ToArray();
    }

    public static string? ExtractTextFromPng(byte[] pngBytes)
    {
        int pos = 8;
        while (pos + 8 <= pngBytes.Length)
        {
            uint len = ReadBigEndianUInt32(pngBytes, pos);
            string type = Encoding.ASCII.GetString(pngBytes, pos + 4, 4);
            int dataStart = pos + 8;
            int dataEnd = dataStart + (int)len;
            if (dataEnd + 4 > pngBytes.Length) break;
            if (type == "iTXt")
            {
                int i = dataStart;
                while (i < dataEnd && pngBytes[i] != 0) i++; i++;
                if (i + 2 >= dataEnd) return null;
                i += 2; // skip compression flag + method
                while (i < dataEnd && pngBytes[i] != 0) i++; i++;
                while (i < dataEnd && pngBytes[i] != 0) i++; i++;
                int textLen = dataEnd - i;
                if (textLen <= 0) return string.Empty;
                return Encoding.UTF8.GetString(pngBytes, i, textLen);
            }
            if (type == "IEND") break;
            pos = dataEnd + 4;
        }
        return null;
    }

    public static string? ExtractTextFromJpeg(byte[] jpgBytes)
    {
        if (!IsJpeg(jpgBytes)) return null;
        int pos = 2;
        while (pos + 4 <= jpgBytes.Length)
        {
            if (jpgBytes[pos] != 0xFF) { pos++; continue; }
            byte marker = jpgBytes[pos + 1];
            if (marker == 0xDA || marker == 0xD9) break;
            if (marker == 0xFE)
            {
                int len = (jpgBytes[pos + 2] << 8) | jpgBytes[pos + 3];
                if (len < 2) return null;
                int dataLen = len - 2;
                if (pos + 4 + dataLen > jpgBytes.Length) return null;
                return Encoding.UTF8.GetString(jpgBytes, pos + 4, dataLen);
            }
            if (pos + 4 > jpgBytes.Length) break;
            int segLen = (jpgBytes[pos + 2] << 8) | jpgBytes[pos + 3];
            pos += 2 + segLen;
        }
        return null;
    }

    public static uint ReadBigEndianUInt32(byte[] b, int offset)
    {
        return ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) | ((uint)b[offset + 2] << 8) | b[offset + 3];
    }

    public static void WriteUInt32ToBuffer(byte[] buffer, int offset, uint v)
    {
        buffer[offset] = (byte)(v >> 24);
        buffer[offset + 1] = (byte)(v >> 16);
        buffer[offset + 2] = (byte)(v >> 8);
        buffer[offset + 3] = (byte)(v & 0xFF);
    }

    public static void WriteBigEndianUInt32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v & 0xFF));
    }

    // CRC32 (IEEE 802.3)
    public static uint Crc32(byte[] data)
    {
        // Изчисляваме стандартен CRC-32 (IEEE 802.3) с полином 0xEDB88320.
        // Този алгоритъм създава таблица на летенето; за оптимизация може да
        // се кешира таблицата като статично поле, но за простота тук тя се
        // пресмята всеки път.
        const uint poly = 0xEDB88320u;
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint res = i;
            for (int j = 0; j < 8; j++) res = (res & 1) != 0 ? (poly ^ (res >> 1)) : (res >> 1);
            table[i] = res;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    // --- BMP LSB helpers (ported from provided C example) ---
    // Read 4-byte pixel data offset from BMP header (little-endian at offset 10)
    public static int BmpDataOffset(byte[] bmp)
    {
        if (bmp.Length < 14) throw new InvalidOperationException("Not a BMP file");
        return (int)bmp[10] | ((int)bmp[11] << 8) | ((int)bmp[12] << 16) | ((int)bmp[13] << 24);
    }

    // Embed text into BMP using LSB method. Writes a single length byte at the data offset,
    // then encodes message bits into LSBs of subsequent bytes (one bit per byte).
    public static void EmbedTextBmpLSBFile(string inBmpPath, string outBmpPath, string text)
    {
        var bmp = File.ReadAllBytes(inBmpPath);
        int offset = BmpDataOffset(bmp);
        var msgBytes = Encoding.UTF8.GetBytes(text);
        if (msgBytes.Length > 255) throw new InvalidOperationException("Text too long for simple BMP LSB embed (max 255 bytes)");

        var outBmp = (byte[])bmp.Clone();
        // write message length (1 byte) at the data offset
        if (offset >= outBmp.Length) throw new InvalidOperationException("Invalid BMP data offset");
        outBmp[offset] = (byte)msgBytes.Length;

        int dataPos = offset + 1; // start embedding after length byte
        for (int i = 0; i < msgBytes.Length; i++)
        {
            byte mb = msgBytes[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if (dataPos >= outBmp.Length) throw new InvalidOperationException("BMP too small to embed message");
                int msgBit = (mb >> (7 - bit)) & 1;
                int lsb = outBmp[dataPos] & 1;
                if (lsb != msgBit)
                {
                    if (msgBit == 1)
                        outBmp[dataPos] = (byte)(outBmp[dataPos] | 1);
                    else
                        outBmp[dataPos] = (byte)(outBmp[dataPos] & 0xFE);
                }
                dataPos++;
            }
        }

        File.WriteAllBytes(outBmpPath, outBmp);
    }

    // Extract text embedded by EmbedTextBmpLSBFile. Reads length byte and reconstructs message.
    public static string? ExtractTextBmpLSBFile(string bmpPath)
    {
        var bmp = File.ReadAllBytes(bmpPath);
        int offset = BmpDataOffset(bmp);
        if (offset + 1 >= bmp.Length) return null;
        int msgLen = bmp[offset];
        if (msgLen <= 0) return string.Empty;
        int dataPos = offset + 1;
        var buf = new byte[msgLen];
        for (int i = 0; i < msgLen; i++)
        {
            byte acc = 0;
            for (int b = 0; b < 8; b++)
            {
                if (dataPos >= bmp.Length) throw new InvalidOperationException("BMP truncated while extracting");
                acc <<= 1;
                acc |= (byte)(bmp[dataPos] & 1);
                dataPos++;
            }
            buf[i] = acc;
        }
        return Encoding.UTF8.GetString(buf);
    }
}
