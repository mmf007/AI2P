using System.Formats.Tar;
using System.IO.Compression;

namespace AI2P.Core;

/// <summary>
/// РАСПАКОВКА АРХИВА СРЕДСТВАМИ .NET (T-292).
///
/// Установщик пакетов (ComfyUI, llama.cpp, musubi-tuner) до этого звал внешние утилиты почти
/// на всё, кроме zip: не нашлось tar и 7-Zip — человеку предлагалось распаковать архив руками
/// в указанный каталог. Внешний архиватор нужен далеко не всегда: <c>System.IO.Compression</c>
/// (zip, gzip) и <c>System.Formats.Tar</c> (tar) входят в рантайм, и ими закрываются все виды
/// архивов, кроме 7z и «редких» сжатий (xz, bzip2, zstd) — их в .NET нет вовсе.
///
/// Вид архива определяется ПО СОДЕРЖИМОМУ (подпись первых байтов), а не по объявлению в
/// справочнике пакетов и не по расширению: имя ассета релиза меняется от версии к версии,
/// и объявленный «7z» на деле регулярно оказывается zip (и наоборот).
///
/// Чего здесь нет намеренно: своей реализации LZMA/LZMA2 — 7z остаётся за внешними
/// утилитами (в Windows 10/11 системный tar.exe их читает), решение об этом принимает
/// вызывающий по ответу <see cref="Supported"/>.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>Виды архивов, которые .NET распаковывает сам.</summary>
    public static readonly string[] Supported = ["zip", "gzip", "tar"];

    /// <summary>
    /// Вид архива по подписи файла: zip, gzip, tar, 7z, xz, bzip2, zstd; пусто — не архив
    /// (или подпись не распознана).
    /// </summary>
    public static string Detect(string path)
    {
        var head = new byte[512];
        int read;
        try
        {
            using var file = File.OpenRead(path);
            read = Fill(file, head, head.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
        return DetectHead(head, read);
    }

    /// <summary>Вид архива по уже прочитанному началу файла (нужно до 262 байт).</summary>
    public static string DetectHead(byte[] head, int length)
    {
        if (Starts(head, length, 0x50, 0x4B, 0x03, 0x04) ||
            Starts(head, length, 0x50, 0x4B, 0x05, 0x06) ||
            Starts(head, length, 0x50, 0x4B, 0x07, 0x08))
        {
            return "zip";
        }
        if (Starts(head, length, 0x1F, 0x8B))
        {
            return "gzip";
        }
        if (Starts(head, length, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C))
        {
            return "7z";
        }
        if (Starts(head, length, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00))
        {
            return "xz";
        }
        if (Starts(head, length, 0x42, 0x5A, 0x68))
        {
            return "bzip2";
        }
        if (Starts(head, length, 0x28, 0xB5, 0x2F, 0xFD))
        {
            return "zstd";
        }
        // tar подписи в начале не имеет: «ustar» лежит в заголовке первой записи, байт 257
        return IsTar(head, length) ? "tar" : "";
    }

    /// <summary>Этот файл .NET распакует сам.</summary>
    public static bool CanExtract(string path) => Supported.Contains(Detect(path));

    /// <summary>
    /// Распаковать архив в каталог средствами .NET. true — распаковано; false — либо вид
    /// архива в .NET не поддерживается (<paramref name="error"/> пуст), либо распаковка не
    /// удалась (<paramref name="error"/> — причина). В обоих случаях вызывающему остаётся
    /// внешняя утилита.
    /// </summary>
    public static bool TryExtract(string path, string targetDir, out string error)
    {
        error = "";
        var kind = Detect(path);
        if (!Supported.Contains(kind))
        {
            return false;
        }
        try
        {
            Directory.CreateDirectory(targetDir);
            switch (kind)
            {
                case "zip":
                    ZipFile.ExtractToDirectory(path, targetDir, overwriteFiles: true);
                    return true;
                case "tar":
                    using (var tar = File.OpenRead(path))
                    {
                        ExtractTar(tar, targetDir);
                    }
                    return true;
                default:
                    ExtractGzip(path, targetDir);
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException
                                       or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// gzip: внутри либо tar (.tar.gz, .tgz) — тогда раскладываем его содержимое, либо один
    /// файл (model.gguf.gz) — тогда кладём его в каталог без расширения .gz.
    /// </summary>
    private static void ExtractGzip(string path, string targetDir)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var head = new byte[512];
        var read = Fill(gzip, head, head.Length);
        using var stream = new HeadStream(head, read, gzip);
        if (IsTar(head, read))
        {
            ExtractTar(stream, targetDir);
            return;
        }
        var name = Path.GetFileName(path);
        name = name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? name[..^3]
            : name + ".out";
        using var target = File.Create(Path.Combine(targetDir, name));
        stream.CopyTo(target);
    }

    /// <summary>
    /// РАСПАКОВКА TAR ПО ЗАПИСЯМ, а не штатным <c>TarFile.ExtractToDirectory</c> (T-4-S0).
    ///
    /// Причина одна, и найдена она на настоящем архиве Python (cpython-*-install_only.tar.gz):
    /// в 100-байтовом поле имени лежит имя, ноль-терминатор И МУСОР ПОСЛЕ НЕГО — остаток
    /// предыдущей записи, который писавший архив tar не затёр. По формату всё, что после
    /// нуля, значения не имеет (так и читает python-овый tarfile), а <c>TarReader</c> .NET
    /// отдаёт имя ЦЕЛИКОМ, вместе с нулём и хвостом. На диске из этого получается
    /// «python.exe<c>\0</c>hon.exe» — то есть Python, которого потом не найти, и установка
    /// пакета честно кончается «в каталоге нет файла python.exe».
    ///
    /// Поэтому имя обрезается по первому нулю, и заодно проверяется выход за каталог
    /// назначения (штатный метод это делает сам, а мы теперь раскладываем записи руками).
    /// </summary>
    private static void ExtractTar(Stream stream, string targetDir)
    {
        var root = Path.GetFullPath(targetDir);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            var name = CleanName(entry.Name);
            if (name.Length == 0)
            {
                continue;
            }
            var full = Path.GetFullPath(Path.Combine(root,
                name.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException(name);   // запись метит за каталог назначения
            }
            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(full);
                continue;
            }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                or TarEntryType.ContiguousFile))
            {
                continue;   // ссылки, устройства и служебные записи нам не нужны
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    /// <summary>Имя записи tar: всё, что после нуля-терминатора, — мусор писавшего архив.</summary>
    private static string CleanName(string name)
    {
        var at = name.IndexOf('\0');
        var value = (at >= 0 ? name[..at] : name).Replace('\\', '/').Trim();
        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];      // «./python/…» — то же, что «python/…»
        }
        return value.Trim('/');      // выход за каталог ловит проверка пути, а не эта строка
    }

    /// <summary>Заголовок первой записи tar: подпись «ustar» с 257-го байта.</summary>
    private static bool IsTar(byte[] head, int length) =>
        length >= 262 && head[257] == 'u' && head[258] == 's' && head[259] == 't'
        && head[260] == 'a' && head[261] == 'r';

    private static bool Starts(byte[] head, int length, params byte[] signature)
    {
        if (length < signature.Length)
        {
            return false;
        }
        for (var i = 0; i < signature.Length; i++)
        {
            if (head[i] != signature[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Прочитать ровно count байт (или сколько есть до конца потока).</summary>
    private static int Fill(Stream source, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var step = source.Read(buffer, read, count - read);
            if (step <= 0)
            {
                break;
            }
            read += step;
        }
        return read;
    }

    /// <summary>
    /// Поток «прочитанное начало + остаток»: распознать tar внутри gzip можно только
    /// заглянув в первые байты, а перемотать назад распаковщик не даёт.
    /// </summary>
    private sealed class HeadStream : Stream
    {
        private readonly byte[] _head;
        private readonly int _length;
        private readonly Stream _rest;
        private int _at;

        public HeadStream(byte[] head, int length, Stream rest)
        {
            _head = head;
            _length = length;
            _rest = rest;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_at < _length)
            {
                var step = Math.Min(count, _length - _at);
                Array.Copy(_head, _at, buffer, offset, step);
                _at += step;
                return step;
            }
            return _rest.Read(buffer, offset, count);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
