using System.Text;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-277: запуск видеомодели с референс-картинкой. Первый живой запуск падал на загрузке
/// стартового кадра — «ComfyUI не принял стартовый кадр objects/CatySark_c.png: HTTP 500
/// 500 Internal Server Error», причём от смены формата (.png → .jpg) ничего не менялось.
///
/// Причина не в картинке: штатный <c>MultipartFormDataContent.Add(content, name, fileName)</c>
/// пишет рядом с <c>filename</c> ещё и <c>filename*</c> (RFC 5987) с тем же значением, а сервер
/// ComfyUI (aiohttp) при разборе предпочитает <c>filename*</c>. Кавычки, которые в T-258
/// проставлялись руками ради имени части, попадали туда как %22 — ComfyUI получал имя файла
/// вместе с кавычками, а такое имя на Windows недопустимо, и обработчик отвечал HTTP 500.
/// Проверено настоящим aiohttp: test/t277/repro.py (old → 500, new → 200).
/// </summary>
public sealed class T277Tests
{
    private const string Name = "ai2p_J-1.png";

    private static string Body(string field = "image", string fileName = Name)
    {
        using var form = ComfyUploadForm.Build(field, fileName, Encoding.ASCII.GetBytes("PNG-BYTES"),
            "image/png");
        return form.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void The_Upload_Body_Names_The_File_The_Way_A_Browser_Does()
    {
        var body = Body();
        Assert.Contains("name=\"image\"", body);
        Assert.Contains($"filename=\"{Name}\"", body);
        // главное: второго имени файла в заголовке нет вовсе — именно его читал ComfyUI
        Assert.DoesNotContain("filename*", body);
        Assert.DoesNotContain("%22", body);
    }

    [Fact]
    public void The_Engine_Sees_The_Plain_File_Name_Without_Quotes()
    {
        // то, что достанется разборщику формы: между filename=" и следующей кавычкой
        var body = Body();
        var at = body.IndexOf("filename=\"", StringComparison.Ordinal) + "filename=\"".Length;
        var parsed = body[at..body.IndexOf('"', at)];
        Assert.Equal(Name, parsed);
    }

    [Fact]
    public void The_Other_Fields_Are_The_Ones_ComfyUI_Waits_For()
    {
        var body = Body();
        Assert.Contains("name=\"overwrite\"", body);
        Assert.Contains("true", body);
        Assert.Contains("name=\"type\"", body);
        Assert.Contains("input", body);
        Assert.Contains("Content-Type: image/png", body);
    }

    [Fact]
    public void Own_Quotes_In_The_Name_Do_Not_Break_The_Header()
    {
        // имя поля берётся из профайла модели, то есть его пишет человек
        var body = Body(field: "im\"age", fileName: "he\"ro.png");
        Assert.Contains("name=\"image\"", body);
        Assert.Contains("filename=\"hero.png\"", body);
        // ни одной лишней кавычки: части остаются разбираемыми
        Assert.Equal(8, body.Split('"').Length - 1); // image, hero.png, overwrite, type — по паре
    }

    [Fact]
    public void A_Line_Break_In_The_Name_Does_Not_Tear_The_Header()
    {
        var body = Body(field: "im\r\nage", fileName: "he\nro.png");
        Assert.Contains("name=\"image\"", body);
        Assert.Contains("filename=\"hero.png\"", body);
        // заголовок части остался одной строкой: разделов ровно столько, сколько полей
        Assert.Equal(3, body.Split("Content-Disposition").Length - 1);
    }
}
