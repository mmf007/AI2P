using System.Net.Http.Headers;

namespace AI2P.Connectors;

/// <summary>
/// Тело запроса загрузки стартового кадра в ComfyUI (<c>POST /upload/image</c>, T-258)
/// — ровно в том виде, в каком его шлёт браузер.
///
/// Собирается ЗДЕСЬ, а не штатным <c>MultipartFormDataContent.Add(content, name, fileName)</c>,
/// из-за двух особенностей .NET (T-277):
/// <list type="number">
/// <item>имя части .NET по умолчанию пишет БЕЗ кавычек (<c>name=image</c>), а разборщик формы
/// на стороне движка вправе этого не принять;</item>
/// <item>вместе с <c>filename</c> .NET добавляет ещё и <c>filename*</c> (RFC 5987) с тем же
/// значением — а aiohttp, на котором написан сервер ComfyUI, при разборе предпочитает именно
/// <c>filename*</c>. Кавычки, поставленные ради пункта 1, попадали туда как %22, ComfyUI получал
/// имя файла вместе с кавычками, на Windows такое имя недопустимо — и загрузка отвечала
/// HTTP 500 (жалоба T-277: «ComfyUI не принял стартовый кадр … HTTP 500»).</item>
/// </list>
/// Поэтому заголовок части собирается руками: кавычки есть, <c>filename*</c> нет.
/// </summary>
public static class ComfyUploadForm
{
    /// <summary>
    /// Форма загрузки картинки: часть с файлом плюс поля <c>overwrite</c> и <c>type</c>,
    /// которые ComfyUI ждёт от браузера.
    /// </summary>
    /// <param name="field">Имя части с файлом (у ComfyUI — image; берётся из профайла модели).</param>
    /// <param name="fileName">Имя файла, под которым кадр ляжет в каталог input движка.</param>
    /// <param name="bytes">Содержимое файла.</param>
    /// <param name="contentType">Тип содержимого по расширению файла.</param>
    public static MultipartFormDataContent Build(string field, string fileName, byte[] bytes,
        string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = Quoted(field),
            FileName = Quoted(fileName),
            // FileNameStar НЕ ставим намеренно: см. пункт 2 в описании класса
        };
        form.Add(file);
        form.Add(Field("overwrite", "true"));
        form.Add(Field("type", "input"));
        return form;
    }

    /// <summary>Обычное поле формы: только имя в кавычках, ничего лишнего.</summary>
    private static HttpContent Field(string name, string value)
    {
        var part = new StringContent(value);
        part.Headers.ContentType = null; // браузер у простого поля тип не шлёт
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = Quoted(name),
        };
        return part;
    }

    /// <summary>
    /// Значение в кавычках. Свои кавычки и управляющие знаки вырезаются: имя поля приходит
    /// из профайла модели, то есть его пишет человек, а кавычка или перевод строки внутри
    /// разрывают заголовок части.
    /// </summary>
    private static string Quoted(string value) =>
        "\"" + new string([.. value.Where(c => c != '"' && !char.IsControl(c))]) + "\"";
}
