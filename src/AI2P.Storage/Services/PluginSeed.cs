using System.Text.Json;
using AI2P.Core;

namespace AI2P.Storage.Services;

/// <summary>
/// МАНИФЕСТЫ ПЛАГИНОВ ДИСТРИБУТИВА (T-115-S0) — файлы <c>plugins/&lt;код&gt;/plugin.json</c>
/// каталога данных, по образцу справочника пакетов <c>models/packages.json</c> (он ложится
/// точно так же, <c>AiModelService.WriteSeedFile</c>).
///
/// <para>ПОЧЕМУ ФАЙЛОМ, А НЕ ЗАПИСЬЮ В БАЗЕ. Манифест — это ОПИСАНИЕ ВОЗМОЖНОСТЕЙ: оно
/// приходит с программой и меняется вместе с ней, а в базе организации лежит только решение
/// человека (завёл, настроил, выключил). Записи плагин НЕ заводит: список показывает манифест
/// как состояние «объявлен», а запись появляется, когда человек нажал «Инициализировать»
/// (T-114-S0). Иначе первый же открывший вкладку завёл бы записи на весь кластер.</para>
///
/// <para>ПЕРЕЗАПИСЫВАЕТСЯ ПО ВЕРСИИ МАНИФЕСТА: файл на диске старее встроенного —
/// кладём новый. Правка руками при этом переживает обновление ровно до следующего подъёма
/// версии, как у справочника пакетов; всё, что человек настраивает, живёт НЕ здесь, а в
/// записи (<c>PluginRecord.SettingsJson</c>) и в config.json (путь к программе).</para>
/// </summary>
public static class PluginSeed
{
    /// <summary>Разложить манифесты дистрибутива по каталогу данных организации.</summary>
    public static void Write(FileStore files)
    {
        foreach (var json in All)
        {
            var manifest = PluginManifest.Parse(json);
            if (manifest is null)
            {
                continue; // встроенный манифест негоден — это дефект сборки, а не данных
            }
            var rel = PluginManifest.PathOf(manifest.Code);
            var abs = files.Abs(rel);
            if (File.Exists(abs) && VersionOf(abs) >= manifest.Version)
            {
                continue;
            }
            files.WriteText(rel, json);
        }
    }

    /// <summary>Версия манифеста, лежащего на диске; 0 — файл нечитаем или это не манифест.</summary>
    private static int VersionOf(string abs)
    {
        try
        {
            return PluginManifest.Parse(File.ReadAllText(abs))?.Version ?? 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Встроенные манифесты. Порядок безразличен — файлы независимы.</summary>
    public static IReadOnlyList<string> All =>
        [ShotcutJson, FfmpegJson, ResolveJson, BlenderJson, OpenShotJson, TrainerMusubiJson];

    /// <summary>Код плагина-шлюза в Shotcut / Kdenlive.</summary>
    public const string ShotcutCode = "editor.shotcut";

    /// <summary>Код плагина-конвертора видео (ffmpeg).</summary>
    public const string FfmpegCode = "tool.ffmpeg";

    /// <summary>Код плагина-шлюза в DaVinci Resolve (экспорт OTIO).</summary>
    public const string ResolveCode = "editor.resolve";

    /// <summary>Код плагина-шлюза в Blender VSE (параметризованный скрипт Python).</summary>
    public const string BlenderCode = "editor.blender";

    /// <summary>Код плагина-шлюза в OpenShot (проект <c>.osp</c> — чистый JSON).</summary>
    public const string OpenShotCode = "editor.openshot";

    /// <summary>Код плагина-ТРЕНЕРА адаптеров LoRA (musubi-tuner, T-156-S0).</summary>
    public const string TrainerMusubiCode = "trainer.musubi";

    /// <summary>
    /// ШЛЮЗ В SHOTCUT / KDENLIVE (T-115-S0) — эталонный шлюз ветки.
    ///
    /// <para>ПОЧЕМУ ОН ПЕРВЫЙ: <c>.mlt</c> (Shotcut) и <c>.kdenlive</c> (Kdenlive) — ОДИН
    /// формат MLT XML, то есть один манифест закрывает сразу два редактора; формат текстовый
    /// и пишется напрямую; и он ЕДИНСТВЕННЫЙ из четырёх, у кого есть рендер без человека и
    /// без окна — <c>melt</c> (в поставке Shotcut для Windows это <c>melt.exe</c>, на других
    /// сборках встречается <c>qmelt</c>).</para>
    ///
    /// <para>ШАБЛОНЫ ВЫВЕРЕНЫ ЖИВЫМ MELT (портативный Shotcut 26.8.1, melt 7.41.0): файл
    /// такого вида отрендерен без окна, код возврата 0. Строение — как у файла самого
    /// Shotcut: папка проекта <c>main_bin</c>, нижняя дорожка <c>background</c> с чёрным
    /// producer, дорожки <c>playlist0</c> (видео) и <c>playlist1</c> (звук), сборка
    /// <c>tractor0</c>. Атрибуты XML в кавычках-апострофах — так шаблон читается в JSON без
    /// частокола обратных косых; для XML это равноправная запись.</para>
    ///
    /// <para>ЧТО МЕНЯТЬ АВТОРУ СЛЕДУЮЩЕГО ШЛЮЗА (T-117-S0 OTIO, T-118-S0 Blender, T-120-S0
    /// OpenShot): блок <c>export</c> целиком и блок <c>render</c>. Кода не нужно — движок
    /// (<see cref="TimelineExport"/>) и набор инструментов (<c>GatewayToolset</c>) общие.</para>
    /// </summary>
    public const string ShotcutJson = """
        {
          "code": "editor.shotcut",
          "kind": "gateway",
          "version": 1,
          "name": "Shotcut / Kdenlive (MLT XML)",
          "description": {
            "ru": "Сборка монтажного листа MLT из медиатеки проекта и рендер через melt без окна. Один файл открывается и в Shotcut, и в Kdenlive.",
            "en": "Builds an MLT timeline from the project media library and renders it headlessly with melt. One file opens both in Shotcut and in Kdenlive.",
            "es": "Crea una linea de tiempo MLT a partir de la biblioteca multimedia del proyecto y la renderiza sin ventana con melt. El mismo archivo se abre en Shotcut y en Kdenlive.",
            "pt": "Monta uma linha do tempo MLT a partir da biblioteca de midia do projeto e renderiza sem janela com o melt. O mesmo arquivo abre no Shotcut e no Kdenlive.",
            "zh-cn": "根据项目媒体库生成 MLT 时间线，并用 melt 无窗口渲染。同一个文件既能在 Shotcut 打开，也能在 Kdenlive 打开。"
          },

          "software": {
            "package": "shotcut",
            "required": false,
            "pathKey": "meltPath",
            "system": {
              "commands": ["melt", "qmelt", "melt.exe", "qmelt.exe"],
              "versionArgs": "--version",
              "minVersion": "7.0",
              "maxVersion": ""
            },
            "hint": {
              "ru": "Нажмите «Установить» — портативный Shotcut скачается и распакуется сам; либо укажите путь к уже установленному melt (в поставке Shotcut для Windows это melt.exe рядом с shotcut.exe).",
              "en": "Press Install to download and unpack the portable Shotcut, or point to an already installed melt (in the Windows Shotcut bundle it is melt.exe next to shotcut.exe).",
              "es": "Pulse Instalar para descargar Shotcut portable, o indique la ruta de un melt ya instalado (en Shotcut para Windows es melt.exe junto a shotcut.exe).",
              "pt": "Clique em Instalar para baixar o Shotcut portatil, ou informe o caminho de um melt ja instalado (no Shotcut para Windows e melt.exe ao lado de shotcut.exe).",
              "zh-cn": "点击「安装」自动下载并解压便携版 Shotcut；也可以直接指定已安装的 melt（Windows 版 Shotcut 中是与 shotcut.exe 同目录的 melt.exe）。"
            }
          },

          "actions": [
            {
              "code": "AI2P.Plugins.Shotcut.TimelineWrite",
              "tool": "shotcut_timeline_write",
              "role": "export",
              "title": {
                "ru": "Собрать монтажный лист MLT",
                "en": "Build the MLT timeline",
                "es": "Crear la linea de tiempo MLT",
                "pt": "Montar a linha do tempo MLT",
                "zh-cn": "生成 MLT 时间线"
              },
              "description": {
                "ru": "Собрать файл ai2p_library.mlt из медиатеки проекта: дорожка видео и дорожка звука, порядок — как в media_list. Отбор: scene (сцена), kind (вид медиа), tag (тэг). Файл человека не трогается: пишется только свой. Открывается и в Shotcut, и в Kdenlive.",
                "en": "Build ai2p_library.mlt from the project media library: one video track and one audio track, ordered as in media_list. Filters: scene, kind, tag. The human's own project file is never touched. Opens both in Shotcut and Kdenlive.",
                "es": "Crea ai2p_library.mlt desde la biblioteca multimedia del proyecto: una pista de video y una de audio, en el orden de media_list. Filtros: scene, kind, tag. El archivo de la persona nunca se toca.",
                "pt": "Cria ai2p_library.mlt a partir da biblioteca de midia do projeto: uma trilha de video e uma de audio, na ordem de media_list. Filtros: scene, kind, tag. O arquivo da pessoa nunca e alterado.",
                "zh-cn": "根据项目媒体库生成 ai2p_library.mlt：一条视频轨和一条音频轨，顺序与 media_list 相同。可按 scene、kind、tag 筛选。绝不改动人工创建的工程文件。"
              }
            },
            {
              "code": "AI2P.Plugins.Shotcut.Render",
              "tool": "shotcut_render",
              "role": "render",
              "needsSoftware": true,
              "title": {
                "ru": "Отрендерить монтажный лист (melt)",
                "en": "Render the timeline (melt)",
                "es": "Renderizar la linea de tiempo (melt)",
                "pt": "Renderizar a linha do tempo (melt)",
                "zh-cn": "渲染时间线（melt）"
              },
              "description": {
                "ru": "Посчитать ролик по собранному монтажному листу программой melt — без окна и без человека. file — что рендерить (по умолчанию ai2p_library.mlt), out — куда положить результат (mp4, H.264 + AAC). Своей командной строки у действия нет: аргументы заданы плагином.",
                "en": "Render the built timeline with melt, headlessly and without a human. file is what to render (ai2p_library.mlt by default), out is where the result goes (mp4, H.264 + AAC). No arbitrary command line: the arguments come from the plugin.",
                "es": "Renderiza la linea de tiempo con melt, sin ventana y sin persona. file es que renderizar (por defecto ai2p_library.mlt), out es donde va el resultado (mp4, H.264 + AAC). No hay linea de comandos libre.",
                "pt": "Renderiza a linha do tempo com o melt, sem janela e sem pessoa. file e o que renderizar (por padrao ai2p_library.mlt), out e onde fica o resultado (mp4, H.264 + AAC). Nao ha linha de comando livre.",
                "zh-cn": "用 melt 无窗口、无人工地渲染已生成的时间线。file 指定渲染对象（默认 ai2p_library.mlt），out 指定输出位置（mp4，H.264 + AAC）。不提供任意命令行。"
              }
            }
          ],

          "experience": [
            {
              "skill": "video-edit",
              "text": {
                "ru": "MLT XML (.mlt Shotcut и .kdenlive Kdenlive): пути в resource — ТОЛЬКО относительно папки, где лежит сам файл проекта. Абсолютный путь убивает переносимость по кластеру: у соседа тот же ролик лежит по другому пути, и монтажный лист откроется пустым.",
                "en": "MLT XML (.mlt for Shotcut, .kdenlive for Kdenlive): resource paths are ONLY relative to the folder holding the project file. An absolute path kills portability across the cluster: on another machine the same clip sits elsewhere and the timeline opens empty.",
                "es": "MLT XML: las rutas de resource son SOLO relativas a la carpeta del archivo de proyecto. Una ruta absoluta rompe la portabilidad en el cluster.",
                "pt": "MLT XML: os caminhos em resource sao SOMENTE relativos a pasta do arquivo de projeto. Caminho absoluto quebra a portabilidade no cluster.",
                "zh-cn": "MLT XML（Shotcut 的 .mlt 与 Kdenlive 的 .kdenlive）：resource 中的路径只能相对于工程文件所在目录。绝对路径会破坏集群内的可移植性。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Агент пишет ТОЛЬКО свой файл ai2p_library.mlt, а файл проекта человека не трогает никогда: человек может держать его открытым в редакторе, и сохранение из редактора затрёт нашу запись (или наоборот). Готовый монтажный лист человек импортирует к себе сам.",
                "en": "The agent writes ONLY its own ai2p_library.mlt and never touches the human's project file: the human may have it open in the editor, and saving from the editor would overwrite our write (or the other way round). The human imports our timeline manually.",
                "es": "El agente escribe SOLO su propio ai2p_library.mlt y nunca toca el archivo de proyecto de la persona: ella puede tenerlo abierto en el editor.",
                "pt": "O agente escreve APENAS o seu ai2p_library.mlt e nunca toca no arquivo de projeto da pessoa: ela pode te-lo aberto no editor.",
                "zh-cn": "智能体只写自己的 ai2p_library.mlt，绝不改动人工工程文件：对方可能正在编辑器里打开它，双方互相覆盖会丢失工作。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Рендер без окна у MLT есть и работает: melt <файл.mlt> -consumer avformat:<файл.mp4> vcodec=libx264 acodec=aac. На headless Linux melt обязан запускаться с QT_QPA_PLATFORM=offscreen, иначе он не стартует вовсе («could not connect to display»); на Windows эта переменная не нужна. Из четырёх редакторов ветки такой рендер есть только у Shotcut/Kdenlive.",
                "en": "MLT does have a headless render and it works: melt <file.mlt> -consumer avformat:<file.mp4> vcodec=libx264 acodec=aac. On headless Linux melt must run with QT_QPA_PLATFORM=offscreen or it will not start at all; on Windows that variable is not needed. Of the four editors only Shotcut/Kdenlive can render this way.",
                "es": "MLT si tiene render sin ventana: melt <archivo.mlt> -consumer avformat:<archivo.mp4>. En Linux sin pantalla hace falta QT_QPA_PLATFORM=offscreen.",
                "pt": "O MLT tem render sem janela: melt <arquivo.mlt> -consumer avformat:<arquivo.mp4>. No Linux sem tela e preciso QT_QPA_PLATFORM=offscreen.",
                "zh-cn": "MLT 支持无窗口渲染：melt <文件.mlt> -consumer avformat:<文件.mp4>。无显示的 Linux 上必须设置 QT_QPA_PLATFORM=offscreen，否则根本无法启动；Windows 不需要。"
              }
            }
          ],

          "settings": [
            { "key": "fps", "type": "number", "default": 25,
              "title": { "ru": "Кадров в секунду", "en": "Frames per second", "es": "Cuadros por segundo", "pt": "Quadros por segundo", "zh-cn": "每秒帧数" } },
            { "key": "width", "type": "number", "default": 1920,
              "title": { "ru": "Ширина кадра", "en": "Frame width", "es": "Ancho del cuadro", "pt": "Largura do quadro", "zh-cn": "画面宽度" } },
            { "key": "height", "type": "number", "default": 1080,
              "title": { "ru": "Высота кадра", "en": "Frame height", "es": "Alto del cuadro", "pt": "Altura do quadro", "zh-cn": "画面高度" } }
          ],

          "export": {
            "file": "ai2p_library.mlt",
            "escape": "xml",
            "fps": 25,
            "width": 1920,
            "height": 1080,
            "clipIdPrefix": "producer",
            "copyStore": true,
            "storeDir": "ai2p_media",
            "services": {
              "video": "avformat",
              "audio": "avformat",
              "image": "qimage",
              "subtitle": "avformat",
              "project": "xml"
            },
            "tracks": [
              { "id": "V1", "kinds": ["video", "image", "project"], "hide": "" },
              { "id": "A1", "kinds": ["audio"], "hide": "video" }
            ],
            "clip": [
              "  <producer id='{clip.id}' in='{clip.in}' out='{clip.out}'>",
              "    <property name='length'>{clip.length}</property>",
              "    <property name='mlt_service'>{clip.service}</property>",
              "    <property name='resource'>{clip.path}</property>",
              "    <property name='shotcut:caption'>{clip.caption}</property>",
              "    <property name='ai2p:scene'>{clip.scene}</property>",
              "    <property name='ai2p:take'>{clip.take}</property>",
              "  </producer>",
              ""
            ],
            "entry": [
              "    <entry producer='{clip.id}' in='{clip.in}' out='{clip.out}'/>",
              ""
            ],
            "track": [
              "  <playlist id='playlist{track.index}'>",
              "    <property name='shotcut:name'>{track.id}</property>",
              "{track.entries}  </playlist>",
              ""
            ],
            "trackRef": [
              "    <track producer='playlist{track.index}' hide='{track.hide}'/>",
              ""
            ],
            "document": [
              "<?xml version='1.0' encoding='utf-8'?>",
              "<!-- Собрано AI2P из медиатеки проекта. Это НАШ файл: правьте свой, а этот перезаписывается. -->",
              "<mlt LC_NUMERIC='C' version='7.0.0' title='{doc.title}' producer='main_bin'>",
              "  <profile description='ai2p' width='{doc.width}' height='{doc.height}' progressive='1' sample_aspect_num='1' sample_aspect_den='1' display_aspect_num='16' display_aspect_den='9' frame_rate_num='{doc.fps}' frame_rate_den='1' colorspace='709'/>",
              "{clips}  <playlist id='main_bin'>",
              "    <property name='xml_retain'>1</property>",
              "{entries}  </playlist>",
              "  <producer id='black' in='00:00:00.000' out='{doc.out}'>",
              "    <property name='length'>{doc.length}</property>",
              "    <property name='mlt_service'>color</property>",
              "    <property name='resource'>black</property>",
              "    <property name='aspect_ratio'>1</property>",
              "  </producer>",
              "  <playlist id='background'>",
              "    <entry producer='black' in='00:00:00.000' out='{doc.out}'/>",
              "  </playlist>",
              "{tracks}  <tractor id='tractor0' title='{doc.title}' in='00:00:00.000' out='{doc.out}'>",
              "    <property name='shotcut'>1</property>",
              "    <property name='ai2p'>1</property>",
              "    <track producer='background'/>",
              "{trackRefs}  </tractor>",
              "</mlt>",
              ""
            ]
          },

          "render": {
            "args": ["{file}", "-consumer", "avformat:{out}", "vcodec=libx264", "acodec=aac", "progress=0"],
            "outExt": ".mp4",
            "env": { "QT_QPA_PLATFORM": "offscreen" },
            "envOs": "linux",
            "timeoutSec": 3600
          },

          "doc": {
            "ru": "plugins/editor.shotcut.md",
            "en": "plugins/editor.shotcut.md",
            "es": "plugins/editor.shotcut.md",
            "pt": "plugins/editor.shotcut.md",
            "zh-cn": "plugins/editor.shotcut.md"
          }
        }
        """;

    /// <summary>
    /// ВИДЕО КОНВЕРТОР (ffmpeg) — T-116-S0, ответ ветки на КОДЕКИ.
    ///
    /// <para>ЗАЧЕМ ОН НУЖЕН: наши генерации это mp4/H.264 и aac/mp3 (fal.ai, ComfyUI,
    /// <c>SaveAudioMP3</c>), а бесплатный DaVinci Resolve под Linux H.264/H.265 не декодирует
    /// вовсе, AAC не поддержан даже в Studio, и наборы кодировщиков у сборок Blender разные.
    /// Конвертор делает связку «генерация → монтаж» независимой от того, что умеет конкретная
    /// сборка редактора на конкретной ОС: перед монтажом материал прогоняется через ffmpeg в
    /// промежуточный формат (DNxHR или ProRes + несжатый звук), который читают все.</para>
    ///
    /// <para>ДЕЙСТВИЯ УЗКИЕ И ИМЕНОВАННЫЕ, а действия «выполнить командную строку ffmpeg»
    /// НЕТ И НЕ БУДЕТ: это исполнение произвольного кода с правом писать файлы, и никакое
    /// правило безопасности его не сузит (у ffmpeg есть <c>-f lavfi</c>, протоколы
    /// <c>file:</c>/<c>concat:</c>/<c>tcp:</c> и <c>-y</c> поверх любого файла). От агента
    /// приходят ТОЛЬКО пути, а параметры работы — из настроек записи плагина.</para>
    ///
    /// <para>ПОЧЕМУ ТУТ НЕТ БЛОКА <c>export</c>: конвертор не собирает монтажный лист, он
    /// готовит для него материал. И наоборот, у четырёх шлюзов в редакторы нет блока
    /// <c>convert</c> — это разные работы, а не разные редакторы.</para>
    ///
    /// <para>УМОЛЧАНИЯ ПРОМЕЖУТОЧНОГО ФОРМАТА: DNxHR HQ (<c>dnxhd</c> + <c>dnxhr_hq</c> +
    /// <c>yuv422p</c>) в контейнере MOV и звук <c>pcm_s16le</c> 48 кГц. DNxHR выбран
    /// умолчанием, а не ProRes, потому что кодировщик ProRes у ffmpeg (<c>prores_ks</c>) на
    /// Windows и Linux даёт файлы, которые Resolve читает не всегда, а DNxHR — родной формат
    /// Avid, и его берут все три редактора ветки. ПАРА «профиль ↔ формат пикселей» связана:
    /// <c>dnxhr_hq</c> требует <c>yuv422p</c>, а <c>prores_ks</c> профиля 3 —
    /// <c>yuv422p10le</c>; несовпадение ffmpeg отвергает сам, и отказ виден агенту.</para>
    /// </summary>
    public const string FfmpegJson = """
        {
          "code": "tool.ffmpeg",
          "kind": "gateway",
          "version": 1,
          "name": "ffmpeg",
          "description": {
            "ru": "Видео конвертор: перекодирование материала в промежуточный формат монтажа, извлечение звука, склейка по списку и приведение к общему кадру. Узкие именованные действия, командной строки у агента нет.",
            "en": "Video converter: transcoding footage to an editing intermediate, extracting audio, concatenating by list and normalising to a common frame. Narrow named actions, no command line for the agent.",
            "es": "Conversor de video: transcodificacion a un formato intermedio de montaje, extraccion de audio, union por lista y normalizacion de cuadro. Acciones limitadas y con nombre, sin linea de comandos para el agente.",
            "pt": "Conversor de video: transcodificacao para um formato intermediario de edicao, extracao de audio, juncao por lista e normalizacao de quadro. Acoes estreitas e nomeadas, sem linha de comando para o agente.",
            "zh-cn": "视频转换器：把素材转成剪辑用中间格式、提取音轨、按列表拼接、统一画幅与帧率。只有窄而具名的动作，智能体没有命令行。"
          },

          "software": {
            "package": "ffmpeg",
            "required": true,
            "pathKey": "ffmpegPath",
            "system": {
              "commands": ["ffmpeg", "ffmpeg.exe"],
              "versionArgs": "-version",
              "minVersion": "6.0",
              "maxVersion": ""
            },
            "hint": {
              "ru": "Нажмите «Установить» — сборка ffmpeg скачается и распакуется сама; либо укажите путь к уже установленному ffmpeg (Linux: apt install ffmpeg, macOS: brew install ffmpeg). Версия должна быть не ниже 6.0: набор ключей заметно менялся.",
              "en": "Press Install to download and unpack an ffmpeg build, or point to an already installed ffmpeg (Linux: apt install ffmpeg, macOS: brew install ffmpeg). Version 6.0 or newer is required: the option set changed noticeably.",
              "es": "Pulse Instalar para descargar ffmpeg, o indique la ruta de un ffmpeg ya instalado (Linux: apt install ffmpeg, macOS: brew install ffmpeg). Se requiere la version 6.0 o superior.",
              "pt": "Clique em Instalar para baixar o ffmpeg, ou informe o caminho de um ffmpeg ja instalado (Linux: apt install ffmpeg, macOS: brew install ffmpeg). E necessaria a versao 6.0 ou superior.",
              "zh-cn": "点击「安装」自动下载并解压 ffmpeg；也可以指定已安装的 ffmpeg（Linux：apt install ffmpeg，macOS：brew install ffmpeg）。版本不得低于 6.0。"
            }
          },

          "actions": [
            {
              "code": "AI2P.Plugins.Ffmpeg.ToEdit",
              "tool": "ffmpeg_to_edit",
              "role": "convert",
              "op": "intermediate",
              "needsSoftware": true,
              "title": {
                "ru": "Перекодировать в промежуточный формат монтажа",
                "en": "Transcode to the editing intermediate",
                "es": "Transcodificar al formato intermedio de montaje",
                "pt": "Transcodificar para o formato intermediario de edicao",
                "zh-cn": "转成剪辑用中间格式"
              },
              "description": {
                "ru": "Перекодировать файл (in) в промежуточный формат монтажа: DNxHR или ProRes плюс несжатый звук. Кодек, профиль и формат пикселей берутся из настроек плагина, а не из строки задания. Результат ложится рядом с исходником с хвостом _edit, либо туда, куда сказано в out.",
                "en": "Transcode the file (in) into an editing intermediate: DNxHR or ProRes plus uncompressed audio. Codec, profile and pixel format come from the plugin settings, not from the request. The result goes next to the source with the _edit suffix, or where out says.",
                "es": "Transcodifica el archivo (in) al formato intermedio de montaje: DNxHR o ProRes con audio sin comprimir. Codec, perfil y formato de pixel vienen de los ajustes del plugin.",
                "pt": "Transcodifica o arquivo (in) para o formato intermediario de edicao: DNxHR ou ProRes com audio sem compressao. Codec, perfil e formato de pixel vem dos ajustes do plugin.",
                "zh-cn": "把文件（in）转成剪辑用中间格式：DNxHR 或 ProRes，配无压缩音频。编码器、profile 与像素格式来自插件设置，而不是任务文本。"
              }
            },
            {
              "code": "AI2P.Plugins.Ffmpeg.ExtractAudio",
              "tool": "ffmpeg_extract_audio",
              "role": "convert",
              "op": "audio",
              "needsSoftware": true,
              "title": {
                "ru": "Извлечь звук",
                "en": "Extract the audio",
                "es": "Extraer el audio",
                "pt": "Extrair o audio",
                "zh-cn": "提取音轨"
              },
              "description": {
                "ru": "Вынуть звуковую дорожку файла (in) в отдельный файл WAV без сжатия: AAC не поддержан даже в платной версии DaVinci Resolve, а WAV читают все редакторы. Частота и разрядность — из настроек плагина.",
                "en": "Pull the audio track of the file (in) into a separate uncompressed WAV: AAC is not supported even in paid DaVinci Resolve, while WAV is read by every editor. Rate and depth come from the plugin settings.",
                "es": "Extrae la pista de audio del archivo (in) a un WAV sin comprimir: AAC no esta soportado ni en DaVinci Resolve de pago, y WAV lo lee cualquier editor.",
                "pt": "Extrai a trilha de audio do arquivo (in) para um WAV sem compressao: AAC nao e suportado nem no DaVinci Resolve pago, e WAV e lido por qualquer editor.",
                "zh-cn": "把文件（in）的音轨提取成单独的无压缩 WAV：AAC 连付费版 DaVinci Resolve 都不支持，而 WAV 所有剪辑软件都能读。"
              }
            },
            {
              "code": "AI2P.Plugins.Ffmpeg.Concat",
              "tool": "ffmpeg_concat",
              "role": "convert",
              "op": "concat",
              "needsSoftware": true,
              "title": {
                "ru": "Склеить по списку",
                "en": "Concatenate by list",
                "es": "Unir por lista",
                "pt": "Juntar pela lista",
                "zh-cn": "按列表拼接"
              },
              "description": {
                "ru": "Склеить файлы подряд в один: список задаётся полем files либо берётся из медиатеки проекта тем же порядком, что печатает media_list (отбор scene, kind, tag). Склейка идёт БЕЗ перекодирования, поэтому куски обязаны быть одного формата — сначала приведите их действием «привести к общему кадру и частоте».",
                "en": "Concatenate files one after another: the list comes from the files field or from the project media library in the same order media_list prints (filters scene, kind, tag). The concatenation is stream copy, so the pieces must share one format — normalise them first.",
                "es": "Une archivos uno tras otro: la lista viene del campo files o de la biblioteca multimedia del proyecto. La union es sin recodificar, asi que las piezas deben compartir formato.",
                "pt": "Junta arquivos em sequencia: a lista vem do campo files ou da biblioteca de midia do projeto. A juncao e por copia, entao as pecas precisam ter o mesmo formato.",
                "zh-cn": "把多个文件首尾拼接：列表来自 files 字段，或按 media_list 的顺序取自项目媒体库（可按 scene、kind、tag 筛选）。拼接不重新编码，因此各段必须格式一致。"
              }
            },
            {
              "code": "AI2P.Plugins.Ffmpeg.Uniform",
              "tool": "ffmpeg_uniform",
              "role": "convert",
              "op": "uniform",
              "needsSoftware": true,
              "title": {
                "ru": "Привести к общему кадру и частоте",
                "en": "Normalise to a common frame and rate",
                "es": "Normalizar cuadro y frecuencia",
                "pt": "Normalizar quadro e taxa",
                "zh-cn": "统一画幅与帧率"
              },
              "description": {
                "ru": "Привести файл (in) к общему размеру кадра и частоте кадров проекта: пропорции сохраняются, недостающее поле дополняется полями. Размер и частота — из настроек плагина. Этим действием готовят куски перед склейкой.",
                "en": "Bring the file (in) to the project common frame size and frame rate: the aspect ratio is kept and the rest is padded. Size and rate come from the plugin settings. Use it to prepare pieces before concatenation.",
                "es": "Lleva el archivo (in) al tamano de cuadro y la frecuencia comunes del proyecto: se mantiene la proporcion y se rellena el resto.",
                "pt": "Leva o arquivo (in) ao tamanho de quadro e a taxa comuns do projeto: a proporcao e mantida e o resto e preenchido.",
                "zh-cn": "把文件（in）统一到项目的画幅与帧率：保持比例，多余部分补边。尺寸与帧率来自插件设置。拼接前用它准备素材。"
              }
            }
          ],

          "experience": [
            {
              "skill": "video-edit",
              "text": {
                "ru": "Перед монтажом материал прогоняют через ffmpeg: наши генерации это mp4/H.264, а бесплатный DaVinci Resolve под Linux H.264/H.265 не декодирует вовсе. Порядок такой: ffmpeg_uniform (общий кадр и частота) → ffmpeg_concat (склейка без перекодирования) → монтаж. Склейка кусков РАЗНОГО формата не работает: concat копирует потоки, а не пересчитывает их.",
                "en": "Run the footage through ffmpeg before editing: our generations are mp4/H.264, and free DaVinci Resolve on Linux does not decode H.264/H.265 at all. The order is ffmpeg_uniform (common frame and rate) → ffmpeg_concat (stream-copy join) → editing. Joining pieces of DIFFERENT formats does not work: concat copies streams instead of re-encoding them.",
                "es": "Antes de montar, pase el material por ffmpeg: nuestras generaciones son mp4/H.264 y el DaVinci Resolve gratuito en Linux no decodifica H.264/H.265. Orden: ffmpeg_uniform -> ffmpeg_concat -> montaje.",
                "pt": "Antes de editar, passe o material pelo ffmpeg: nossas geracoes sao mp4/H.264 e o DaVinci Resolve gratuito no Linux nao decodifica H.264/H.265. Ordem: ffmpeg_uniform -> ffmpeg_concat -> edicao.",
                "zh-cn": "剪辑前先用 ffmpeg 过一遍：我们生成的是 mp4/H.264，而 Linux 版免费 DaVinci Resolve 根本不解码 H.264/H.265。顺序是 ffmpeg_uniform → ffmpeg_concat → 剪辑。不同格式的片段无法拼接：concat 只复制流，不重新编码。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Действия «выполнить командную строку ffmpeg» в системе нет намеренно, и просить его бесполезно. Кодек, профиль, формат пикселей, размер кадра и частота задаются НАСТРОЙКОЙ записи плагина («Настройки → Плагины и MCP»), а не текстом задания: инструменту передают только пути. Нужен другой профиль — попросите человека поменять настройку.",
                "en": "There is deliberately no 'run an ffmpeg command line' action, and asking for one is pointless. Codec, profile, pixel format, frame size and rate are set in the plugin record settings (Settings → Plugins and MCP), not in the task text: the tool takes paths only. If another profile is needed, ask the human to change the setting.",
                "es": "No existe la accion «ejecutar una linea de comandos de ffmpeg» y pedirla no sirve. Codec, perfil, formato de pixel, tamano y frecuencia se fijan en los ajustes del plugin, no en el texto de la tarea.",
                "pt": "Nao existe a acao «executar uma linha de comando do ffmpeg» e pedi-la nao adianta. Codec, perfil, formato de pixel, tamanho e taxa ficam nos ajustes do plugin, nao no texto da tarefa.",
                "zh-cn": "系统故意没有「执行 ffmpeg 命令行」这个动作，索要也没有用。编码器、profile、像素格式、画幅和帧率都在插件设置里（设置 → 插件与 MCP），任务文本里改不了：工具只接收路径。"
              }
            },
            {
              "skill": "audio-music",
              "text": {
                "ru": "Звук из ролика вынимают действием ffmpeg_extract_audio, и вынимается он в WAV без сжатия. Причина не в качестве: AAC не поддержан даже в платной DaVinci Resolve Studio, а mp3 в монтаже даёт сдвиг по времени из-за тишины в начале кадра кодека. WAV читают все редакторы ветки.",
                "en": "Audio is pulled out of a clip with ffmpeg_extract_audio, and it comes out as uncompressed WAV. The reason is not quality: AAC is not supported even in paid DaVinci Resolve Studio, and mp3 shifts in time because of the encoder priming silence. Every editor of the branch reads WAV.",
                "es": "El audio se extrae con ffmpeg_extract_audio y sale como WAV sin comprimir: AAC no esta soportado ni en DaVinci Resolve Studio y el mp3 se desplaza en el tiempo.",
                "pt": "O audio e extraido com ffmpeg_extract_audio e sai como WAV sem compressao: AAC nao e suportado nem no DaVinci Resolve Studio e o mp3 desloca no tempo.",
                "zh-cn": "用 ffmpeg_extract_audio 提取音轨，输出是无压缩 WAV。原因不是音质：AAC 连付费的 DaVinci Resolve Studio 都不支持，而 mp3 因编码器前导静音会产生时间偏移。"
              }
            },
            {
              "skill": "audio-speech",
              "text": {
                "ru": "Речевую дорожку под ролик подкладывают так: сначала ffmpeg_extract_audio с готового ролика (узнать длительность и частоту), затем звук приводят к той же частоте 48 кГц. Синтезированная речь приходит в mp3 — в монтаж её кладут только через WAV, иначе дорожка уезжает от картинки на десятки миллисекунд.",
                "en": "To lay a speech track under a clip: first ffmpeg_extract_audio from the ready clip (to learn its length and rate), then bring the speech to the same 48 kHz. Synthesised speech arrives as mp3 — put it on the timeline only through WAV, otherwise the track drifts from the picture by tens of milliseconds.",
                "es": "Para poner voz bajo un clip: primero ffmpeg_extract_audio del clip listo, luego lleve la voz a los mismos 48 kHz. La voz sintetizada llega en mp3 y solo debe entrar al montaje via WAV.",
                "pt": "Para colocar voz sob um clipe: primeiro ffmpeg_extract_audio do clipe pronto, depois leve a voz aos mesmos 48 kHz. A voz sintetizada chega em mp3 e so deve entrar na edicao via WAV.",
                "zh-cn": "给片子铺配音：先对成片用 ffmpeg_extract_audio（了解时长与采样率），再把语音统一到 48 kHz。合成语音是 mp3，必须经 WAV 才能进时间线，否则会与画面差出几十毫秒。"
              }
            }
          ],

          "settings": [
            { "key": "videoCodec", "type": "choice", "default": "dnxhd", "choices": ["dnxhd", "prores_ks"],
              "title": { "ru": "Кодек промежуточного формата", "en": "Intermediate video codec", "es": "Codec intermedio de video", "pt": "Codec intermediario de video", "zh-cn": "中间格式视频编码器" } },
            { "key": "videoProfile", "type": "choice", "default": "dnxhr_hq", "choices": ["dnxhr_lb", "dnxhr_sq", "dnxhr_hq", "dnxhr_hqx", "0", "1", "2", "3"],
              "title": { "ru": "Профиль кодека (dnxhr_* для DNxHR, 0-3 для ProRes)", "en": "Codec profile (dnxhr_* for DNxHR, 0-3 for ProRes)", "es": "Perfil del codec", "pt": "Perfil do codec", "zh-cn": "编码 profile" } },
            { "key": "pixelFormat", "type": "choice", "default": "yuv422p", "choices": ["yuv422p", "yuv422p10le", "yuva444p10le"],
              "title": { "ru": "Формат пикселей", "en": "Pixel format", "es": "Formato de pixel", "pt": "Formato de pixel", "zh-cn": "像素格式" } },
            { "key": "audioCodec", "type": "choice", "default": "pcm_s16le", "choices": ["pcm_s16le", "pcm_s24le"],
              "title": { "ru": "Кодек звука", "en": "Audio codec", "es": "Codec de audio", "pt": "Codec de audio", "zh-cn": "音频编码器" } },
            { "key": "audioRate", "type": "choice", "default": "48000", "choices": ["44100", "48000"],
              "title": { "ru": "Частота дискретизации, Гц", "en": "Audio sample rate, Hz", "es": "Frecuencia de muestreo, Hz", "pt": "Taxa de amostragem, Hz", "zh-cn": "音频采样率（Hz）" } },
            { "key": "width", "type": "number", "default": 1920,
              "title": { "ru": "Ширина кадра", "en": "Frame width", "es": "Ancho del cuadro", "pt": "Largura do quadro", "zh-cn": "画面宽度" } },
            { "key": "height", "type": "number", "default": 1080,
              "title": { "ru": "Высота кадра", "en": "Frame height", "es": "Alto del cuadro", "pt": "Altura do quadro", "zh-cn": "画面高度" } },
            { "key": "fps", "type": "number", "default": 25,
              "title": { "ru": "Кадров в секунду", "en": "Frames per second", "es": "Cuadros por segundo", "pt": "Quadros por segundo", "zh-cn": "每秒帧数" } }
          ],

          "convert": {
            "timeoutSec": 7200,
            "progressEverySec": 15,
            "ops": [
              {
                "op": "intermediate",
                "inputs": "one",
                "outExt": ".mov",
                "outSuffix": "_edit",
                "args": ["-hide_banner", "-nostdin", "-y", "-i", "{in}",
                         "-map", "0:v:0?", "-c:v", "{videoCodec}", "-profile:v", "{videoProfile}", "-pix_fmt", "{pixelFormat}",
                         "-map", "0:a?", "-c:a", "{audioCodec}", "-ar", "{audioRate}",
                         "-progress", "pipe:1", "-nostats", "{out}"]
              },
              {
                "op": "audio",
                "inputs": "one",
                "outExt": ".wav",
                "outSuffix": "_audio",
                "args": ["-hide_banner", "-nostdin", "-y", "-i", "{in}",
                         "-vn", "-map", "0:a:0?", "-c:a", "{audioCodec}", "-ar", "{audioRate}",
                         "-progress", "pipe:1", "-nostats", "{out}"]
              },
              {
                "op": "concat",
                "inputs": "list",
                "outExt": "",
                "outName": "ai2p_concat",
                "listFile": "ai2p_concat.txt",
                "listLine": "file '{path}'",
                "args": ["-hide_banner", "-nostdin", "-y",
                         "-f", "concat", "-safe", "0", "-i", "{list}", "-c", "copy",
                         "-progress", "pipe:1", "-nostats", "{out}"]
              },
              {
                "op": "uniform",
                "inputs": "one",
                "outExt": ".mov",
                "outSuffix": "_uniform",
                "args": ["-hide_banner", "-nostdin", "-y", "-i", "{in}",
                         "-map", "0:v:0?", "-map", "0:a?",
                         "-vf", "scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps}",
                         "-c:v", "{videoCodec}", "-profile:v", "{videoProfile}", "-pix_fmt", "{pixelFormat}",
                         "-c:a", "{audioCodec}", "-ar", "{audioRate}",
                         "-progress", "pipe:1", "-nostats", "{out}"]
              }
            ]
          },

          "doc": {
            "ru": "plugins/tool.ffmpeg.md",
            "en": "plugins/tool.ffmpeg.md",
            "es": "plugins/tool.ffmpeg.md",
            "pt": "plugins/tool.ffmpeg.md",
            "zh-cn": "plugins/tool.ffmpeg.md"
          }
        }
        """;

    /// <summary>
    /// ШЛЮЗ В DAVINCI RESOLVE (T-117-S0) — экспорт OTIO, и БОЛЬШЕ НИЧЕГО.
    ///
    /// <para>ГЛАВНОЕ ОГРАНИЧЕНИЕ, ИЗ КОТОРОГО СЛЕДУЕТ ВЕСЬ ВИД ЭТОГО МАНИФЕСТА: бесплатный
    /// Resolve автоматизировать НЕЛЬЗЯ. Внешний скриптовый API (<c>DaVinciResolveScript</c>)
    /// в бесплатной версии закрыт, а с версии 19.1 (ноябрь 2024) закрыт окончательно — мост
    /// из отдельного процесса перестал принимать подключения, и в free скрипты идут только из
    /// внутренней консоли, руками. Поэтому цепочка такая: АГЕНТ КЛАДЁТ <c>.otio</c> → ЧЕЛОВЕК
    /// ИМПОРТИРУЕТ ЕГО МЫШЬЮ (File → Import → Timeline → OTIO). Ни блока <c>render</c>, ни
    /// второго действия здесь нет и быть не может.</para>
    ///
    /// <para>ФОРМАТ — OTIO (OpenTimelineIO, Academy Software Foundation): обычный JSON,
    /// который Resolve читает штатно начиная с 18.5, в том числе бесплатный. В сам проект
    /// Resolve мы не пишем никогда — это закрытая база данных.</para>
    ///
    /// <para>СОФТ ПОСТАВИТЬ НЕЛЬЗЯ: дистрибутив на 3–4 ГБ отдаётся за формой регистрации на
    /// сайте Blackmagic, постоянной прямой ссылки нет. Поэтому <c>package</c> ПУСТ намеренно —
    /// именно из-за пустого кода пакета <c>PluginSoftwareProbe</c> отвечает
    /// <c>NeedsManualPath</c> («укажите, где он уже стоит»), а не <c>CanInstall</c>. Версию
    /// Resolve по командной строке не отдаёт, поэтому <c>versionArgs</c> пуст: проверяется
    /// НАЛИЧИЕ исполняемого файла, и только оно.</para>
    ///
    /// <para>ЧЕГО OTIO НЕ ПЕРЕНОСИТ: эффекты, переходы и цветокоррекцию — это формат обмена
    /// монтажным решением, а не проектом; круговой обмен лоссовый. И субтитры: дорожки для
    /// них в OTIO нет, поэтому вид медиа <c>subtitle</c> ни на одну дорожку не раскладывается,
    /// а <c>.srt</c> человек кладёт в Resolve отдельно.</para>
    /// </summary>
    public const string ResolveJson = """
        {
          "code": "editor.resolve",
          "kind": "gateway",
          "version": 1,
          "name": "DaVinci Resolve (OTIO)",
          "description": {
            "ru": "Сборка монтажного листа OTIO из медиатеки проекта. Бесплатный Resolve скриптами не автоматизируется: агент кладёт файл .otio, а импортирует его человек — File → Import → Timeline → OTIO.",
            "en": "Builds an OTIO timeline from the project media library. Free Resolve cannot be scripted: the agent writes the .otio file and a human imports it — File → Import → Timeline → OTIO.",
            "es": "Crea una linea de tiempo OTIO a partir de la biblioteca multimedia del proyecto. El Resolve gratuito no se automatiza con scripts: el agente escribe el archivo .otio y la persona lo importa (File → Import → Timeline → OTIO).",
            "pt": "Monta uma linha do tempo OTIO a partir da biblioteca de midia do projeto. O Resolve gratuito nao e automatizavel por scripts: o agente grava o arquivo .otio e a pessoa o importa (File → Import → Timeline → OTIO).",
            "zh-cn": "根据项目媒体库生成 OTIO 时间线。免费版 Resolve 无法用脚本自动化：智能体写出 .otio 文件，由人工导入（File → Import → Timeline → OTIO）。"
          },

          "software": {
            "package": "",
            "required": false,
            "pathKey": "resolvePath",
            "system": {
              "commands": ["Resolve.exe", "resolve", "DaVinci Resolve"],
              "versionArgs": "",
              "minVersion": "",
              "maxVersion": ""
            },
            "hint": {
              "ru": "DaVinci Resolve мы не устанавливаем: дистрибутив на 3-4 ГБ отдаётся за формой регистрации на сайте Blackmagic Design, постоянной прямой ссылки нет. Поставьте его сами и укажите здесь путь — файл программы (Windows: Resolve.exe, Linux: /opt/resolve/bin/resolve) либо каталог, в который вы его поставили. Версию Resolve по командной строке не сообщает, поэтому проверяется только наличие файла.",
              "en": "We do not install DaVinci Resolve: the 3-4 GB distribution sits behind a registration form on the Blackmagic Design site and has no permanent direct link. Install it yourself and point here at the program file (Windows: Resolve.exe, Linux: /opt/resolve/bin/resolve) or at the folder you installed it into. Resolve does not report its version on the command line, so only the presence of the file is checked.",
              "es": "No instalamos DaVinci Resolve: el paquete de 3-4 GB esta tras un formulario de registro en el sitio de Blackmagic Design y no tiene enlace directo permanente. Instalelo usted e indique aqui el archivo del programa (Windows: Resolve.exe, Linux: /opt/resolve/bin/resolve) o la carpeta donde lo instalo. Solo se comprueba que el archivo exista.",
              "pt": "Nao instalamos o DaVinci Resolve: o pacote de 3-4 GB fica atras de um formulario de registro no site da Blackmagic Design e nao tem link direto permanente. Instale-o voce mesmo e informe aqui o arquivo do programa (Windows: Resolve.exe, Linux: /opt/resolve/bin/resolve) ou a pasta onde o instalou. So se verifica a existencia do arquivo.",
              "zh-cn": "我们不安装 DaVinci Resolve：3-4 GB 的安装包需要在 Blackmagic Design 网站填写注册表单才能下载，没有固定的直链。请自行安装后在此指定程序文件（Windows：Resolve.exe，Linux：/opt/resolve/bin/resolve）或安装目录。Resolve 不在命令行给出版本号，因此只检查文件是否存在。"
            }
          },

          "actions": [
            {
              "code": "AI2P.Plugins.Resolve.TimelineWrite",
              "tool": "resolve_timeline_write",
              "role": "export",
              "needsSoftware": false,
              "title": {
                "ru": "Собрать монтажный лист OTIO",
                "en": "Build the OTIO timeline",
                "es": "Crear la linea de tiempo OTIO",
                "pt": "Montar a linha do tempo OTIO",
                "zh-cn": "生成 OTIO 时间线"
              },
              "description": {
                "ru": "Собрать файл ai2p_scenes.otio из медиатеки проекта: дорожка видео и дорожка звука, порядок — как в media_list. Отбор: scene (сцена), kind (вид медиа), tag (тэг). Проект Resolve не трогается никогда, таймлайн НЕ появляется сам: человек импортирует файл руками (File → Import → Timeline → OTIO). Под Linux бесплатный Resolve не читает H.264/H.265, а AAC не читает нигде — перед сборкой прогоните материал через плагин «видео конвертор (ffmpeg)»: ffmpeg_to_edit для картинки и ffmpeg_extract_audio для звука.",
                "en": "Build ai2p_scenes.otio from the project media library: one video track and one audio track, ordered as in media_list. Filters: scene, kind, tag. The Resolve project is never touched and the timeline does NOT appear by itself: a human imports the file manually (File → Import → Timeline → OTIO). On Linux free Resolve reads neither H.264 nor H.265, and AAC is read nowhere — run the footage through the ffmpeg converter plugin first: ffmpeg_to_edit for picture, ffmpeg_extract_audio for sound.",
                "es": "Crea ai2p_scenes.otio desde la biblioteca multimedia del proyecto: una pista de video y una de audio, en el orden de media_list. Filtros: scene, kind, tag. El proyecto de Resolve nunca se toca y la linea de tiempo NO aparece sola: la persona importa el archivo a mano (File → Import → Timeline → OTIO). En Linux el Resolve gratuito no lee H.264/H.265 y el AAC no se lee en ningun sistema: pase antes el material por el plugin conversor ffmpeg.",
                "pt": "Cria ai2p_scenes.otio a partir da biblioteca de midia do projeto: uma trilha de video e uma de audio, na ordem de media_list. Filtros: scene, kind, tag. O projeto do Resolve nunca e alterado e a linha do tempo NAO aparece sozinha: a pessoa importa o arquivo manualmente (File → Import → Timeline → OTIO). No Linux o Resolve gratuito nao le H.264/H.265 e o AAC nao e lido em lugar nenhum: passe antes o material pelo plugin conversor ffmpeg.",
                "zh-cn": "根据项目媒体库生成 ai2p_scenes.otio：一条视频轨和一条音频轨，顺序与 media_list 相同。可按 scene、kind、tag 筛选。绝不改动 Resolve 工程，时间线也不会自动出现：需人工导入（File → Import → Timeline → OTIO）。Linux 上的免费版 Resolve 既不读 H.264 也不读 H.265，AAC 在任何系统都不支持——请先用 ffmpeg 转换器插件处理素材。"
              }
            }
          ],

          "experience": [
            {
              "skill": "video-edit",
              "text": {
                "ru": "Бесплатный DaVinci Resolve скриптами НЕ автоматизируется: внешний API DaVinciResolveScript в free закрыт, а с версии 19.1 (ноябрь 2024) мост из отдельного процесса не принимает подключения вовсе — скрипты идут только из внутренней консоли, руками. Поэтому шлюз делает ровно одно: агент кладёт файл ai2p_scenes.otio, а таймлайн создаёт ЧЕЛОВЕК — File → Import → Timeline → OTIO. Не обещайте в отчёте, что таймлайн появится сам, и не ждите этого: автоматизация есть только в платной Resolve Studio.",
                "en": "Free DaVinci Resolve CANNOT be scripted: the external DaVinciResolveScript API is closed in the free edition, and since version 19.1 (November 2024) the out-of-process bridge accepts no connections at all — scripts run only from the internal console, by hand. So the gateway does exactly one thing: the agent writes ai2p_scenes.otio, and the timeline is created by a HUMAN — File → Import → Timeline → OTIO. Do not promise in the report that the timeline will appear on its own: automation exists only in the paid Resolve Studio.",
                "es": "El DaVinci Resolve gratuito NO se automatiza con scripts: la API externa DaVinciResolveScript esta cerrada en la version gratuita y desde la 19.1 (noviembre de 2024) el puente entre procesos no acepta conexiones. El agente solo escribe ai2p_scenes.otio y la linea de tiempo la crea la PERSONA: File → Import → Timeline → OTIO. La automatizacion existe unicamente en Resolve Studio de pago.",
                "pt": "O DaVinci Resolve gratuito NAO e automatizavel por scripts: a API externa DaVinciResolveScript e fechada na versao gratuita e desde a 19.1 (novembro de 2024) a ponte entre processos nao aceita conexoes. O agente apenas grava ai2p_scenes.otio e a linha do tempo e criada pela PESSOA: File → Import → Timeline → OTIO. A automacao so existe no Resolve Studio pago.",
                "zh-cn": "免费版 DaVinci Resolve 无法用脚本自动化：外部 DaVinciResolveScript API 在免费版中被关闭，自 19.1（2024 年 11 月）起跨进程桥接完全不接受连接，脚本只能在内置控制台里手动运行。因此本网关只做一件事：智能体写出 ai2p_scenes.otio，时间线由人工创建（File → Import → Timeline → OTIO）。不要在报告里承诺时间线会自动出现——自动化只存在于付费的 Resolve Studio。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Под Linux бесплатный DaVinci Resolve НЕ декодирует H.264 и H.265 (в свободной сборке нет лицензированных кодеков), а звук AAC не поддержан даже в платной Studio. Наши генерации — ровно mp4/H.264 и aac/mp3, поэтому перед сборкой .otio материал ОБЯЗАН пройти через плагин «видео конвертор (ffmpeg)»: ffmpeg_to_edit переводит картинку в DNxHR или ProRes, ffmpeg_extract_audio вынимает звук в WAV. Пропустив этот шаг, вы получите таймлайн, где все клипы «Media Offline».",
                "en": "On Linux the free DaVinci Resolve does NOT decode H.264 or H.265 (the free build ships without the licensed codecs), and AAC audio is unsupported even in the paid Studio. Our generations are exactly mp4/H.264 and aac/mp3, so before building the .otio the footage MUST go through the ffmpeg converter plugin: ffmpeg_to_edit turns the picture into DNxHR or ProRes, ffmpeg_extract_audio pulls the sound into WAV. Skip that step and you get a timeline where every clip says Media Offline.",
                "es": "En Linux el DaVinci Resolve gratuito NO decodifica H.264 ni H.265, y el audio AAC no se soporta ni en Studio de pago. Nuestras generaciones son mp4/H.264 y aac/mp3, asi que antes de crear el .otio el material DEBE pasar por el plugin conversor ffmpeg: ffmpeg_to_edit a DNxHR o ProRes y ffmpeg_extract_audio a WAV. Sin ese paso todos los clips apareceran como Media Offline.",
                "pt": "No Linux o DaVinci Resolve gratuito NAO decodifica H.264 nem H.265, e o audio AAC nao e suportado nem no Studio pago. Nossas geracoes sao mp4/H.264 e aac/mp3, entao antes de montar o .otio o material PRECISA passar pelo plugin conversor ffmpeg: ffmpeg_to_edit para DNxHR ou ProRes e ffmpeg_extract_audio para WAV. Sem esse passo todos os clipes ficam como Media Offline.",
                "zh-cn": "Linux 上的免费版 DaVinci Resolve 不解码 H.264 和 H.265（免费构建不含授权编解码器），AAC 音频连付费版 Studio 也不支持。我们生成的正是 mp4/H.264 与 aac/mp3，因此在生成 .otio 之前，素材必须先经过 ffmpeg 转换器插件：ffmpeg_to_edit 把画面转成 DNxHR 或 ProRes，ffmpeg_extract_audio 把声音提取成 WAV。跳过这一步，时间线上每个片段都会显示 Media Offline。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "OTIO — формат ОБМЕНА монтажным решением, а не формат проекта: он переносит порядок клипов, их длины и ссылки на файлы, и НЕ переносит эффекты, переходы, цветокоррекцию, скорость и настройки Fusion. Круговой обмен лоссовый: выгрузив таймлайн из Resolve в .otio и вернув обратно, вы потеряете всю обработку. Поэтому наш .otio — это ЧЕРНОВАЯ СБОРКА (порядок и хронометраж), а всё оформление человек делает уже в Resolve; переписывать его сборку нашим файлом второй раз нельзя.",
                "en": "OTIO is an INTERCHANGE format for an editing decision, not a project format: it carries clip order, durations and file references, and does NOT carry effects, transitions, colour grading, retiming or Fusion setups. The round trip is lossy: exporting a timeline from Resolve to .otio and back loses all processing. So our .otio is a ROUGH ASSEMBLY (order and timing); the finishing is done by a human inside Resolve, and overwriting their work with our file a second time is not acceptable.",
                "es": "OTIO es un formato de INTERCAMBIO de la decision de montaje, no un formato de proyecto: lleva el orden de los clips, sus duraciones y las referencias a archivos, y NO lleva efectos, transiciones, etalonaje ni Fusion. El viaje de ida y vuelta es con perdida. Nuestro .otio es un montaje EN BRUTO; el acabado lo hace la persona dentro de Resolve.",
                "pt": "OTIO e um formato de INTERCAMBIO da decisao de edicao, nao um formato de projeto: leva a ordem dos clipes, suas duracoes e as referencias de arquivo, e NAO leva efeitos, transicoes, correcao de cor nem Fusion. O ciclo de ida e volta e com perda. Nosso .otio e uma montagem BRUTA; o acabamento a pessoa faz dentro do Resolve.",
                "zh-cn": "OTIO 是剪辑决定的交换格式，不是工程格式：它传递片段顺序、时长和文件引用，不传递特效、转场、调色、变速和 Fusion 设置。往返转换有损：把时间线从 Resolve 导出为 .otio 再导回，所有处理都会丢失。因此我们的 .otio 只是粗剪（顺序与时长），精修由人工在 Resolve 中完成，不能用我们的文件二次覆盖对方的成果。"
              }
            }
          ],

          "settings": [
            { "key": "fps", "type": "number", "default": 25,
              "title": { "ru": "Кадров в секунду", "en": "Frames per second", "es": "Cuadros por segundo", "pt": "Quadros por segundo", "zh-cn": "每秒帧数" } },
            { "key": "width", "type": "number", "default": 1920,
              "title": { "ru": "Ширина кадра", "en": "Frame width", "es": "Ancho del cuadro", "pt": "Largura do quadro", "zh-cn": "画面宽度" } },
            { "key": "height", "type": "number", "default": 1080,
              "title": { "ru": "Высота кадра", "en": "Frame height", "es": "Alto del cuadro", "pt": "Altura do quadro", "zh-cn": "画面高度" } }
          ],

          "export": {
            "file": "ai2p_scenes.otio",
            "escape": "json",
            "separator": ",\n",
            "fps": 25,
            "width": 1920,
            "height": 1080,
            "copyStore": true,
            "storeDir": "ai2p_media",
            "tracks": [
              { "id": "V1", "kind": "Video", "kinds": ["video", "image", "project"] },
              { "id": "A1", "kind": "Audio", "kinds": ["audio"] }
            ],
            "entry": [
              "          {",
              "            \"OTIO_SCHEMA\": \"Clip.1\",",
              "            \"name\": \"{clip.name}\",",
              "            \"metadata\": { \"ai2p\": { \"scene\": \"{clip.scene}\", \"take\": {clip.take}, \"order\": {clip.order}, \"kind\": \"{clip.kind}\", \"caption\": \"{clip.caption}\" } },",
              "            \"source_range\": {",
              "              \"OTIO_SCHEMA\": \"TimeRange.1\",",
              "              \"start_time\": { \"OTIO_SCHEMA\": \"RationalTime.1\", \"rate\": {clip.fps}, \"value\": 0 },",
              "              \"duration\": { \"OTIO_SCHEMA\": \"RationalTime.1\", \"rate\": {clip.fps}, \"value\": {clip.frames} }",
              "            },",
              "            \"effects\": [],",
              "            \"markers\": [],",
              "            \"enabled\": true,",
              "            \"media_reference\": {",
              "              \"OTIO_SCHEMA\": \"ExternalReference.1\",",
              "              \"name\": \"{clip.name}\",",
              "              \"metadata\": {},",
              "              \"available_range\": null,",
              "              \"available_image_bounds\": null,",
              "              \"target_url\": \"{clip.path}\"",
              "            }",
              "          }"
            ],
            "track": [
              "      {",
              "        \"OTIO_SCHEMA\": \"Track.1\",",
              "        \"name\": \"{track.id}\",",
              "        \"kind\": \"{track.kind}\",",
              "        \"metadata\": {},",
              "        \"source_range\": null,",
              "        \"effects\": [],",
              "        \"markers\": [],",
              "        \"enabled\": true,",
              "        \"children\": [",
              "{track.entries}",
              "        ]",
              "      }"
            ],
            "document": [
              "{",
              "  \"OTIO_SCHEMA\": \"Timeline.1\",",
              "  \"name\": \"{doc.title}\",",
              "  \"metadata\": { \"ai2p\": { \"generator\": \"AI2P\", \"clips\": {doc.count}, \"fps\": {doc.fps}, \"width\": {doc.width}, \"height\": {doc.height} } },",
              "  \"global_start_time\": { \"OTIO_SCHEMA\": \"RationalTime.1\", \"rate\": {doc.fps}, \"value\": 0 },",
              "  \"tracks\": {",
              "    \"OTIO_SCHEMA\": \"Stack.1\",",
              "    \"name\": \"{doc.title}\",",
              "    \"metadata\": {},",
              "    \"source_range\": null,",
              "    \"effects\": [],",
              "    \"markers\": [],",
              "    \"enabled\": true,",
              "    \"children\": [",
              "{tracks}",
              "    ]",
              "  }",
              "}",
              ""
            ]
          },

          "doc": {
            "ru": "plugins/editor.resolve.md",
            "en": "plugins/editor.resolve.md",
            "es": "plugins/editor.resolve.md",
            "pt": "plugins/editor.resolve.md",
            "zh-cn": "plugins/editor.resolve.md"
          }
        }
        """;

    /// <summary>
    /// ШЛЮЗ В BLENDER VSE (T-118-S0) — ПАРАМЕТРИЗОВАННЫЙ СКРИПТ PYTHON, а не правка
    /// <c>.blend</c>.
    ///
    /// <para>ПОЧЕМУ НЕ <c>.blend</c>: это бинарный дамп внутренних C-структур с блоком DNA
    /// (типы и смещения полей записаны в самом файле, версия от версии разные). Сторонним
    /// кодом в него не пишут — практический путь ровно один: МЫ ГЕНЕРИРУЕМ <c>.py</c>, а
    /// Blender его исполняет (<c>blender --background --python скрипт.py</c>) и сам сохраняет
    /// проект. Тот же запуск считает и ролик.</para>
    ///
    /// <para>ЗДЕСЬ БЕЗОПАСНОСТЬ ОСОБАЯ. Ограничение каталогом у Blender ИЛЛЮЗОРНО: Python
    /// внутри него открывает любой файл через <c>open()</c>, и никакое правило безопасности
    /// AI2P этого не видит. Реально ограничивает не путь, а то, что СКРИПТ ПИШЕМ МЫ. Отсюда
    /// два следствия, и оба сделаны:</para>
    /// <list type="number">
    /// <item>Действия «выполни этот Python» в справочнике НЕТ и быть не может. Сборка — это
    /// наш шаблон (<c>export.document</c>) плюс подстановки, а модель заполняет только
    /// параметры: отбор клипов (<c>scene</c>, <c>kind</c>, <c>tag</c>) и имя выходного файла.
    /// Все подстановки экранируются как строковый литерал Python
    /// (<see cref="TimelineFormat.EscapePython"/>): имя файла с кавычкой или переводом строки
    /// не рвёт скрипт и дописать в него код не позволяет.</item>
    /// <item>Рендер запускает ТОЛЬКО НАШ СОБРАННЫЙ ФАЙЛ — блок <c>render</c> помечен
    /// <c>ownFileOnly</c>, поэтому параметр «что запускать» агенту даже не объявляется
    /// (<see cref="RenderCommand.OwnFileOnly"/>). Иначе агент написал бы свой <c>.py</c>
    /// инструментом записи файлов и запустил бы его нашими руками.</item>
    /// </list>
    ///
    /// <para>ЧТО ДЕЛАЕТ СКРИПТ: строит дорожки VSE (видео и звук — разные каналы), ставит
    /// размер кадра и частоту, а дальше смотрит на РАСШИРЕНИЕ переданного результата —
    /// <c>.blend</c> сохранить проект, иначе посчитать ролик (FFMPEG, H.264 + AAC). Пути в
    /// скрипте только относительные: они считаются от каталога самого скрипта
    /// (<c>__file__</c>), а <c>save_as_mainfile(relative_remap=True)</c> оставляет их
    /// относительными и в сохранённом <c>.blend</c>.</para>
    /// </summary>
    public const string BlenderJson = """
        {
          "code": "editor.blender",
          "kind": "gateway",
          "version": 1,
          "name": "Blender VSE (скрипт Python)",
          "description": {
            "ru": "Сборка монтажа в Blender Video Sequence Editor скриптом Python: мы генерируем .py по своему шаблону, Blender исполняет его без окна и сам сохраняет проект (или считает ролик). В .blend напрямую не пишем — это бинарный дамп.",
            "en": "Builds an edit in the Blender Video Sequence Editor with a Python script: we generate a .py from our own template, Blender runs it headlessly and saves the project itself (or renders the movie). We never write .blend directly — it is a binary dump.",
            "es": "Monta en el Blender Video Sequence Editor mediante un script de Python: generamos un .py con nuestra plantilla, Blender lo ejecuta sin ventana y guarda el proyecto (o renderiza). Nunca escribimos el .blend directamente: es un volcado binario.",
            "pt": "Monta no Blender Video Sequence Editor por um script Python: geramos um .py a partir do nosso modelo, o Blender executa sem janela e salva o projeto (ou renderiza). Nunca escrevemos o .blend direto: ele e um despejo binario.",
            "zh-cn": "用 Python 脚本在 Blender 视频序列编辑器里搭建剪辑：我们按自己的模板生成 .py，Blender 无窗口执行并自行保存工程（或渲染成片）。绝不直接写 .blend——那是二进制转储。"
          },

          "software": {
            "package": "blender",
            "required": false,
            "pathKey": "blenderPath",
            "system": {
              "commands": ["blender", "blender.exe"],
              "versionArgs": "--version",
              "minVersion": "3.0",
              "maxVersion": ""
            },
            "hint": {
              "ru": "Нажмите «Установить» — портативный архив Blender скачается и распакуется сам; либо укажите путь к уже установленному blender (на Windows это blender.exe в каталоге программы).",
              "en": "Press Install to download and unpack the portable Blender archive, or point to an already installed blender (on Windows it is blender.exe in the program folder).",
              "es": "Pulse Instalar para descargar Blender portable, o indique la ruta de un blender ya instalado (en Windows es blender.exe en la carpeta del programa).",
              "pt": "Clique em Instalar para baixar o Blender portatil, ou informe o caminho de um blender ja instalado (no Windows e blender.exe na pasta do programa).",
              "zh-cn": "点击「安装」自动下载并解压便携版 Blender；也可以直接指定已安装的 blender（Windows 上是程序目录里的 blender.exe）。"
            }
          },

          "actions": [
            {
              "code": "AI2P.Plugins.Blender.TimelineWrite",
              "tool": "blender_timeline_write",
              "role": "export",
              "title": {
                "ru": "Собрать монтаж скриптом Blender",
                "en": "Build the Blender edit script",
                "es": "Crear el script de montaje de Blender",
                "pt": "Montar o script de edicao do Blender",
                "zh-cn": "生成 Blender 剪辑脚本"
              },
              "description": {
                "ru": "Собрать файл ai2p_timeline.py из медиатеки проекта: дорожка видео и дорожка звука VSE, порядок — как в media_list. Отбор: scene (сцена), kind (вид медиа), tag (тэг). Скрипт — НАШ шаблон, вы заполняете только параметры отбора и имя файла; произвольного Python в системе нет. Файл человека (.blend) не трогается.",
                "en": "Build ai2p_timeline.py from the project media library: one VSE video track and one audio track, ordered as in media_list. Filters: scene, kind, tag. The script is OUR template, you fill in only the filters and the file name; there is no arbitrary Python in the system. The human's .blend is never touched.",
                "es": "Crea ai2p_timeline.py desde la biblioteca multimedia del proyecto: una pista de video y una de audio del VSE, en el orden de media_list. Filtros: scene, kind, tag. El script es NUESTRA plantilla: usted solo rellena filtros y nombre de archivo. No existe Python libre.",
                "pt": "Cria ai2p_timeline.py a partir da biblioteca de midia do projeto: uma trilha de video e uma de audio do VSE, na ordem de media_list. Filtros: scene, kind, tag. O script e o NOSSO modelo: voce preenche apenas filtros e nome do arquivo. Nao existe Python livre.",
                "zh-cn": "根据项目媒体库生成 ai2p_timeline.py：一条 VSE 视频轨和一条音频轨，顺序与 media_list 相同。可按 scene、kind、tag 筛选。脚本是我们的模板，你只填筛选条件和文件名；系统里没有任意 Python。绝不改动人工的 .blend。"
              }
            },
            {
              "code": "AI2P.Plugins.Blender.Render",
              "tool": "blender_render",
              "role": "render",
              "needsSoftware": true,
              "title": {
                "ru": "Исполнить скрипт в Blender без окна",
                "en": "Run the script in headless Blender",
                "es": "Ejecutar el script en Blender sin ventana",
                "pt": "Executar o script no Blender sem janela",
                "zh-cn": "在无窗口 Blender 中执行脚本"
              },
              "description": {
                "ru": "Запустить blender --background --python на СОБРАННОМ нами скрипте: out с расширением .blend даёт готовый проект с дорожками (по умолчанию ai2p_timeline.blend), любое другое расширение — посчитанный ролик (mp4, H.264 + AAC). Что именно исполнять, выбрать нельзя: запускается только наш файл, своей командной строки у действия нет.",
                "en": "Run blender --background --python on the script WE built: an out ending in .blend gives a ready project with tracks (ai2p_timeline.blend by default), any other extension gives a rendered movie (mp4, H.264 + AAC). What to run cannot be chosen: only our own file is executed, and there is no arbitrary command line.",
                "es": "Ejecuta blender --background --python sobre el script que NOSOTROS generamos: un out con extension .blend da el proyecto con pistas, otra extension da el video (mp4, H.264 + AAC). No se puede elegir que ejecutar: solo nuestro archivo.",
                "pt": "Executa blender --background --python sobre o script que NOS geramos: um out com extensao .blend da o projeto com trilhas, outra extensao da o video (mp4, H.264 + AAC). Nao da para escolher o que executar: apenas o nosso arquivo.",
                "zh-cn": "对我们生成的脚本执行 blender --background --python：out 以 .blend 结尾则得到带轨道的工程（默认 ai2p_timeline.blend），其他扩展名则渲染成片（mp4，H.264 + AAC）。执行对象不可选择：只运行我们自己的文件，也没有任意命令行。"
              }
            }
          ],

          "experience": [
            {
              "skill": "video-edit",
              "text": {
                "ru": "Монтаж в Blender собирается СКРИПТОМ, а не правкой .blend: файл .blend — бинарный дамп внутренних структур с блоком DNA, сторонним кодом в него не пишут. Мы генерируем .py и отдаём его Blender: blender --background --python скрипт.py. Проект и рендер делает сам Blender.",
                "en": "A Blender edit is built by a SCRIPT, not by editing the .blend: a .blend is a binary dump of internal structures with a DNA block, and third-party code does not write into it. We generate a .py and hand it to Blender: blender --background --python script.py. Blender saves the project and renders it itself.",
                "es": "El montaje en Blender se arma con un SCRIPT, no editando el .blend: el .blend es un volcado binario con bloque DNA y no se escribe desde fuera. Generamos un .py y se lo damos a Blender: blender --background --python script.py.",
                "pt": "A edicao no Blender e feita por SCRIPT, nao editando o .blend: o .blend e um despejo binario com bloco DNA e nao se escreve nele de fora. Geramos um .py e entregamos ao Blender: blender --background --python script.py.",
                "zh-cn": "Blender 的剪辑用脚本搭建，而不是改 .blend：.blend 是带 DNA 块的内部结构二进制转储，外部代码写不进去。我们生成 .py 交给 Blender：blender --background --python script.py。工程保存与渲染都由 Blender 自己完成。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Скрипт для Blender — ЭТО НАШ ШАБЛОН, а вы заполняете только параметры: отбор клипов (scene, kind, tag) и имя выходного файла. Действия «выполни этот Python» в системе нет и не будет: Python внутри Blender открывает любой файл через open(), и ограничение путём там иллюзорно — реально ограничивает то, что текст скрипта пишем мы. Просить произвольный скрипт бесполезно.",
                "en": "The Blender script IS OUR TEMPLATE, and you fill in the parameters only: clip filters (scene, kind, tag) and the output file name. There is no 'run this Python' action and there never will be: Python inside Blender can open any file with open(), so a path restriction there is illusory — what really limits it is that we write the script text. Asking for an arbitrary script is pointless.",
                "es": "El script de Blender ES NUESTRA PLANTILLA y usted solo rellena parametros: filtros (scene, kind, tag) y el nombre del archivo. No existe la accion «ejecuta este Python»: dentro de Blender, Python abre cualquier archivo con open() y limitar por ruta es ilusorio.",
                "pt": "O script do Blender E O NOSSO MODELO e voce preenche apenas parametros: filtros (scene, kind, tag) e o nome do arquivo. Nao existe a acao «execute este Python»: dentro do Blender o Python abre qualquer arquivo com open(), e limitar por caminho e ilusorio.",
                "zh-cn": "Blender 脚本就是我们的模板，你只填参数：片段筛选（scene、kind、tag）和输出文件名。系统没有也不会有「执行这段 Python」的动作：Blender 里的 Python 能用 open() 打开任何文件，按路径限制是假的——真正的限制是脚本文本由我们编写。索要任意脚本没有意义。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Пути у шлюза Blender — только внутри папки проекта (либо внутри каталогов, открытых правилами безопасности задачи), и только относительные: и там, куда пишем скрипт, и внутри самого скрипта. Скрипт считает пути от своего каталога (__file__), а сохранённый .blend хранит их относительными — иначе проект не откроется ни на одном другом компьютере кластера.",
                "en": "Paths in the Blender gateway stay inside the project folder (or inside directories opened by the task security rules) and stay relative — both where the script is written and inside the script itself. The script resolves paths from its own folder (__file__), and the saved .blend keeps them relative; otherwise the project opens nowhere else in the cluster.",
                "es": "Las rutas del gateway de Blender solo van dentro de la carpeta del proyecto (o de directorios abiertos por las reglas de la tarea) y son relativas. El script resuelve rutas desde su propia carpeta (__file__) y el .blend guardado las mantiene relativas.",
                "pt": "Os caminhos do gateway do Blender ficam dentro da pasta do projeto (ou de diretorios abertos pelas regras da tarefa) e sao relativos. O script resolve caminhos a partir da propria pasta (__file__) e o .blend salvo os mantem relativos.",
                "zh-cn": "Blender 网关的路径只能在项目文件夹内（或任务安全规则开放的目录内），并且只用相对路径：写脚本的位置如此，脚本内部也如此。脚本以自身目录（__file__）解析路径，保存的 .blend 也保持相对路径，否则集群里别的机器打不开。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "VSE как монтажка слаб: нет вложенных таймлайнов, беден набор переходов, звук почти без обработки, а набор кодеков у сборок Blender разный. Берите этот шлюз, если в проекте И ТАК ЕСТЬ 3D (титры, сцена, композитинг) и монтаж хочется держать в одном файле с ней. Если 3D в проекте нет — берите Shotcut/Kdenlive: там и монтаж богаче, и рендер без окна проще.",
                "en": "The VSE is a weak editor: no nested timelines, few transitions, almost no audio processing, and Blender builds ship different codec sets. Take this gateway when the project ALREADY HAS 3D (titles, a scene, compositing) and you want the edit in the same file. With no 3D in the project take Shotcut/Kdenlive instead: richer editing and simpler headless rendering.",
                "es": "El VSE es un editor debil: sin lineas de tiempo anidadas, pocas transiciones, casi sin proceso de audio y con juegos de codecs distintos segun la compilacion. Use este gateway si el proyecto YA TIENE 3D; si no, use Shotcut/Kdenlive.",
                "pt": "O VSE e um editor fraco: sem linhas do tempo aninhadas, poucas transicoes, quase sem processamento de audio e com conjuntos de codecs diferentes por compilacao. Use este gateway se o projeto JA TEM 3D; se nao, use Shotcut/Kdenlive.",
                "zh-cn": "VSE 作为剪辑软件偏弱：没有嵌套时间线、转场少、几乎没有音频处理，各版 Blender 的编解码器集合也不同。若项目本来就有 3D（字幕、场景、合成），用这个网关把剪辑放进同一个文件；若没有 3D，请选 Shotcut/Kdenlive。"
              }
            }
          ],

          "settings": [
            { "key": "fps", "type": "number", "default": 25,
              "title": { "ru": "Кадров в секунду", "en": "Frames per second", "es": "Cuadros por segundo", "pt": "Quadros por segundo", "zh-cn": "每秒帧数" } },
            { "key": "width", "type": "number", "default": 1920,
              "title": { "ru": "Ширина кадра", "en": "Frame width", "es": "Ancho del cuadro", "pt": "Largura do quadro", "zh-cn": "画面宽度" } },
            { "key": "height", "type": "number", "default": 1080,
              "title": { "ru": "Высота кадра", "en": "Frame height", "es": "Alto del cuadro", "pt": "Altura do quadro", "zh-cn": "画面高度" } }
          ],

          "export": {
            "file": "ai2p_timeline.py",
            "escape": "python",
            "fps": 25,
            "width": 1920,
            "height": 1080,
            "clipIdPrefix": "clip",
            "copyStore": true,
            "storeDir": "ai2p_media",
            "tracks": [
              { "id": "V1", "kind": "video", "kinds": ["video", "image", "project"] },
              { "id": "A1", "kind": "audio", "kinds": ["audio"] }
            ],
            "entry": [
              "        [\"{clip.path}\", \"{clip.kind}\", \"{clip.caption}\", {clip.frames}, \"{clip.scene}\", {clip.take}],",
              ""
            ],
            "track": [
              "    [{track.number}, \"{track.id}\", [",
              "{track.entries}    ]],",
              ""
            ],
            "document": [
              "# -*- coding: utf-8 -*-",
              "# Собрано AI2P из медиатеки проекта по ШАБЛОНУ ПЛАГИНА. Это НАШ файл: он",
              "# перезаписывается целиком, правьте свой. Произвольного скрипта здесь быть не может:",
              "# подставляются только данные медиатеки, и все они экранированы как строки Python.",
              "# Запуск: blender --background --factory-startup --python ai2p_timeline.py -- <результат>",
              "# Результат с расширением .blend — сохранить проект, любой другой — посчитать ролик.",
              "import os",
              "import sys",
              "",
              "import bpy",
              "",
              "NAME = \"{doc.title}\"",
              "FPS = {doc.fps}",
              "WIDTH = {doc.width}",
              "HEIGHT = {doc.height}",
              "OWN = \"ai2p_timeline.blend\"",
              "",
              "# Дорожка: [канал, имя, клипы]. Клип: [путь, вид, подпись, кадров, сцена, дубль].",
              "# Пути ТОЛЬКО относительные — они считаются от каталога этого файла.",
              "TRACKS = [",
              "{tracks}]",
              "",
              "BASE = os.path.dirname(os.path.abspath(__file__))",
              "",
              "",
              "def target():",
              "    if \"--\" in sys.argv:",
              "        rest = sys.argv[sys.argv.index(\"--\") + 1:]",
              "        if rest:",
              "            return rest[0]",
              "    return os.path.join(BASE, OWN)",
              "",
              "",
              "def strips(editor):",
              "    # 4.4 переименовала sequences в strips; понимаем оба имени",
              "    found = getattr(editor, \"strips\", None)",
              "    return editor.sequences if found is None else found",
              "",
              "",
              "def add(seq, kind, name, path, channel, start, frames):",
              "    if kind == \"audio\":",
              "        return seq.new_sound(name=name, filepath=path, channel=channel, frame_start=start)",
              "    if kind == \"image\":",
              "        strip = seq.new_image(name=name, filepath=path, channel=channel, frame_start=start)",
              "        strip.frame_final_duration = frames",
              "        return strip",
              "    return seq.new_movie(name=name, filepath=path, channel=channel, frame_start=start)",
              "",
              "",
              "def build():",
              "    scene = bpy.context.scene",
              "    scene.render.fps = int(round(FPS))",
              "    scene.render.fps_base = 1.0",
              "    scene.render.resolution_x = WIDTH",
              "    scene.render.resolution_y = HEIGHT",
              "    scene.render.resolution_percentage = 100",
              "    if scene.sequence_editor is None:",
              "        scene.sequence_editor_create()",
              "    seq = strips(scene.sequence_editor)",
              "    for old in list(seq):",
              "        seq.remove(old)",
              "    last = 1",
              "    for channel, track, clips in TRACKS:",
              "        start = 1",
              "        for path, kind, caption, frames, shot, take in clips:",
              "            full = os.path.normpath(os.path.join(BASE, path))",
              "            if not os.path.exists(full):",
              "                print(\"AI2P: нет файла\", path)",
              "                continue",
              "            label = caption if caption else os.path.basename(path)",
              "            strip = add(seq, kind, label, full, channel, start, frames)",
              "            start = int(strip.frame_final_end)",
              "            last = max(last, start)",
              "    scene.frame_start = 1",
              "    scene.frame_end = max(1, last - 1)",
              "    print(\"AI2P: дорожек\", len(TRACKS), \"клипов\", len(list(seq)))",
              "    return scene",
              "",
              "",
              "def save(scene, out):",
              "    if out.lower().endswith(\".blend\"):",
              "        bpy.ops.wm.save_as_mainfile(filepath=out, relative_remap=True)",
              "        print(\"AI2P: проект сохранён\", out)",
              "        return",
              "    scene.render.filepath = out",
              "    scene.render.use_file_extension = False",
              "    scene.render.image_settings.file_format = \"FFMPEG\"",
              "    scene.render.ffmpeg.format = \"MPEG4\"",
              "    scene.render.ffmpeg.codec = \"H264\"",
              "    scene.render.ffmpeg.audio_codec = \"AAC\"",
              "    bpy.ops.render.render(animation=True)",
              "    print(\"AI2P: ролик посчитан\", out)",
              "",
              "",
              "save(build(), target())",
              ""
            ]
          },

          "render": {
            "args": ["--background", "--factory-startup", "--python-exit-code", "1",
                     "--python", "{file}", "--", "{out}"],
            "outExt": ".blend",
            "ownFileOnly": true,
            "timeoutSec": 3600
          },

          "doc": {
            "ru": "plugins/editor.blender.md",
            "en": "plugins/editor.blender.md",
            "es": "plugins/editor.blender.md",
            "pt": "plugins/editor.blender.md",
            "zh-cn": "plugins/editor.blender.md"
          }
        }
        """;

    /// <summary>
    /// ШЛЮЗ В OPENSHOT (T-120-S0) — ЗАПАСНОЙ, и это сказано вслух и в опыте, и в документе.
    ///
    /// <para>ПО ФОРМАЛЬНЫМ ПРИЗНАКАМ ОН ЛУЧШИЙ ИЗ ЧЕТЫРЁХ: кроссплатформенный, бесплатный,
    /// формат проекта <c>.osp</c> — ЧИСТЫЙ JSON в UTF-8 (начиная с OpenShot 2.0), который
    /// пишется напрямую и не требует ни исполнения кода (в отличие от Blender), ни ручного
    /// импорта чужого формата (в отличие от OTIO у Resolve): человек просто открывает файл.
    /// ПО НАДЁЖНОСТИ ОН ХУЖЕ: репутация стабильности у OpenShot слабая, а рендера без окна у
    /// него нет вовсе — <c>openshot-qt</c> считает ролик только своим окном, и блока
    /// <c>render</c> здесь поэтому нет (обещать рендер, которого нет, хуже, чем не иметь его).
    /// Отсюда правило ветки: первый выбор — Shotcut/Kdenlive (там melt), этот — второй.</para>
    ///
    /// <para>ЧЕМ .OSP ОТЛИЧАЕТСЯ ОТ ОСТАЛЬНЫХ ТРЁХ ФОРМАТОВ И ЧТО ИЗ ЭТОГО СЛЕДУЕТ ДЛЯ
    /// ЭКСПОРТЁРА: список клипов у него ПЛОСКИЙ (<c>clips</c> — один массив на весь файл), а
    /// дорожку и место на ней называет САМ КЛИП полями <c>layer</c> и <c>position</c>. В MLT и
    /// OTIO дорожка — это последовательность, и клипы встают подряд сами; здесь без начала
    /// клипа все они легли бы друг на друга в нуле. Поэтому в общий движок добавлены
    /// <c>{clip.positionSec}</c> (начало клипа на дорожке) и <c>{track.layer}</c> (номер слоя
    /// в терминах формата), а записи дорожки стали видеть поля своей дорожки — три добавки
    /// того же рода, что <c>separator</c> и <c>{track.kind}</c> у T-117-S0.</para>
    ///
    /// <para>ПУТИ ТОЛЬКО ОТНОСИТЕЛЬНЫЕ — и это РОДНОЙ вид файла, а не наша выдумка: сам
    /// OpenShot сохраняет проект с относительными путями (<c>path_mode="relative"</c>) и
    /// разворачивает их при открытии от каталога <c>.osp</c>. Ключ пути в JSON называется
    /// ровно <c>path</c>: по нему идёт разбор.</para>
    ///
    /// <para>СОФТ ПОСТАВИТЬ НЕЧЕМ, И <c>package</c> ПУСТ НАМЕРЕННО — как у Resolve. Под
    /// Windows OpenShot отдаётся ТОЛЬКО установщиком <c>OpenShot-v4.0.0-x86_64.exe</c>
    /// (229 078 328 байт, HEAD 03.09.2026), портативного архива у проекта нет, а наш
    /// установщик пакетов распаковывает только zip/tar/gzip и чужих установщиков не запускает.
    /// Поэтому человеку — поле «укажите, где он уже стоит», а ссылка и размер названы в
    /// подсказке. Сборке экспорт OpenShot не нужен вовсе: файл пишем мы.</para>
    ///
    /// <para>ВЕРСИИ В БЛОКЕ <c>version</c> НЕ ВЫДУМАНЫ: OpenShot 4.0.0 и libopenshot 1.0.0 —
    /// выпуски от 30.08.2026, снятые списком релизов. Число тут не украшение: при открытии
    /// OpenShot гоняет по версии свои шаги обновления старых проектов, и заниженная версия
    /// означала бы, что он переделает наш файл «на всякий случай».</para>
    /// </summary>
    public const string OpenShotJson = """
        {
          "code": "editor.openshot",
          "kind": "gateway",
          "version": 1,
          "name": "OpenShot (.osp)",
          "description": {
            "ru": "Сборка проекта OpenShot (.osp — чистый JSON) из медиатеки проекта. Файл пишется напрямую и открывается двойным щелчком. Рендера без окна у OpenShot нет: считать ролик человек запускает сам либо берёт шлюз в Shotcut/Kdenlive.",
            "en": "Builds an OpenShot project (.osp — plain JSON) from the project media library. The file is written directly and opens with a double click. OpenShot has no headless render: the human renders by hand, or the Shotcut/Kdenlive gateway is used instead.",
            "es": "Crea un proyecto de OpenShot (.osp, JSON puro) a partir de la biblioteca multimedia del proyecto. El archivo se escribe directamente y se abre con doble clic. OpenShot no tiene render sin ventana.",
            "pt": "Monta um projeto do OpenShot (.osp, JSON puro) a partir da biblioteca de midia do projeto. O arquivo e gravado direto e abre com um duplo clique. O OpenShot nao tem render sem janela.",
            "zh-cn": "根据项目媒体库生成 OpenShot 工程（.osp，纯 JSON）。文件直接写出，双击即可打开。OpenShot 没有无窗口渲染。"
          },

          "software": {
            "package": "",
            "required": false,
            "pathKey": "openshotPath",
            "system": {
              "commands": ["openshot-qt", "openshot-qt.exe", "openshot.exe", "launch-openshot"],
              "versionArgs": "",
              "minVersion": "",
              "maxVersion": ""
            },
            "hint": {
              "ru": "OpenShot мы не устанавливаем: под Windows он отдаётся только установщиком OpenShot-v4.0.0-x86_64.exe (229 078 328 байт, https://github.com/OpenShot/openshot-qt/releases), портативного архива у проекта нет, а мы распаковываем архивы, а не запускаем чужие установщики. Поставьте его сами и укажите здесь путь — файл программы (Windows: openshot-qt.exe, Linux: openshot-qt из пакета или AppImage) либо каталог, куда вы его поставили. Linux: apt install openshot-qt; macOS: dmg с того же адреса. Версию OpenShot по командной строке не сообщает, поэтому проверяется только наличие файла.",
              "en": "We do not install OpenShot: on Windows it ships only as the installer OpenShot-v4.0.0-x86_64.exe (229,078,328 bytes, https://github.com/OpenShot/openshot-qt/releases), the project has no portable archive, and we unpack archives rather than run third-party installers. Install it yourself and point here at the program file (Windows: openshot-qt.exe, Linux: openshot-qt from the package or the AppImage) or at the folder you installed it into. Linux: apt install openshot-qt; macOS: the dmg from the same page. OpenShot does not report its version on the command line, so only the presence of the file is checked.",
              "es": "No instalamos OpenShot: en Windows solo se entrega como instalador OpenShot-v4.0.0-x86_64.exe (229 078 328 bytes, https://github.com/OpenShot/openshot-qt/releases) y no hay archivo portable. Instalelo usted e indique aqui el archivo del programa (Windows: openshot-qt.exe, Linux: openshot-qt o el AppImage) o la carpeta de instalacion. Linux: apt install openshot-qt. Solo se comprueba que el archivo exista.",
              "pt": "Nao instalamos o OpenShot: no Windows ele vem apenas como instalador OpenShot-v4.0.0-x86_64.exe (229 078 328 bytes, https://github.com/OpenShot/openshot-qt/releases) e nao ha pacote portatil. Instale-o voce mesmo e informe aqui o arquivo do programa (Windows: openshot-qt.exe, Linux: openshot-qt ou o AppImage) ou a pasta de instalacao. Linux: apt install openshot-qt. So se verifica a existencia do arquivo.",
              "zh-cn": "我们不安装 OpenShot：Windows 上它只提供安装程序 OpenShot-v4.0.0-x86_64.exe（229 078 328 字节，https://github.com/OpenShot/openshot-qt/releases），官方没有便携压缩包，而我们只解压压缩包、不运行第三方安装程序。请自行安装后在此指定程序文件（Windows：openshot-qt.exe；Linux：openshot-qt 或 AppImage）或安装目录。Linux 可用 apt install openshot-qt。OpenShot 不在命令行给出版本号，因此只检查文件是否存在。"
            }
          },

          "actions": [
            {
              "code": "AI2P.Plugins.OpenShot.TimelineWrite",
              "tool": "openshot_timeline_write",
              "role": "export",
              "needsSoftware": false,
              "title": {
                "ru": "Собрать проект OpenShot (.osp)",
                "en": "Build the OpenShot project (.osp)",
                "es": "Crear el proyecto de OpenShot (.osp)",
                "pt": "Montar o projeto do OpenShot (.osp)",
                "zh-cn": "生成 OpenShot 工程（.osp）"
              },
              "description": {
                "ru": "Собрать файл ai2p_library.osp из медиатеки проекта: дорожка видео (слой L2) и дорожка звука (слой L1), порядок — как в media_list. Отбор: scene (сцена), kind (вид медиа), tag (тэг). Файл человека не трогается: пишется только свой, и открывает его человек. Рендера у этого шлюза нет — OpenShot без окна считать не умеет.",
                "en": "Build ai2p_library.osp from the project media library: a video track (layer L2) and an audio track (layer L1), ordered as in media_list. Filters: scene, kind, tag. The human's own project file is never touched: we write only ours, and a human opens it. This gateway has no render — OpenShot cannot render headlessly.",
                "es": "Crea ai2p_library.osp desde la biblioteca multimedia del proyecto: una pista de video (capa L2) y una de audio (capa L1), en el orden de media_list. Filtros: scene, kind, tag. El archivo de la persona nunca se toca. Este gateway no renderiza: OpenShot no sabe hacerlo sin ventana.",
                "pt": "Cria ai2p_library.osp a partir da biblioteca de midia do projeto: uma trilha de video (camada L2) e uma de audio (camada L1), na ordem de media_list. Filtros: scene, kind, tag. O arquivo da pessoa nunca e alterado. Este gateway nao renderiza: o OpenShot nao sabe fazer isso sem janela.",
                "zh-cn": "根据项目媒体库生成 ai2p_library.osp：一条视频轨（图层 L2）和一条音频轨（图层 L1），顺序与 media_list 相同。可按 scene、kind、tag 筛选。绝不改动人工创建的工程文件，由人工打开。本网关不提供渲染——OpenShot 无法无窗口渲染。"
              }
            }
          ],

          "experience": [
            {
              "skill": "video-edit",
              "text": {
                "ru": "Проект OpenShot (.osp) — это ЧИСТЫЙ JSON в UTF-8 (так с версии 2.0), и мы пишем его напрямую: ни исполнения кода, ни импорта чужого формата не нужно, человек открывает файл двойным щелчком. Устройство: files — список ресурсов, clips — ПЛОСКИЙ список клипов, и дорожку с местом называет сам клип полями layer (номер слоя из layers) и position (секунды от начала). Дорожки-последовательности, как в MLT, здесь нет.",
                "en": "An OpenShot project (.osp) is plain UTF-8 JSON (since version 2.0) and we write it directly: no code execution and no foreign-format import, the human just double-clicks the file. Layout: files is the resource list, clips is a FLAT list, and each clip names its track and place itself via layer (a number from layers) and position (seconds from the start). There is no track-as-sequence like in MLT.",
                "es": "Un proyecto de OpenShot (.osp) es JSON puro en UTF-8 (desde la version 2.0) y lo escribimos directamente. Estructura: files es la lista de recursos, clips es una lista PLANA, y cada clip indica su pista y su lugar con layer (numero de la capa) y position (segundos).",
                "pt": "Um projeto do OpenShot (.osp) e JSON puro em UTF-8 (desde a versao 2.0) e o gravamos direto. Estrutura: files e a lista de recursos, clips e uma lista PLANA, e cada clipe indica sua trilha e seu lugar com layer (numero da camada) e position (segundos).",
                "zh-cn": "OpenShot 工程（.osp）是纯 UTF-8 JSON（自 2.0 起），我们直接写出：不执行代码，也不需要导入外来格式，人工双击即可打开。结构：files 是素材列表，clips 是扁平列表，每个片段自己用 layer（图层编号）和 position（起始秒数）说明所在轨道与位置。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "Пути в .osp — ТОЛЬКО относительно каталога самого файла проекта, и это родной вид формата: OpenShot сам сохраняет проект с относительными путями и разворачивает их при открытии. Абсолютный путь убивает переносимость по кластеру — у соседа тот же ролик лежит по другому пути, и проект откроется со всеми клипами «файл не найден». Ключ в JSON называется ровно path.",
                "en": "Paths in an .osp are ONLY relative to the folder of the project file, and this is the format's native form: OpenShot itself saves relative paths and expands them on open. An absolute path kills portability across the cluster — on another machine the same clip sits elsewhere and every clip opens as missing. The JSON key is exactly path.",
                "es": "Las rutas del .osp son SOLO relativas a la carpeta del archivo de proyecto, y asi lo guarda el propio OpenShot. Una ruta absoluta rompe la portabilidad en el cluster. La clave en JSON se llama exactamente path.",
                "pt": "Os caminhos no .osp sao SOMENTE relativos a pasta do arquivo de projeto, e e assim que o proprio OpenShot salva. Caminho absoluto quebra a portabilidade no cluster. A chave no JSON se chama exatamente path.",
                "zh-cn": "在 .osp 中，路径只能相对于工程文件所在目录——这正是该格式的原生做法：OpenShot 自己就保存相对路径并在打开时展开。绝对路径会破坏集群内的可移植性。JSON 里的键名就是 path。"
              }
            },
            {
              "skill": "video-edit",
              "text": {
                "ru": "У OpenShot слабая репутация стабильности, и рендера без окна у него нет вовсе. Поэтому шлюз в OpenShot — ЗАПАСНОЙ: первый выбор для монтажа Shotcut/Kdenlive (там есть melt — ролик считается без человека), а OpenShot берите, если человек работает именно в нём или если Shotcut на этой машине не стоит. Не обещайте в отчёте, что ролик посчитается сам: по .osp его считает человек, открыв проект.",
                "en": "OpenShot has a weak stability reputation and no headless render at all. So the OpenShot gateway is the BACKUP one: the first choice for editing is Shotcut/Kdenlive (melt renders without a human), and OpenShot is for when the human works in it or Shotcut is not installed on that machine. Do not promise in the report that the movie will be rendered by itself: from an .osp a human renders it, by opening the project.",
                "es": "OpenShot tiene fama de poca estabilidad y no tiene render sin ventana. Por eso este gateway es el DE RESERVA: la primera opcion es Shotcut/Kdenlive (con melt). No prometa que la pelicula se renderizara sola: desde un .osp la renderiza una persona.",
                "pt": "O OpenShot tem fama de pouca estabilidade e nao tem render sem janela. Por isso este gateway e o RESERVA: a primeira escolha e Shotcut/Kdenlive (com melt). Nao prometa que o filme sera renderizado sozinho: a partir de um .osp quem renderiza e a pessoa.",
                "zh-cn": "OpenShot 的稳定性口碑一般，而且完全没有无窗口渲染。因此 OpenShot 网关是备选：剪辑首选 Shotcut/Kdenlive（有 melt，可无人渲染）。不要在报告里承诺成片会自动生成——.osp 需要人工打开工程再渲染。"
              }
            }
          ],

          "settings": [
            { "key": "fps", "type": "number", "default": 25,
              "title": { "ru": "Кадров в секунду", "en": "Frames per second", "es": "Cuadros por segundo", "pt": "Quadros por segundo", "zh-cn": "每秒帧数" } },
            { "key": "width", "type": "number", "default": 1920,
              "title": { "ru": "Ширина кадра", "en": "Frame width", "es": "Ancho del cuadro", "pt": "Largura do quadro", "zh-cn": "画面宽度" } },
            { "key": "height", "type": "number", "default": 1080,
              "title": { "ru": "Высота кадра", "en": "Frame height", "es": "Alto del cuadro", "pt": "Altura do quadro", "zh-cn": "画面高度" } }
          ],

          "export": {
            "file": "ai2p_library.osp",
            "escape": "json",
            "separator": ",\n",
            "fps": 25,
            "width": 1920,
            "height": 1080,
            "clipIdPrefix": "F",
            "copyStore": true,
            "storeDir": "ai2p_media",
            "services": {
              "video": "FFmpegReader",
              "audio": "FFmpegReader",
              "image": "QtImageReader"
            },
            "tracks": [
              { "id": "V1", "layer": "2000000", "kinds": ["video", "image"] },
              { "id": "A1", "layer": "1000000", "kinds": ["audio"] }
            ],
            "clipByKind": {
              "video": [
                "  {",
                "   \"id\": \"{clip.id}\",",
                "   \"path\": \"{clip.path}\",",
                "   \"name\": \"{clip.name}\",",
                "   \"media_type\": \"video\",",
                "   \"type\": \"{clip.service}\",",
                "   \"duration\": {clip.durationSec},",
                "   \"video_length\": \"{clip.frames}\",",
                "   \"fps\": { \"num\": {clip.fps}, \"den\": 1 },",
                "   \"has_video\": true,",
                "   \"has_audio\": true,",
                "   \"has_single_image\": false,",
                "   \"metadata\": { \"ai2p_scene\": \"{clip.scene}\", \"ai2p_take\": \"{clip.take}\", \"ai2p_caption\": \"{clip.caption}\" }",
                "  }"
              ],
              "audio": [
                "  {",
                "   \"id\": \"{clip.id}\",",
                "   \"path\": \"{clip.path}\",",
                "   \"name\": \"{clip.name}\",",
                "   \"media_type\": \"audio\",",
                "   \"type\": \"{clip.service}\",",
                "   \"duration\": {clip.durationSec},",
                "   \"video_length\": \"{clip.frames}\",",
                "   \"fps\": { \"num\": {clip.fps}, \"den\": 1 },",
                "   \"has_video\": false,",
                "   \"has_audio\": true,",
                "   \"has_single_image\": false,",
                "   \"metadata\": { \"ai2p_scene\": \"{clip.scene}\", \"ai2p_take\": \"{clip.take}\", \"ai2p_caption\": \"{clip.caption}\" }",
                "  }"
              ],
              "image": [
                "  {",
                "   \"id\": \"{clip.id}\",",
                "   \"path\": \"{clip.path}\",",
                "   \"name\": \"{clip.name}\",",
                "   \"media_type\": \"image\",",
                "   \"type\": \"{clip.service}\",",
                "   \"duration\": {clip.durationSec},",
                "   \"video_length\": \"{clip.frames}\",",
                "   \"fps\": { \"num\": {clip.fps}, \"den\": 1 },",
                "   \"has_video\": true,",
                "   \"has_audio\": false,",
                "   \"has_single_image\": true,",
                "   \"metadata\": { \"ai2p_scene\": \"{clip.scene}\", \"ai2p_take\": \"{clip.take}\", \"ai2p_caption\": \"{clip.caption}\" }",
                "  }"
              ]
            },
            "entry": [
              "  {",
              "   \"id\": \"C{clip.index}\",",
              "   \"file_id\": \"{clip.id}\",",
              "   \"title\": \"{clip.caption}\",",
              "   \"layer\": {track.layer},",
              "   \"position\": {clip.positionSec},",
              "   \"start\": 0,",
              "   \"end\": {clip.durationSec},",
              "   \"duration\": {clip.durationSec},",
              "   \"reader\": {",
              "    \"type\": \"{clip.service}\",",
              "    \"path\": \"{clip.path}\",",
              "    \"media_type\": \"{clip.kind}\",",
              "    \"duration\": {clip.durationSec},",
              "    \"video_length\": \"{clip.frames}\",",
              "    \"fps\": { \"num\": {clip.fps}, \"den\": 1 }",
              "   }",
              "  }"
            ],
            "track": ["{track.entries}"],
            "document": [
              "{",
              " \"id\": \"AI2P000001\",",
              " \"fps\": { \"num\": {doc.fps}, \"den\": 1 },",
              " \"display_ratio\": { \"num\": 16, \"den\": 9 },",
              " \"pixel_ratio\": { \"num\": 1, \"den\": 1 },",
              " \"width\": {doc.width},",
              " \"height\": {doc.height},",
              " \"sample_rate\": 48000,",
              " \"channels\": 2,",
              " \"channel_layout\": 3,",
              " \"profile\": \"FHD PAL 1080p 25 fps\",",
              " \"duration\": {doc.durationSec},",
              " \"scale\": 15.0,",
              " \"tick_pixels\": 100,",
              " \"playhead_position\": 0,",
              " \"settings\": {},",
              " \"export_settings\": null,",
              " \"markers\": [],",
              " \"progress\": [],",
              " \"effects\": [],",
              " \"history\": { \"undo\": [], \"redo\": [] },",
              " \"layers\": [",
              "  { \"id\": \"L1\", \"label\": \"A1\", \"number\": 1000000, \"y\": 0, \"lock\": false },",
              "  { \"id\": \"L2\", \"label\": \"V1\", \"number\": 2000000, \"y\": 0, \"lock\": false },",
              "  { \"id\": \"L3\", \"label\": \"\", \"number\": 3000000, \"y\": 0, \"lock\": false },",
              "  { \"id\": \"L4\", \"label\": \"\", \"number\": 4000000, \"y\": 0, \"lock\": false },",
              "  { \"id\": \"L5\", \"label\": \"\", \"number\": 5000000, \"y\": 0, \"lock\": false }",
              " ],",
              " \"files\": [",
              "{clips}",
              " ],",
              " \"clips\": [",
              "{tracks}",
              " ],",
              " \"version\": { \"openshot-qt\": \"4.0.0\", \"libopenshot\": \"1.0.0\" }",
              "}",
              ""
            ]
          },

          "doc": {
            "ru": "plugins/editor.openshot.md",
            "en": "plugins/editor.openshot.md",
            "es": "plugins/editor.openshot.md",
            "pt": "plugins/editor.openshot.md",
            "zh-cn": "plugins/editor.openshot.md"
          }
        }
        """;

    /// <summary>
    /// ТРЕНЕР АДАПТЕРОВ LoRA — musubi-tuner (kohya-ss), T-156-S0. Первый плагин ветки, который
    /// ведёт НЕ к редактору, а к ДОЛГОЙ СЧИТАЮЩЕЙ ПРОГРАММЕ: работа идёт часами и оформляется
    /// отдельной задачей с исполнителем «авто ПО» (T-153-S0, T-154-S0), а не вызовом инструмента
    /// внутри хода агента. Отсюда три отличия от шлюзов ветки.
    ///
    /// <para>ПРОГРАММ У ПЛАГИНА ДВЕ (блок <c>software</c> массивом, T-146-S0), и они разной
    /// природы. ГЛАВНАЯ (первая) — <c>python</c>: именно её запускает
    /// <c>SoftwareConnector</c>, все три шага обучения это запуски питона. Годный диапазон
    /// 3.10–3.12 не придирчивость: на 3.13 musubi-tuner не собирается, а узналось бы это через
    /// несколько часов счёта (наука T-4-S0). ВТОРАЯ — сам <c>musubi-tuner</c>: это не
    /// исполняемый файл, а КАТАЛОГ СКРИПТОВ, искать его в PATH нечем, поэтому блока
    /// <c>system</c> у него нет вовсе — путь ставит установка модели (она этот пакет и качает)
    /// либо человек кнопкой «Обзор…». Плагин готов, только когда готовы ОБЕ программы
    /// (<c>ApiEndpoints.PluginProbe</c>).</para>
    ///
    /// <para>ОПЕРАЦИИ — БЛОК <c>run</c> (T-155-S0), А НЕ <c>convert</c>: у долгого запуска есть
    /// рабочий каталог, тайм-аут МОЛЧАНИЯ отдельно от общего предела (тренер молчит между
    /// эпохами по четверть часа) и правило разбора результата — имя файла адаптера заранее
    /// неизвестно, поэтому <c>newest</c> по маске. Операций три, ровно как шагов у musubi-tuner
    /// для Kandinsky 5 (docs/kandinsky5.md, наука T-289): кэш латентов → кэш выходов текстовых
    /// кодировщиков (ОБЯЗАТЕЛЕН) → само обучение. У обучения стоит <c>singleInstance</c>:
    /// видеокарта одна, и второй запуск не «идёт медленнее», а отказывает по памяти.</para>
    ///
    /// <para>РОЛИ У ДЕЙСТВИЙ НЕТ НАМЕРЕННО. Роль (<c>export</c>/<c>render</c>/<c>convert</c>)
    /// означает «агент может позвать это инструментом»; обучение агент инструментом не зовёт —
    /// он заводит ЗАДАЧУ, и её выполняет исполнитель «авто ПО». Запись справочника действий при
    /// этом всё равно заводится инициализацией (иначе правило безопасности не закрывает
    /// операцию вовсе, наука 2d3af8da), и правило вида «плагины и MCP» пишется как
    /// <c>trainer.musubi:lora.train</c>.</para>
    ///
    /// <para>ЗНАЧЕНИЯ ВЕСОВ И ЗАДАЧИ ТРЕНЕРА — НАСТРОЙКИ ЗАПИСИ, а не текст задания: пути DiT и
    /// VAE, имя задачи musubi (<c>k5-lite-t2v-5s-sd</c>), число шагов и размер сети. Постановщик
    /// задачи вправе назвать в описании только ОБЪЯВЛЕННЫЕ здесь ключи (<c>steps: 1200</c>) —
    /// это и есть граница безопасности ветки: произвольной командной строки нет ни у кого.</para>
    /// </summary>
    public const string TrainerMusubiJson = """
        {
          "code": "trainer.musubi",
          "kind": "gateway",
          "version": 2,
          "name": "Musubi Tuner (LoRA)",
          "description": {
            "ru": "Обучение адаптеров LoRA тренером musubi-tuner (kohya-ss): кэш латентов, кэш выходов текстовых кодировщиков и само обучение. Работа идёт часами и выполняется отдельной задачей исполнителем «авто ПО», а не инструментом агента. Программы плагина ставит установка модели, у которой объявлены пакеты обучения.",
            "en": "Trains LoRA adapters with musubi-tuner (kohya-ss): caching latents, caching text encoder outputs and the training itself. The work takes hours and runs as a separate task performed by the software executor, not as an agent tool. The programs are installed by the model whose profile declares training packages.",
            "es": "Entrena adaptadores LoRA con musubi-tuner (kohya-ss): cache de latentes, cache de salidas de los codificadores de texto y el entrenamiento. Dura horas y se ejecuta como tarea aparte con el ejecutor «software», no como herramienta del agente.",
            "pt": "Treina adaptadores LoRA com o musubi-tuner (kohya-ss): cache de latentes, cache das saidas dos codificadores de texto e o treinamento. Leva horas e roda como tarefa separada com o executor «software», nao como ferramenta do agente.",
            "zh-cn": "用 musubi-tuner（kohya-ss）训练 LoRA 适配器：缓存 latents、缓存文本编码器输出、正式训练。耗时数小时，由「自动软件」执行者以单独任务运行，而不是智能体工具。"
          },

          "software": [
            {
              "id": "python",
              "name": "Python 3.10-3.12",
              "package": "python",
              "required": true,
              "pathKey": "pythonPath",
              "system": {
                "commands": ["python", "python3", "python.exe"],
                "versionArgs": "--version",
                "minVersion": "3.10",
                "maxVersion": "3.12"
              },
              "hint": {
                "ru": "Нажмите «Установить» — сборка Python 3.12 скачается и распакуется сама; либо укажите путь к уже установленному Python 3.10-3.12 (на 3.13 musubi-tuner не собирается). Тот же пакет ставит установка модели Kandinsky.",
                "en": "Press Install to download a Python 3.12 build, or point to an already installed Python 3.10-3.12 (musubi-tuner does not build on 3.13). The same package is installed by the Kandinsky model install.",
                "es": "Pulse Instalar para descargar Python 3.12, o indique un Python 3.10-3.12 ya instalado (musubi-tuner no compila en 3.13).",
                "pt": "Clique em Instalar para baixar o Python 3.12, ou informe um Python 3.10-3.12 ja instalado (o musubi-tuner nao compila no 3.13).",
                "zh-cn": "点击「安装」自动下载 Python 3.12；也可以指定已安装的 Python 3.10-3.12（musubi-tuner 在 3.13 上装不上）。"
              }
            },
            {
              "id": "musubi-tuner",
              "name": "Musubi Tuner",
              "package": "musubi-tuner",
              "required": true,
              "pathKey": "musubiPath",
              "hint": {
                "ru": "Нажмите «Установить» — исходники musubi-tuner v0.3.4 скачаются и распакуются сами, а следом установка сделает окружение с torch (подкаталог .venv) и поставит зависимости тренера: обучение больше ничего не доставляет молча. Либо укажите каталог, куда вы положили тренер сами (в нём лежит kandinsky5_train_network.py). В PATH этот пакет не ищется: это каталог скриптов, а не программа.",
                "en": "Press Install: the musubi-tuner v0.3.4 sources are downloaded and unpacked, and right after that the install builds the torch environment (the .venv subfolder) and installs the trainer requirements — training no longer downloads anything silently. Or point at the folder you unpacked them into (it holds kandinsky5_train_network.py). This package is not looked up in PATH: it is a script folder, not a program.",
                "es": "Pulse Instalar: se descarga musubi-tuner v0.3.4 y, a continuacion, la instalacion crea el entorno con torch (.venv) e instala las dependencias del entrenador. O indique la carpeta donde lo descomprimio (contiene kandinsky5_train_network.py).",
                "pt": "Clique em Instalar: o musubi-tuner v0.3.4 e baixado e, em seguida, a instalacao cria o ambiente com torch (.venv) e instala as dependencias do treinador. Ou informe a pasta onde voce o descompactou (contem kandinsky5_train_network.py).",
                "zh-cn": "点击「安装」：先下载并解压 musubi-tuner v0.3.4 源码，随后安装过程会创建带 torch 的环境（.venv 子目录）并装好训练器依赖，训练时不再偷偷下载任何东西。也可以指定你自己解压到的目录（其中有 kandinsky5_train_network.py）。"
              }
            }
          ],

          "actions": [
            {
              "code": "AI2P.Plugins.Musubi.CacheLatents",
              "tool": "musubi_cache_latents",
              "op": "lora.cacheLatents",
              "needsSoftware": true,
              "title": {
                "ru": "Кэш латентов датасета",
                "en": "Cache the dataset latents",
                "es": "Cachear los latentes del dataset",
                "pt": "Cachear os latentes do dataset",
                "zh-cn": "缓存数据集 latents"
              },
              "description": {
                "ru": "Первый шаг обучения: прогнать кадры датасета через VAE и сложить латенты в кэш. Датасет описан файлом dataset.toml, путь к нему приходит параметром in.",
                "en": "The first training step: run the dataset frames through the VAE and store the latents in the cache. The dataset is described by dataset.toml, whose path comes as the in parameter.",
                "es": "Primer paso: pasar los cuadros del dataset por el VAE y guardar los latentes en la cache.",
                "pt": "Primeiro passo: passar os quadros do dataset pelo VAE e guardar os latentes no cache.",
                "zh-cn": "训练第一步：把数据集画面过一遍 VAE，把 latents 存入缓存。"
              }
            },
            {
              "code": "AI2P.Plugins.Musubi.CacheTextEncoders",
              "tool": "musubi_cache_text_encoders",
              "op": "lora.cacheText",
              "needsSoftware": true,
              "title": {
                "ru": "Кэш выходов текстовых кодировщиков",
                "en": "Cache the text encoder outputs",
                "es": "Cachear las salidas de los codificadores de texto",
                "pt": "Cachear as saidas dos codificadores de texto",
                "zh-cn": "缓存文本编码器输出"
              },
              "description": {
                "ru": "Второй шаг обучения, обязательный: посчитать выходы текстовых кодировщиков (Qwen2.5-VL и CLIP) по подписям датасета. Без него обучение не стартует.",
                "en": "The second, mandatory training step: compute the text encoder outputs (Qwen2.5-VL and CLIP) over the dataset captions. Training does not start without it.",
                "es": "Segundo paso, obligatorio: calcular las salidas de los codificadores de texto (Qwen2.5-VL y CLIP) sobre las descripciones del dataset.",
                "pt": "Segundo passo, obrigatorio: calcular as saidas dos codificadores de texto (Qwen2.5-VL e CLIP) sobre as legendas do dataset.",
                "zh-cn": "训练第二步（必需）：按数据集字幕计算文本编码器（Qwen2.5-VL 与 CLIP）的输出。"
              }
            },
            {
              "code": "AI2P.Plugins.Musubi.LoraTrain",
              "tool": "musubi_lora_train",
              "op": "lora.train",
              "needsSoftware": true,
              "singleInstance": true,
              "title": {
                "ru": "Обучение LoRA",
                "en": "LoRA training",
                "es": "Entrenamiento LoRA",
                "pt": "Treinamento LoRA",
                "zh-cn": "LoRA 训练"
              },
              "description": {
                "ru": "Третий шаг: само обучение адаптера (accelerate launch kandinsky5_train_network.py). Идёт часами и занимает видеокарту целиком, поэтому двух таких работ разом система не запускает — вторая задача ЖДЁТ. Веса, имя задачи тренера, число шагов и размер сети берутся из настроек записи плагина; результат — самый свежий файл .safetensors рабочего каталога.",
                "en": "The third step: the adapter training itself (accelerate launch kandinsky5_train_network.py). It runs for hours and takes the whole GPU, so two such runs never start at once — the second task WAITS. Weights, trainer task name, step count and network size come from the plugin record settings; the result is the newest .safetensors file of the working folder.",
                "es": "Tercer paso: el entrenamiento del adaptador. Dura horas y ocupa toda la GPU, por eso nunca se lanzan dos a la vez: la segunda tarea ESPERA.",
                "pt": "Terceiro passo: o treinamento do adaptador. Leva horas e ocupa a GPU inteira, por isso nunca se iniciam dois ao mesmo tempo: a segunda tarefa ESPERA.",
                "zh-cn": "第三步：适配器训练本身。耗时数小时并独占显卡，因此系统不会同时启动两个——第二个任务会等待。"
              }
            }
          ],

          "settings": [
            { "key": "scripts", "type": "text", "default": "",
              "title": { "ru": "Каталог скриптов musubi-tuner", "en": "musubi-tuner scripts folder", "es": "Carpeta de scripts de musubi-tuner", "pt": "Pasta de scripts do musubi-tuner", "zh-cn": "musubi-tuner 脚本目录" } },
            { "key": "dit", "type": "text", "default": "",
              "title": { "ru": "Файл весов DiT", "en": "DiT weights file", "es": "Archivo de pesos DiT", "pt": "Arquivo de pesos DiT", "zh-cn": "DiT 权重文件" } },
            { "key": "vae", "type": "text", "default": "",
              "title": { "ru": "Файл весов VAE", "en": "VAE weights file", "es": "Archivo de pesos VAE", "pt": "Arquivo de pesos VAE", "zh-cn": "VAE 权重文件" } },
            { "key": "task", "type": "choice", "default": "k5-lite-t2v-5s-sd", "choices": ["k5-lite-t2v-5s-sd", "k5-lite-i2v-5s-sd"],
              "title": { "ru": "Задача тренера", "en": "Trainer task", "es": "Tarea del entrenador", "pt": "Tarefa do treinador", "zh-cn": "训练任务" } },
            { "key": "textEncoderQwen", "type": "text", "default": "Qwen/Qwen2.5-VL-7B-Instruct",
              "title": { "ru": "Текстовый кодировщик Qwen", "en": "Qwen text encoder", "es": "Codificador de texto Qwen", "pt": "Codificador de texto Qwen", "zh-cn": "Qwen 文本编码器" } },
            { "key": "textEncoderClip", "type": "text", "default": "openai/clip-vit-large-patch14",
              "title": { "ru": "Текстовый кодировщик CLIP", "en": "CLIP text encoder", "es": "Codificador de texto CLIP", "pt": "Codificador de texto CLIP", "zh-cn": "CLIP 文本编码器" } },
            { "key": "steps", "type": "number", "default": 2000,
              "title": { "ru": "Шагов обучения", "en": "Training steps", "es": "Pasos de entrenamiento", "pt": "Passos de treinamento", "zh-cn": "训练步数" } },
            { "key": "networkDim", "type": "number", "default": 32,
              "title": { "ru": "Размер сети LoRA", "en": "LoRA network dim", "es": "Dimension de la red LoRA", "pt": "Dimensao da rede LoRA", "zh-cn": "LoRA 网络维度" } },
            { "key": "networkAlpha", "type": "number", "default": 32,
              "title": { "ru": "Alpha сети LoRA", "en": "LoRA network alpha", "es": "Alpha de la red LoRA", "pt": "Alpha da rede LoRA", "zh-cn": "LoRA 网络 alpha" } },
            { "key": "learningRate", "type": "text", "default": "1e-4",
              "title": { "ru": "Скорость обучения", "en": "Learning rate", "es": "Tasa de aprendizaje", "pt": "Taxa de aprendizado", "zh-cn": "学习率" } },
            { "key": "mixedPrecision", "type": "choice", "default": "bf16", "choices": ["bf16", "fp16", "no"],
              "title": { "ru": "Точность вычислений", "en": "Mixed precision", "es": "Precision mixta", "pt": "Precisao mista", "zh-cn": "混合精度" } },
            { "key": "outputName", "type": "text", "default": "lora",
              "title": { "ru": "Имя файла адаптера", "en": "Adapter file name", "es": "Nombre del archivo del adaptador", "pt": "Nome do arquivo do adaptador", "zh-cn": "适配器文件名" } }
          ],

          "run": {
            "idleTimeoutSec": 900,
            "timeoutSec": 86400,
            "ops": [
              {
                "op": "lora.cacheLatents",
                "outExt": ".log",
                "args": ["{scripts}/kandinsky5_cache_latents.py",
                         "--dataset_config", "{in}",
                         "--vae", "{vae}"]
              },
              {
                "op": "lora.cacheText",
                "outExt": ".log",
                "args": ["{scripts}/kandinsky5_cache_text_encoder_outputs.py",
                         "--dataset_config", "{in}",
                         "--text_encoder_qwen", "{textEncoderQwen}",
                         "--text_encoder_clip", "{textEncoderClip}",
                         "--batch_size", "1"]
              },
              {
                "op": "lora.train",
                "singleInstance": true,
                "outName": "lora",
                "outExt": ".safetensors",
                "result": { "kind": "newest", "dir": "", "pattern": "{outputName}*.safetensors" },
                "args": ["-m", "accelerate.commands.launch",
                         "--num_cpu_threads_per_process", "1",
                         "--mixed_precision", "{mixedPrecision}",
                         "{scripts}/kandinsky5_train_network.py",
                         "--mixed_precision", "{mixedPrecision}",
                         "--dataset_config", "{in}",
                         "--task", "{task}",
                         "--dit", "{dit}",
                         "--vae", "{vae}",
                         "--text_encoder_qwen", "{textEncoderQwen}",
                         "--text_encoder_clip", "{textEncoderClip}",
                         "--sdpa", "--fp8_base", "--gradient_checkpointing",
                         "--max_data_loader_n_workers", "1", "--persistent_data_loader_workers",
                         "--learning_rate", "{learningRate}",
                         "--optimizer_type", "AdamW8Bit",
                         "--max_grad_norm", "1.0",
                         "--lr_scheduler", "constant_with_warmup", "--lr_warmup_steps", "100",
                         "--network_module", "networks.lora_kandinsky",
                         "--network_dim", "{networkDim}", "--network_alpha", "{networkAlpha}",
                         "--timestep_sampling", "shift", "--discrete_flow_shift", "5.0",
                         "--scheduler_scale", "10.0",
                         "--max_train_steps", "{steps}",
                         "--output_dir", "{work}", "--output_name", "{outputName}",
                         "--seed", "42"]
              }
            ]
          }
        }
        """;
}
