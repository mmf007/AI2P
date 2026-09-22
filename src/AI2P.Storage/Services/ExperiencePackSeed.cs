using AI2P.Core;

namespace AI2P.Storage.Services;

/// <summary>
/// НАБОРЫ ОПЫТА ДИСТРИБУТИВА (T-270-S0) — файлы <c>packs/&lt;код&gt;/pack.json</c> каталога
/// данных, ровно по образцу манифестов плагинов (<see cref="PluginSeed"/>) и справочника
/// пакетов (<c>AiModelService.WriteSeedFile</c>).
///
/// <para>ПОЧЕМУ ТАК, А НЕ СТРОКОЙ В buildRelease.ps1/.sh: у <c>plugins/</c> и <c>models/</c>
/// в скриптах выкладки нет НИ ОДНОЙ строки — их файлы кладёт сама программа при открытии
/// организации, и в выкладку, в установку и в пакет установщика они попадают вместе с
/// исполняемым файлом. Так же сделано и здесь: набор дистрибутива — это встроенный текст,
/// который разворачивается в каталог данных на любом сервере, включая реплику.</para>
///
/// <para>ПЕРЕЗАПИСЫВАЕТСЯ ПО ВЕРСИИ НАБОРА: файл на диске старее встроенного — кладём новый.
/// Правка руками переживает обновление ровно до следующего подъёма версии, как у манифеста
/// плагина.</para>
///
/// <para>СТАВИТЬ НАБОР САМ ПО СЕБЕ СИД НЕ ДОЛЖЕН (решение заказчика по T-270-S0): файл
/// появляется в каталоге, а записи в опыт кладёт человек кнопкой «Установить», выбрав
/// область. Иначе поставляемый опыт начал бы приходить всем задачам молча.</para>
///
/// <para>ТЕКСТЫ СТИЛЕЙ написаны подзадачей ветки «Опыт: 2–3 стиля работы для дистрибутива»
/// (T-272-S0): «Строгое код-ревью» (9 записей), «Разработка через тесты» (10) и
/// «Исследование и отчёт» (10). Четвёртый набор — «Анализ опыта» (T-271-S0) — это не стиль
/// работы, а шаблон задач.</para>
///
/// <para>ЧЕГО В ЗАПИСЯХ СТИЛЯ БЫТЬ НЕ ДОЛЖНО: названия нашего продукта, путей наших файлов,
/// кодов наших задач и наших внутренних договорённостей. Набор ставится в ЛЮБУЮ организацию,
/// а проектное правило, приехавшее чужому, — это мусор, который некому опознать (правило
/// разграничения областей, T-269-S0).</para>
/// </summary>
public static class ExperiencePackSeed
{
    /// <summary>Разложить наборы дистрибутива по каталогу данных организации.</summary>
    public static void Write(FileStore files)
    {
        foreach (var json in All)
        {
            var pack = ExperiencePack.Parse(json);
            if (pack is null)
            {
                continue;   // встроенный набор негоден — это дефект сборки, а не данных
            }
            var rel = ExperiencePack.PathOf(pack.Code);
            var abs = files.Abs(rel);
            if (File.Exists(abs) && VersionOf(abs) >= pack.Version)
            {
                continue;
            }
            files.WriteText(rel, json);
        }
    }

    /// <summary>Версия набора, лежащего на диске; 0 — файл нечитаем или это не набор.</summary>
    private static int VersionOf(string abs)
    {
        try
        {
            return ExperiencePack.Parse(File.ReadAllText(abs))?.Version ?? 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Встроенные наборы. Порядок безразличен — файлы независимы.</summary>
    public static IReadOnlyList<string> All =>
        [StrictReviewJson, TddJson, ResearchReportJson, ExperienceAnalysisJson];

    /// <summary>Код набора «Разработка через тесты» (T-272-S0).</summary>
    public const string TddCode = "style.tdd";

    /// <summary>Код набора «Исследование и отчёт» (T-272-S0).</summary>
    public const string ResearchReportCode = "style.research-report";

    /// <summary>Код набора с шаблоном «Анализ опыта» (T-271-S0).</summary>
    public const string AnalysisCode = "style.experience-analysis";

    /// <summary>
    /// НАБОР «АНАЛИЗ ОПЫТА» (T-271-S0) — тот самый шаблон, который заказчик просил
    /// поставлять ВМЕСТЕ СО СТИЛЯМИ: периодический разбор накопленного опыта (обобщить,
    /// разделить, разметить, перенести, погасить).
    ///
    /// <para>УЗЛОВ ТРИ (решение заказчика по вопросу 3): корень с общим порядком работы и
    /// два потомка — «общий опыт организации» и «опыт проекта» (его копируют на каждый
    /// проект). Потомки получают текст корня в задании — <c>ParentInPrompt</c> проставляет
    /// установка набора, поэтому порядок работы написан ОДИН РАЗ.</para>
    ///
    /// <para>РАСПИСАНИЯ НАБОР НЕ СТАВИТ (решение заказчика по вопросу 2): расписание
    /// запускает задачи и тратит деньги на модель — его заводит человек сам, узел годится
    /// в <c>Schedule.TemplateTaskId</c> как любой другой без единой правки кода.</para>
    ///
    /// <para>ПРАВИЛО ДЕАКТИВАЦИИ ПО СТАТИСТИКЕ — ПРОСТО ТЕКСТ В ЗАДАНИИ УЗЛА (решение
    /// заказчика по вопросу 1): считать его кодом никто не просил, и порог правится
    /// человеком в шаблоне, а не в исходниках.</para>
    /// </summary>
    public const string ExperienceAnalysisJson = """
        {
          "code": "style.experience-analysis",
          "version": 1,
          "name": {
            "ru": "Анализ опыта",
            "en": "Experience analysis",
            "es": "Análisis de la experiencia",
            "pt": "Análise da experiência",
            "zh-cn": "经验分析"
          },
          "description": {
            "ru": "Шаблон периодического разбора накопленного опыта: обобщить, разделить, разметить, перенести, погасить устаревшее.",
            "en": "A template for the periodic review of accumulated experience: merge, split, tag, move, switch off the stale.",
            "es": "Plantilla para la revisión periódica de la experiencia acumulada: fundir, dividir, etiquetar, mover y desactivar lo obsoleto.",
            "pt": "Modelo para a revisão periódica da experiência acumulada: fundir, dividir, etiquetar, mover e desligar o obsoleto.",
            "zh-cn": "定期梳理已积累经验的模板：归并、拆分、打标签、迁移、停用过时条目。"
          },
          "doc": {
            "ru": "packs/style.experience-analysis.md",
            "en": "packs/style.experience-analysis.md",
            "es": "packs/style.experience-analysis.md",
            "pt": "packs/style.experience-analysis.md",
            "zh-cn": "packs/style.experience-analysis.md"
          },
          "records": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0003",
              "skill": "",
              "tags": ["опыт"],
              "alwaysLoad": false,
              "text": {
                "ru": "Опыт не удаляют, а гасят: ставшая ненужной запись помечается неактивной — её видно в списке, её можно прочитать и вернуть, а дальше её уносит правило архивации. Удалённую запись не восстанавливает ничто, поэтому удаление — решение человека, а не вывод разбора.",
                "en": "Experience is not deleted, it is switched off: a record that is no longer needed is marked inactive — it stays visible, can be read and brought back, and the archiving rule takes it away later. A deleted record is restored by nothing, so deletion is a human decision, not a conclusion of the review.",
                "es": "La experiencia no se borra, se desactiva: un registro que ya no hace falta se marca como inactivo — sigue visible, se puede leer y reactivar, y luego la regla de archivado se lo lleva. Un registro borrado no lo recupera nada, así que borrar es decisión de la persona, no conclusión de la revisión.",
                "pt": "A experiência não se apaga, desliga-se: um registro que deixou de ser necessário é marcado como inativo — continua visível, pode ser lido e reativado, e depois a regra de arquivamento leva-o embora. Um registro apagado não é recuperado por nada, por isso apagar é decisão da pessoa, não conclusão da revisão.",
                "zh-cn": "经验不删除，只停用：不再需要的条目标记为停用——它仍在列表中可读、可恢复，之后由归档规则带走。被删除的条目无法由任何机制恢复，因此删除是人的决定，而不是梳理的结论。"
              }
            }
          ],
          "templates": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p2001",
              "parent": null,
              "skills": ["analyze-data"],
              "title": {
                "ru": "Анализ опыта",
                "en": "Experience analysis",
                "es": "Análisis de la experiencia",
                "pt": "Análise da experiência",
                "zh-cn": "经验分析"
              },
              "description": {
                "ru": "Периодический разбор накопленного опыта. Запускается по расписанию — расписание заводит человек сам (Настройки → Расписания, копия этого узла раз в неделю или раз в месяц).\n\n## Что прочитать\n1. `experience_usage` по своей области — сколько ЗАДАЧ получило каждую запись и когда это было в последний раз; строки идут от самых невостребованных.\n2. `list_experience` по той же области СТРАНИЦАМИ (limit/offset) — весь корпус подряд, вместе с тэгами и навыками.\n3. `search_experience` — когда надо проверить, нет ли у записи двойника в другой области.\n\n## Что сделать\n* **Обобщить.** Несколько записей об одном и том же — завести ОДНУ сводную (`create_experience`), а исходные ПОГАСИТЬ (`set_experience_active`, active=false). В сводной назвать идентификаторы исходных.\n* **Разделить.** Запись, в которой смешаны два разных урока, — сделать из неё две отдельные, исходную погасить.\n* **Разметить.** `update_experience`: проставить тэги (тема работы) и навык (кому эта запись нужна). Передавай ТОЛЬКО те поля, которые меняешь, вместе с полным текстом; не переданное поле не меняется.\n* **Перенести.** `move_experience`: общее правило «как мы работаем» — в general; всё, что называет продукт, файл или код задачи, — в опыт проекта; всё про один шаг процесса — в узел шаблона.\n* **Погасить по статистике.** Правило: запись гасится, если она не уходила НИ В ОДНО задание 3 месяца ПРИ ТОМ, что задания по её теме за это время были (тема — это тэги записи). Не уходила потому, что таких задач просто не было, — НЕ ГАСИТЬ. У записи, заведённой недавно и не имеющей статистики вовсе, срок считается от даты создания.\n\n## Чего не делать\n* **Не удалять** записи — только гасить: удаления у агента нет, а погашенную запись человек может прочитать и вернуть.\n* **Не трогать чужие** записи (созданные другим сервером): их правит сервер-владелец, отказ на такую запись — норма, а не ошибка.\n* **Не гасить поставляемые правила** дистрибутива с фиксированными идентификаторами: это правила, по которым работает сам агент.\n* **Не переписывать смысл**: обобщение обязано сохранить все факты исходных записей, иначе это не обобщение, а потеря опыта.\n\n## Что написать в отчёте\nСписком: что обобщено (какие идентификаторы во что), что разделено, что погашено и по какой причине, какие тэги и навыки проставлены, что перенесено между областями. Числами: сколько записей было и сколько осталось активными. Отдельно — на что не хватило данных.",
                "en": "The periodic review of accumulated experience. Runs on a schedule — the schedule is set up by a human (Settings → Schedules, a copy of this node once a week or once a month).\n\n## What to read\n1. `experience_usage` for your scope — how many TASKS received each record and when that last happened; rows go from the least used.\n2. `list_experience` for the same scope, PAGE BY PAGE (limit/offset) — the whole corpus in a row, with tags and skills.\n3. `search_experience` — when you need to check whether a record has a twin in another scope.\n\n## What to do\n* **Merge.** Several records about the same thing — create ONE summary record (`create_experience`) and switch the originals OFF (`set_experience_active`, active=false). Name the original ids in the summary.\n* **Split.** A record mixing two different lessons — make two separate records out of it and switch the original off.\n* **Tag.** `update_experience`: set tags (the topic) and the skill (who needs this record). Pass ONLY the fields you change, together with the full text; a field you do not pass is not changed.\n* **Move.** `move_experience`: a general rule about how we work — to general; anything naming the product, a file or a task code — to the project experience; anything about one step of the process — to a template node.\n* **Switch off by statistics.** The rule: a record is switched off if it went into NO job for 3 months WHILE jobs on its topic did happen in that time (the topic is the record's tags). If it was not used simply because there were no such tasks — DO NOT switch it off. For a record created recently and having no statistics at all, count the period from its creation date.\n\n## What not to do\n* **Do not delete** records — only switch them off: the agent has no deletion, and a switched-off record can be read and brought back by a human.\n* **Do not touch records owned by another server**: they are edited by their owner, and a refusal on such a record is normal, not an error.\n* **Do not switch off the rules shipped with the distribution** (fixed ids): those are the rules the agent itself works by.\n* **Do not rewrite the meaning**: a merge must keep every fact of the original records, otherwise it is not a merge but a loss of experience.\n\n## What to write in the report\nAs a list: what was merged (which ids into what), what was split, what was switched off and why, which tags and skills were set, what was moved between scopes. As numbers: how many records there were and how many stayed active. Separately — what there was not enough data for.",
                "es": "Revisión periódica de la experiencia acumulada. Se lanza por calendario — el calendario lo crea la persona (Configuración → Calendarios, una copia de este nodo una vez por semana o por mes).\n\n## Qué leer\n1. `experience_usage` de tu ámbito: cuántas TAREAS recibieron cada registro y cuándo fue la última vez; las filas van de las menos usadas.\n2. `list_experience` del mismo ámbito POR PÁGINAS (limit/offset): todo el corpus seguido, con etiquetas y habilidades.\n3. `search_experience`: cuando haya que comprobar si un registro tiene un gemelo en otro ámbito.\n\n## Qué hacer\n* **Fundir.** Varios registros sobre lo mismo: crear UNO resumido (`create_experience`) y DESACTIVAR los originales (`set_experience_active`, active=false). Nombrar en el resumen los identificadores originales.\n* **Dividir.** Un registro que mezcla dos lecciones distintas: hacer dos registros separados y desactivar el original.\n* **Etiquetar.** `update_experience`: poner etiquetas (el tema) y la habilidad (a quién le hace falta). Pasa SOLO los campos que cambias, junto con el texto completo; un campo que no pasas no cambia.\n* **Mover.** `move_experience`: la regla general de cómo trabajamos, a general; todo lo que nombra el producto, un archivo o un código de tarea, a la experiencia del proyecto; todo lo referido a un paso del proceso, a un nodo de plantilla.\n* **Desactivar por estadística.** La regla: un registro se desactiva si no entró en NINGÚN trabajo durante 3 meses AUNQUE hubo trabajos sobre su tema en ese período (el tema son sus etiquetas). Si no se usó simplemente porque no hubo tareas así, NO desactivar. Para un registro creado hace poco y sin estadística alguna, el plazo se cuenta desde su fecha de creación.\n\n## Qué no hacer\n* **No borrar** registros, solo desactivarlos: el agente no tiene borrado y un registro desactivado puede leerse y reactivarse.\n* **No tocar registros de otro servidor**: los edita su dueño, y un rechazo sobre ellos es normal, no un error.\n* **No desactivar las reglas de la distribución** (identificadores fijos): son las reglas por las que trabaja el propio agente.\n* **No reescribir el sentido**: una fusión debe conservar todos los hechos de los originales; si no, no es fusión sino pérdida de experiencia.\n\n## Qué escribir en el informe\nEn lista: qué se fundió (qué identificadores en qué), qué se dividió, qué se desactivó y por qué, qué etiquetas y habilidades se pusieron, qué se movió entre ámbitos. En números: cuántos registros había y cuántos quedaron activos. Aparte: para qué faltaron datos.",
                "pt": "Revisão periódica da experiência acumulada. Roda por agendamento — o agendamento é criado pela pessoa (Configurações → Agendamentos, uma cópia deste nó uma vez por semana ou por mês).\n\n## O que ler\n1. `experience_usage` do seu escopo: quantas TAREFAS receberam cada registro e quando foi a última vez; as linhas vão das menos usadas.\n2. `list_experience` do mesmo escopo POR PÁGINAS (limit/offset): todo o corpus em sequência, com etiquetas e habilidades.\n3. `search_experience`: quando for preciso verificar se um registro tem um gêmeo em outro escopo.\n\n## O que fazer\n* **Fundir.** Vários registros sobre a mesma coisa: criar UM resumido (`create_experience`) e DESLIGAR os originais (`set_experience_active`, active=false). Citar no resumo os identificadores originais.\n* **Dividir.** Um registro que mistura duas lições diferentes: fazer dois registros separados e desligar o original.\n* **Etiquetar.** `update_experience`: pôr etiquetas (o tema) e a habilidade (a quem serve). Envie SOMENTE os campos que muda, junto com o texto completo; um campo não enviado não muda.\n* **Mover.** `move_experience`: a regra geral sobre como trabalhamos, para general; tudo o que nomeia o produto, um arquivo ou um código de tarefa, para a experiência do projeto; tudo sobre um passo do processo, para um nó de modelo.\n* **Desligar por estatística.** A regra: um registro é desligado se não entrou em NENHUM trabalho por 3 meses EMBORA tenham existido trabalhos sobre o seu tema nesse período (o tema são as suas etiquetas). Se não foi usado apenas porque não houve tais tarefas, NÃO desligar. Para um registro criado há pouco e sem estatística alguma, o prazo conta desde a data de criação.\n\n## O que não fazer\n* **Não apagar** registros, apenas desligá-los: o agente não tem apagamento, e um registro desligado pode ser lido e reativado.\n* **Não mexer em registros de outro servidor**: são editados pelo dono, e uma recusa neles é normal, não um erro.\n* **Não desligar as regras da distribuição** (identificadores fixos): são as regras pelas quais o próprio agente trabalha.\n* **Não reescrever o sentido**: uma fusão deve preservar todos os fatos dos originais; caso contrário não é fusão, é perda de experiência.\n\n## O que escrever no relatório\nEm lista: o que foi fundido (quais identificadores em quê), o que foi dividido, o que foi desligado e por quê, quais etiquetas e habilidades foram postas, o que foi movido entre escopos. Em números: quantos registros havia e quantos ficaram ativos. À parte: para o que faltaram dados.",
                "zh-cn": "定期梳理已积累的经验。由计划任务触发——计划由人自行设置（设置 → 计划，按周或按月复制本节点）。\n\n## 需要读什么\n1. 对本范围执行 `experience_usage`：每条经验被多少个任务收到过、最近一次是什么时候；行按从最少使用开始排列。\n2. 对同一范围分页执行 `list_experience`（limit/offset）：连续读取整批语料，含标签与技能。\n3. `search_experience`：当需要确认某条经验在别的范围是否有孪生条目时使用。\n\n## 要做什么\n* **归并。** 若干条讲同一件事的条目——新建一条汇总条目（`create_experience`），并把原条目停用（`set_experience_active`，active=false）。在汇总条目中写明原条目的标识。\n* **拆分。** 混杂了两个不同教训的条目——拆成两条独立条目，原条目停用。\n* **打标签。** `update_experience`：补充标签（工作主题）和技能（谁需要这条）。只传你要修改的字段，并附上完整文本；未传入的字段不会改动。\n* **迁移。** `move_experience`：关于“我们怎么工作”的通用规则放入 general；凡是点名产品、文件或任务代码的放入项目经验；只讲流程中某一步的放入模板节点。\n* **按统计停用。** 规则：若某条经验连续 3 个月没有进入任何任务，而同期确实有与其主题相关的任务（主题即其标签），则停用；如果只是因为根本没有这类任务而未被使用，则不要停用。对于新建不久、完全没有统计的条目，期限从创建日期算起。\n\n## 不要做什么\n* **不要删除**条目，只停用：代理没有删除能力，而停用的条目人仍可阅读并恢复。\n* **不要触碰他人服务器的条目**：它们由所属服务器修改，被拒绝属于正常而非故障。\n* **不要停用发行版自带规则**（标识固定）：那是代理自身遵循的规则。\n* **不要改写原意**：归并必须保留原条目的全部事实，否则不是归并而是经验流失。\n\n## 报告里写什么\n列表：归并了什么（哪些标识并入何处）、拆分了什么、停用了什么及原因、补了哪些标签与技能、在范围之间迁移了什么。数字：原有多少条、剩下多少条处于启用状态。另外单列：哪些判断因数据不足而未做。"
              },
              "acceptance": {
                "ru": "Каждое действие названо идентификаторами записей; ни одна запись не удалена (только погашена); в отчёте есть числа «было / осталось активными».",
                "en": "Every action names the record ids; no record is deleted (only switched off); the report gives the numbers «were / left active».",
                "es": "Cada acción indica los identificadores de los registros; ningún registro se borra (solo se desactiva); el informe da los números «había / quedan activos».",
                "pt": "Cada ação indica os identificadores dos registros; nenhum registro é apagado (apenas desligado); o relatório traz os números «havia / restam ativos».",
                "zh-cn": "每项操作都写明条目标识；没有任何条目被删除（只被停用）；报告给出“原有／剩余启用”的数字。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p2002",
              "parent": "b1e5a0c0-7d1a-4c31-9f01-0000000p2001",
              "skills": ["analyze-data"],
              "title": {
                "ru": "Анализ общего опыта организации",
                "en": "Review of the organisation's general experience",
                "es": "Revisión de la experiencia general de la organización",
                "pt": "Revisão da experiência geral da organização",
                "zh-cn": "梳理组织的通用经验"
              },
              "description": {
                "ru": "Разобрать ОБЩИЕ ПРАВИЛА РАБОТЫ организации: `list_experience` scope=general и `experience_usage` scope=general. Порядок работы, запреты и правило деактивации — в задании узла-родителя «Анализ опыта».\n\nОсобое внимание здесь: в общих правилах не должно быть записей про конкретный продукт, файл или код задачи — такие переносить в опыт проекта (`move_experience` scope=project). Поставляемые правила дистрибутива не гасить и не переносить.",
                "en": "Review the organisation's GENERAL WORKING RULES: `list_experience` scope=general and `experience_usage` scope=general. The order of work, the prohibitions and the switch-off rule are in the job of the parent node «Experience analysis».\n\nWhat matters here: the general rules must contain no records about a particular product, file or task code — move those to the project experience (`move_experience` scope=project). Do not switch off or move the rules shipped with the distribution.",
                "es": "Revisar las REGLAS GENERALES DE TRABAJO de la organización: `list_experience` scope=general y `experience_usage` scope=general. El orden de trabajo, las prohibiciones y la regla de desactivación están en el trabajo del nodo padre «Análisis de la experiencia».\n\nLo importante aquí: en las reglas generales no debe haber registros sobre un producto, archivo o código de tarea concretos — muévelos a la experiencia del proyecto (`move_experience` scope=project). No desactives ni muevas las reglas de la distribución.",
                "pt": "Revisar as REGRAS GERAIS DE TRABALHO da organização: `list_experience` scope=general e `experience_usage` scope=general. A ordem do trabalho, as proibições e a regra de desligamento estão no trabalho do nó pai «Análise da experiência».\n\nO que importa aqui: nas regras gerais não deve haver registros sobre um produto, arquivo ou código de tarefa concretos — mova-os para a experiência do projeto (`move_experience` scope=project). Não desligue nem mova as regras da distribuição.",
                "zh-cn": "梳理组织的通用工作规则：`list_experience` scope=general 与 `experience_usage` scope=general。工作步骤、禁忌以及停用规则见父节点“经验分析”的任务文本。\n\n此处重点：通用规则中不应出现涉及具体产品、文件或任务代码的条目——把它们迁到项目经验（`move_experience` scope=project）。发行版自带规则不得停用、不得迁移。"
              },
              "acceptance": {
                "ru": "Общие правила разобраны целиком; проектные записи из них перенесены; поставляемые правила не тронуты.",
                "en": "The general rules are reviewed in full; project-specific records are moved out; the shipped rules are untouched.",
                "es": "Las reglas generales se revisan por completo; los registros propios de un proyecto se han movido; las reglas de la distribución quedan intactas.",
                "pt": "As regras gerais são revisadas por inteiro; os registros próprios de um projeto foram movidos; as regras da distribuição ficam intactas.",
                "zh-cn": "通用规则被完整梳理；属于项目的条目已迁出；自带规则未被改动。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p2003",
              "parent": "b1e5a0c0-7d1a-4c31-9f01-0000000p2001",
              "skills": ["analyze-data"],
              "title": {
                "ru": "Анализ опыта проекта",
                "en": "Review of the project experience",
                "es": "Revisión de la experiencia del proyecto",
                "pt": "Revisão da experiência do projeto",
                "zh-cn": "梳理项目经验"
              },
              "description": {
                "ru": "Разобрать опыт ОДНОГО проекта: `list_experience` scope=project и `experience_usage` scope=project. Порядок работы, запреты и правило деактивации — в задании узла-родителя «Анализ опыта». На каждый проект заводится своя копия этой подзадачи.\n\nОсобое внимание здесь: запись, верная в ЛЮБОМ проекте организации (про процесс, отчётность, дисциплину проверок), переносится в общие правила (`move_experience` scope=general); запись про один шаг процесса — в узел шаблона (scope=template).",
                "en": "Review the experience of ONE project: `list_experience` scope=project and `experience_usage` scope=project. The order of work, the prohibitions and the switch-off rule are in the job of the parent node «Experience analysis». Each project gets its own copy of this subtask.\n\nWhat matters here: a record that holds in ANY project of the organisation (about the process, reporting, test discipline) moves to the general rules (`move_experience` scope=general); a record about one step of the process moves to a template node (scope=template).",
                "es": "Revisar la experiencia de UN proyecto: `list_experience` scope=project y `experience_usage` scope=project. El orden de trabajo, las prohibiciones y la regla de desactivación están en el trabajo del nodo padre «Análisis de la experiencia». Cada proyecto recibe su propia copia de esta subtarea.\n\nLo importante aquí: un registro válido en CUALQUIER proyecto de la organización (sobre el proceso, los informes, la disciplina de pruebas) pasa a las reglas generales (`move_experience` scope=general); un registro sobre un paso del proceso pasa a un nodo de plantilla (scope=template).",
                "pt": "Revisar a experiência de UM projeto: `list_experience` scope=project e `experience_usage` scope=project. A ordem do trabalho, as proibições e a regra de desligamento estão no trabalho do nó pai «Análise da experiência». Cada projeto recebe a sua própria cópia desta subtarefa.\n\nO que importa aqui: um registro válido em QUALQUER projeto da organização (sobre o processo, relatórios, disciplina de testes) passa para as regras gerais (`move_experience` scope=general); um registro sobre um passo do processo passa para um nó de modelo (scope=template).",
                "zh-cn": "梳理单个项目的经验：`list_experience` scope=project 与 `experience_usage` scope=project。工作步骤、禁忌以及停用规则见父节点“经验分析”的任务文本。每个项目各自复制一份本子任务。\n\n此处重点：在组织的任何项目中都成立的条目（关于流程、报告、测试纪律）迁入通用规则（`move_experience` scope=general）；只讲流程中某一步的条目迁入模板节点（scope=template）。"
              },
              "acceptance": {
                "ru": "Опыт проекта разобран целиком; общие правила из него вынесены; в отчёте есть числа «было / осталось активными».",
                "en": "The project experience is reviewed in full; general rules are taken out of it; the report gives the numbers «were / left active».",
                "es": "La experiencia del proyecto se revisa por completo; las reglas generales se han sacado de ella; el informe da los números «había / quedan activos».",
                "pt": "A experiência do projeto é revisada por inteiro; as regras gerais foram retiradas dela; o relatório traz os números «havia / restam ativos».",
                "zh-cn": "项目经验被完整梳理；其中的通用规则已迁出；报告给出“原有／剩余启用”的数字。"
              }
            }
          ]
        }
        """;

    /// <summary>Код набора «Строгое код-ревью».</summary>
    public const string StrictReviewCode = "style.strict-review";

    /// <summary>
    /// НАБОР «СТРОГОЕ КОД-РЕВЬЮ» (9 записей, T-272-S0). Идентификаторы записей и узла шаблона
    /// ФИКСИРОВАННЫЕ (диапазон <c>b1e5a0c0-…-0000000p…</c> занят наборами опыта) — иначе в
    /// организации из нескольких серверов копился бы второй комплект записей.
    ///
    /// <para>ПОЧЕМУ ЭТОТ СТИЛЬ ВЗЯТ ПЕРВЫМ: просмотр чужой работы — единственное место, где
    /// дисциплина целиком держится на договорённости, а не на инструменте; правила «сначала
    /// задание, потом дифф», «у замечания есть вес» и «вердикт обязателен» меняют поведение
    /// исполнителя сразу и не требуют ни одной настройки системы.</para>
    /// </summary>
    public const string StrictReviewJson = """
        {
          "code": "style.strict-review",
          "version": 1,
          "name": {
            "ru": "Строгое код-ревью",
            "en": "Strict code review",
            "es": "Revisión de código estricta",
            "pt": "Revisão de código rigorosa",
            "zh-cn": "严格代码审查"
          },
          "description": {
            "ru": "Стиль работы: как мы смотрим чужой код и что обязаны написать в отзыве.",
            "en": "A working style: how we read someone else's code and what a review must say.",
            "es": "Estilo de trabajo: cómo leemos el código ajeno y qué debe decir la revisión.",
            "pt": "Estilo de trabalho: como lemos o código alheio e o que a revisão deve dizer.",
            "zh-cn": "工作风格：我们如何阅读他人的代码，以及评审必须写明什么。"
          },
          "doc": {
            "ru": "packs/style.strict-review.md",
            "en": "packs/style.strict-review.md",
            "es": "packs/style.strict-review.md",
            "pt": "packs/style.strict-review.md",
            "zh-cn": "packs/style.strict-review.md"
          },
          "records": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0001",
              "skill": "code-review",
              "tags": ["ревью"],
              "alwaysLoad": false,
              "text": {
                "ru": "Отзыв о коде начинается со списка того, что СЛОМАЕТСЯ: сценарий отказа с конкретными входными данными и ожидаемым результатом. Замечания о стиле идут после и отдельным разделом — иначе они заслоняют дефект.",
                "en": "A code review starts with the list of what WILL BREAK: a failure scenario with concrete inputs and the expected result. Style remarks come afterwards, in a separate section — otherwise they hide the defect.",
                "es": "Una revisión de código empieza con la lista de lo que SE ROMPERÁ: un escenario de fallo con entradas concretas y el resultado esperado. Las observaciones de estilo van después, en una sección aparte; si no, tapan el defecto.",
                "pt": "Uma revisão de código começa pela lista do que VAI QUEBRAR: um cenário de falha com entradas concretas e o resultado esperado. Observações de estilo vêm depois, em seção separada — caso contrário escondem o defeito.",
                "zh-cn": "代码评审首先列出会出问题的地方：给出具体输入与预期结果的失败场景。风格类意见放在其后的单独一节——否则会淹没真正的缺陷。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0002",
              "skill": "code-review",
              "tags": ["ревью", "тесты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Замечание без проверки, которая его ловит, считается недоказанным: к каждому дефекту называется имя теста — существующего или того, который надо написать.",
                "en": "A remark with no test that catches it counts as unproven: every defect names a test — an existing one, or the one that has to be written.",
                "es": "Una observación sin una prueba que la detecte se considera no demostrada: cada defecto nombra una prueba, existente o por escribir.",
                "pt": "Uma observação sem um teste que a detecte é considerada não comprovada: cada defeito indica um teste — existente ou a escrever.",
                "zh-cn": "没有相应测试能捕获的意见视为未经证实：每个缺陷都要指明一个测试——已有的，或者需要新写的。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0004",
              "skill": "code-review",
              "tags": ["ревью", "требования"],
              "alwaysLoad": false,
              "text": {
                "ru": "Просмотр начинается с задания и критериев приёмки, а не с текста правки. Дефект «сделано не то, о чём просили» дороже любой ошибки в коде, и по одному только тексту правки его не видно.",
                "en": "A review starts with the task and its acceptance criteria, not with the diff. The defect «this is not what was asked for» costs more than any error in the code, and the diff alone does not show it.",
                "es": "La revisión empieza por el encargo y sus criterios de aceptación, no por el diff. El defecto «no es lo que se pidió» cuesta más que cualquier error de código, y el diff por sí solo no lo muestra.",
                "pt": "A revisão começa pelo pedido e pelos critérios de aceitação, não pelo diff. O defeito «não é o que foi pedido» custa mais do que qualquer erro de código, e o diff sozinho não o revela.",
                "zh-cn": "评审从任务书和验收标准开始，而不是从改动文本开始。“做的不是要求的东西”这种缺陷比任何代码错误都昂贵，而且只看改动是看不出来的。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0005",
              "skill": "code-review",
              "tags": ["ревью"],
              "alwaysLoad": false,
              "text": {
                "ru": "У каждого замечания есть место (файл и строка) и вес: блокирующее, важное, на усмотрение автора. Отзыв без веса читается как «всё плохо» — автор чинит опечатки и проходит мимо дефекта.",
                "en": "Every remark names a place (file and line) and a weight: blocking, important, up to the author. A review without weights reads as «everything is bad» — the author fixes typos and walks past the defect.",
                "es": "Cada observación indica un lugar (archivo y línea) y un peso: bloqueante, importante, a criterio del autor. Una revisión sin pesos se lee como «todo está mal»: el autor corrige erratas y pasa de largo el defecto.",
                "pt": "Cada observação indica um lugar (arquivo e linha) e um peso: bloqueante, importante, a critério do autor. Uma revisão sem pesos lê-se como «está tudo mal»: o autor corrige erros de digitação e passa ao largo do defeito.",
                "zh-cn": "每条意见都要写明位置（文件与行）和分量：阻塞、重要、由作者自行决定。没有分量区分的评审读起来就是“全都不行”——作者去改错别字，却绕过了真正的缺陷。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0006",
              "skill": "code-review",
              "tags": ["ревью", "надёжность"],
              "alwaysLoad": false,
              "text": {
                "ru": "Границы проверяются всегда: пустой вход, ноль, предел, повтор того же вызова, одновременный вызов, отказ внешней службы. По каждой границе в отзыве сказано, что происходит СЕЙЧАС, а не «стоит подумать».",
                "en": "The boundaries are always checked: empty input, zero, the limit, the same call repeated, a concurrent call, an outage of an external service. For each boundary the review says what happens NOW, not «worth thinking about».",
                "es": "Los límites se comprueban siempre: entrada vacía, cero, el máximo, la misma llamada repetida, una llamada concurrente, la caída de un servicio externo. Para cada límite la revisión dice qué pasa AHORA, no «habría que pensarlo».",
                "pt": "Os limites verificam-se sempre: entrada vazia, zero, o máximo, a mesma chamada repetida, uma chamada concorrente, a queda de um serviço externo. Para cada limite a revisão diz o que acontece AGORA, não «vale a pena pensar».",
                "zh-cn": "边界必须逐一检查：空输入、零、上限、同一调用重复、并发调用、外部服务失效。对每个边界都要写出现在会发生什么，而不是“值得考虑一下”。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0007",
              "skill": "code-review",
              "tags": ["ревью", "объём работы"],
              "alwaysLoad": false,
              "text": {
                "ru": "Отзыв ограничен изменённым кодом. Давняя беда по соседству называется отдельной задачей, а не требованием к этой правке: иначе правка не закрывается никогда, а настоящие дефекты тонут в списке пожеланий.",
                "en": "A review is limited to the changed code. An old problem next door becomes a separate task, not a requirement for this change: otherwise the change never closes and the real defects drown in a list of wishes.",
                "es": "La revisión se limita al código modificado. Un problema antiguo al lado se convierte en una tarea aparte, no en un requisito de este cambio: si no, el cambio no se cierra nunca y los defectos reales se ahogan en una lista de deseos.",
                "pt": "A revisão limita-se ao código alterado. Um problema antigo ao lado vira uma tarefa separada, não um requisito desta alteração: caso contrário a alteração nunca fecha e os defeitos reais afogam-se numa lista de desejos.",
                "zh-cn": "评审只针对被改动的代码。旁边的陈年问题另开任务，而不是作为本次改动的要求：否则改动永远无法收尾，真正的缺陷淹没在愿望清单里。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0008",
              "skill": "code-review",
              "tags": ["ревью"],
              "alwaysLoad": false,
              "text": {
                "ru": "Чужой код во время просмотра не правится: отзыв описывает, чинит автор. Правку самого проверяющего не смотрит уже никто — проверка теряется вместе с авторством. Исключение одно: прямая просьба автора.",
                "en": "Someone else's code is not edited during the review: the review describes, the author fixes. A change made by the reviewer is reviewed by nobody — the check is lost together with the authorship. The only exception is a direct request from the author.",
                "es": "El código ajeno no se edita durante la revisión: la revisión describe, el autor corrige. Un cambio hecho por quien revisa ya no lo revisa nadie: la comprobación se pierde junto con la autoría. La única excepción es una petición directa del autor.",
                "pt": "O código alheio não se edita durante a revisão: a revisão descreve, o autor corrige. Uma alteração feita por quem revisa já não é revisada por ninguém — a verificação perde-se junto com a autoria. A única exceção é um pedido direto do autor.",
                "zh-cn": "评审期间不直接改他人的代码：评审负责描述，作者负责修改。评审者自己改的地方没有人再看——检查随着署名一起消失。唯一的例外是作者明确请求。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0009",
              "skill": "code-review",
              "tags": ["ревью"],
              "alwaysLoad": false,
              "text": {
                "ru": "Просмотр заканчивается однозначным вердиктом: принято, принято с правками, вернуть на доработку. Отзыв без вердикта не закрывает работу и оставляет её висеть между исполнителем и проверяющим.",
                "en": "A review ends with an unambiguous verdict: accepted, accepted with fixes, sent back. A review with no verdict closes nothing and leaves the work hanging between the author and the reviewer.",
                "es": "La revisión termina con un veredicto inequívoco: aceptado, aceptado con correcciones, devuelto. Una revisión sin veredicto no cierra nada y deja el trabajo colgando entre el autor y quien revisa.",
                "pt": "A revisão termina com um veredicto inequívoco: aceito, aceito com correções, devolvido. Uma revisão sem veredicto não fecha nada e deixa o trabalho pendurado entre o autor e o revisor.",
                "zh-cn": "评审必须以明确结论收尾：通过、带修改通过、退回返工。没有结论的评审关不掉任何工作，只会让它悬在作者与评审者之间。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0010",
              "skill": "code-write",
              "tags": ["ревью"],
              "alwaysLoad": false,
              "text": {
                "ru": "Перед тем как отдать работу на просмотр, автор проходит свою правку сам и может объяснить каждую строку. Необъяснимое изменение — отладочный вывод, случайное переформатирование, забытый файл — убирается ДО просмотра, а не обсуждается в отзыве.",
                "en": "Before handing the work over for review, the author walks through the change and can explain every line. An unexplainable change — a debug print, an accidental reformatting, a forgotten file — is removed BEFORE the review, not discussed in it.",
                "es": "Antes de entregar el trabajo a revisión, el autor recorre su cambio y puede explicar cada línea. Un cambio inexplicable — una traza de depuración, un reformateo accidental, un archivo olvidado — se quita ANTES de la revisión, no se discute en ella.",
                "pt": "Antes de entregar o trabalho para revisão, o autor percorre a sua alteração e consegue explicar cada linha. Uma alteração inexplicável — um print de depuração, uma reformatação acidental, um arquivo esquecido — retira-se ANTES da revisão, não se discute nela.",
                "zh-cn": "把工作交付评审之前，作者先自己过一遍改动，并且能解释每一行。解释不了的改动——调试输出、无意的重新排版、误提交的文件——要在评审之前删掉，而不是留到评审里讨论。"
              }
            }
          ],
          "templates": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p1001",
              "parent": null,
              "skills": ["code-review"],
              "title": {
                "ru": "Код-ревью изменений",
                "en": "Review of the changes",
                "es": "Revisión de los cambios",
                "pt": "Revisão das alterações",
                "zh-cn": "变更评审"
              },
              "description": {
                "ru": "Просмотреть изменения ветки по стилю «Строгое код-ревью».",
                "en": "Review the branch changes following the «Strict code review» style.",
                "es": "Revisar los cambios de la rama según el estilo «Revisión de código estricta».",
                "pt": "Revisar as alterações do ramo seguindo o estilo «Revisão de código rigorosa».",
                "zh-cn": "按照“严格代码审查”风格评审分支变更。"
              },
              "acceptance": {
                "ru": "Каждый дефект назван сценарием отказа и именем проверки.",
                "en": "Every defect names a failure scenario and a test.",
                "es": "Cada defecto indica un escenario de fallo y una prueba.",
                "pt": "Cada defeito indica um cenário de falha e um teste.",
                "zh-cn": "每个缺陷都给出失败场景和对应测试。"
              }
            }
          ]
        }
        """;

    /// <summary>
    /// НАБОР «РАЗРАБОТКА ЧЕРЕЗ ТЕСТЫ» (T-272-S0). Методология выбрана первой из трёх, потому
    /// что она меняет ПОРЯДОК действий исполнителя, а не только форму отчёта: сначала красная
    /// проверка, потом код. Записи разведены по навыкам намеренно — правило про минимальную
    /// правку нужно тому, кто пишет код (<c>code-write</c>), правило про воспроизводимость —
    /// тому, кто пишет проверки (<c>code-test</c>), правило про красное у соседа — обоим.
    /// Идентификаторы фиксированные, диапазон <c>…-0000000p01NN</c>.
    /// </summary>
    public const string TddJson = """
        {
          "code": "style.tdd",
          "version": 1,
          "name": {
            "ru": "Разработка через тесты",
            "en": "Test-driven development",
            "es": "Desarrollo guiado por pruebas",
            "pt": "Desenvolvimento guiado por testes",
            "zh-cn": "测试驱动开发"
          },
          "description": {
            "ru": "Стиль работы: сначала красная проверка, потом код; дефект чинится проверкой, рефакторинг идёт по зелёному.",
            "en": "A working style: the red test first, the code second; a defect is fixed through a test, refactoring happens on green.",
            "es": "Estilo de trabajo: primero la prueba en rojo, después el código; el defecto se corrige con una prueba y la refactorización se hace en verde.",
            "pt": "Estilo de trabalho: primeiro o teste vermelho, depois o código; o defeito corrige-se com um teste e a refatoração faz-se no verde.",
            "zh-cn": "工作风格：先写失败的测试，再写代码；缺陷通过测试来修，重构在全绿状态下进行。"
          },
          "doc": {
            "ru": "packs/style.tdd.md",
            "en": "packs/style.tdd.md",
            "es": "packs/style.tdd.md",
            "pt": "packs/style.tdd.md",
            "zh-cn": "packs/style.tdd.md"
          },
          "records": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0101",
              "skill": "code-test",
              "tags": ["тесты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Проверка пишется ПЕРВОЙ и обязана упасть — именно по той причине, ради которой написана. Проверка, зелёная ещё до правки, не доказывает ничего: она проверяет не то, что вы думаете. В отчёте приводится текст падения красного прогона — это и есть доказательство, что проверка работает.",
                "en": "The test is written FIRST and must fail — for exactly the reason it was written for. A test that is green before the change proves nothing: it checks something other than what you think. The report quotes the failure message of the red run — that is the proof the test works.",
                "es": "La prueba se escribe PRIMERO y debe fallar, justo por el motivo para el que fue escrita. Una prueba que ya está en verde antes del cambio no demuestra nada: comprueba algo distinto de lo que crees. El informe cita el mensaje de fallo de la ejecución en rojo: esa es la prueba de que la prueba funciona.",
                "pt": "O teste escreve-se PRIMEIRO e tem de falhar — exatamente pelo motivo para o qual foi escrito. Um teste que já está verde antes da alteração não prova nada: verifica outra coisa que não a que você pensa. O relatório cita a mensagem de falha da execução vermelha — essa é a prova de que o teste funciona.",
                "zh-cn": "测试先写，并且必须失败——而且要因为它被写出来的那个原因而失败。在改动之前就变绿的测试什么也证明不了：它检查的不是你以为的东西。报告里要给出失败运行的报错文本——这才是测试有效的证据。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0102",
              "skill": "code-test",
              "tags": ["тесты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Одна проверка — одно утверждение о поведении. Имя проверки называет условие и ожидаемый результат («пустой список отдаёт ноль»), а не имя метода: по красной строке прогона должно быть понятно, что сломалось, не открывая исходник.",
                "en": "One test — one statement about behaviour. The name of the test states the condition and the expected result («an empty list returns zero»), not the name of a method: the red line of the run must say what broke without opening the source.",
                "es": "Una prueba, una afirmación sobre el comportamiento. El nombre de la prueba enuncia la condición y el resultado esperado («una lista vacía devuelve cero»), no el nombre de un método: la línea roja de la ejecución debe decir qué se rompió sin abrir el código.",
                "pt": "Um teste — uma afirmação sobre o comportamento. O nome do teste enuncia a condição e o resultado esperado («uma lista vazia devolve zero»), não o nome de um método: a linha vermelha da execução deve dizer o que quebrou sem abrir o código.",
                "zh-cn": "一个测试只断言一件事。测试名要写出条件和预期结果（“空列表返回零”），而不是方法名：光看运行结果里那行红字，就该知道坏了什么，不必打开源码。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0103",
              "skill": "code-write",
              "tags": ["тесты", "разработка"],
              "alwaysLoad": false,
              "text": {
                "ru": "На красную проверку пишется САМАЯ МАЛАЯ правка, которая делает её зелёной. Всё, что «пригодится потом», идёт следующим кругом и со своей проверкой: код, за которым не стоит ни одной красной проверки, никем не заказан и никем не проверяется.",
                "en": "A red test gets the SMALLEST change that turns it green. Everything that «will come in handy later» goes into the next round with a test of its own: code with no red test behind it was ordered by nobody and is checked by nobody.",
                "es": "A una prueba en rojo le corresponde el cambio MÁS PEQUEÑO que la pone en verde. Todo lo que «servirá más adelante» va en la siguiente vuelta y con su propia prueba: el código que no tiene detrás ninguna prueba en rojo no lo pidió nadie y no lo comprueba nadie.",
                "pt": "A um teste vermelho corresponde a MENOR alteração que o torna verde. Tudo o que «vai servir mais tarde» vai na volta seguinte e com o seu próprio teste: código sem nenhum teste vermelho atrás não foi pedido por ninguém e não é verificado por ninguém.",
                "zh-cn": "针对一个失败的测试，只写让它变绿的最小改动。所有“以后用得上”的东西留到下一轮，并且配上自己的测试：背后没有任何失败测试的代码，既没人要求，也没人检查。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0104",
              "skill": "code-debug",
              "tags": ["тесты", "дефекты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Починка дефекта начинается с проверки, которая его воспроизводит, и только потом правится код. Дефект, закрытый без такой проверки, считается незакрытым: ничто не мешает ему вернуться следующей же правкой, и никто этого не заметит.",
                "en": "Fixing a defect starts with a test that reproduces it, and only then is the code changed. A defect closed without such a test counts as not closed: nothing stops it from coming back with the very next change, and nobody will notice.",
                "es": "Corregir un defecto empieza por una prueba que lo reproduce, y solo después se toca el código. Un defecto cerrado sin esa prueba cuenta como no cerrado: nada impide que vuelva con el siguiente cambio, y nadie se dará cuenta.",
                "pt": "Corrigir um defeito começa por um teste que o reproduz, e só depois se mexe no código. Um defeito fechado sem esse teste conta como não fechado: nada impede que volte já na alteração seguinte, e ninguém vai reparar.",
                "zh-cn": "修缺陷先写能复现它的测试，然后才改代码。没有这样一个测试就宣布修好的缺陷视为没修：下一次改动它随时会回来，而且没有人会察觉。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0105",
              "skill": "code-test",
              "tags": ["тесты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Проверяется наблюдаемое поведение — вход и выход, итоговое состояние, обращения наружу, — а не внутреннее устройство. Проверка, знающая про приватные поля и порядок внутренних вызовов, краснеет от любой перестановки кода и тем самым запрещает рефакторинг.",
                "en": "What is checked is observable behaviour — input and output, the resulting state, the calls made outwards — not the internal construction. A test that knows about private fields and the order of internal calls goes red at any rearrangement of the code and thereby forbids refactoring.",
                "es": "Se comprueba el comportamiento observable — entrada y salida, el estado resultante, las llamadas hacia fuera —, no la construcción interna. Una prueba que conoce campos privados y el orden de las llamadas internas se pone roja ante cualquier reordenación del código y con ello prohíbe la refactorización.",
                "pt": "Verifica-se o comportamento observável — entrada e saída, o estado resultante, as chamadas para fora —, não a construção interna. Um teste que conhece campos privados e a ordem das chamadas internas fica vermelho a qualquer rearranjo do código e com isso proíbe a refatoração.",
                "zh-cn": "要检查的是可观察的行为——输入与输出、最终状态、对外部的调用——而不是内部构造。知道私有字段和内部调用顺序的测试，代码稍一挪动就变红，等于禁止了重构。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0106",
              "skill": "code-test",
              "tags": ["тесты"],
              "alwaysLoad": false,
              "text": {
                "ru": "Проверка обязана давать один и тот же ответ всегда: без текущего времени, случайных чисел, сети, общих каталогов и зависимости от порядка выполнения. Время, случайность и внешние службы подаются параметром или подделкой. Плавающая проверка хуже отсутствующей — её краснота перестаёт что-либо значить.",
                "en": "A test must give the same answer every time: no current time, no random numbers, no network, no shared directories, no dependence on the order of execution. Time, randomness and external services are passed in as a parameter or a fake. A flaky test is worse than a missing one — its red stops meaning anything.",
                "es": "Una prueba debe dar siempre la misma respuesta: sin hora actual, sin números aleatorios, sin red, sin directorios compartidos y sin depender del orden de ejecución. El tiempo, el azar y los servicios externos se pasan como parámetro o como doble. Una prueba inestable es peor que ninguna: su rojo deja de significar algo.",
                "pt": "Um teste tem de dar sempre a mesma resposta: sem hora atual, sem números aleatórios, sem rede, sem diretórios partilhados e sem depender da ordem de execução. O tempo, o acaso e os serviços externos entram como parâmetro ou como duplo. Um teste instável é pior do que nenhum — o seu vermelho deixa de significar alguma coisa.",
                "zh-cn": "测试必须每次都给出同样的结果：不依赖当前时间、随机数、网络、公共目录，也不依赖执行顺序。时间、随机性和外部服务通过参数或替身传入。不稳定的测试比没有测试更糟——它变红已经说明不了任何问题。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0107",
              "skill": "code-test",
              "tags": ["тесты", "прогон"],
              "alwaysLoad": false,
              "text": {
                "ru": "Зелёный прогон засчитывается только вместе с ЧИСЛОМ прошедших проверок: фильтр с опечаткой не находит ни одной проверки и заканчивается успехом. Число сверяется с прошлым прогоном и называется в отчёте.",
                "en": "A green run counts only together with the NUMBER of tests that passed: a filter with a typo finds no test at all and still ends in success. The number is compared with the previous run and named in the report.",
                "es": "Una ejecución en verde solo cuenta junto con el NÚMERO de pruebas superadas: un filtro con una errata no encuentra ninguna prueba y aun así termina con éxito. El número se compara con la ejecución anterior y se indica en el informe.",
                "pt": "Uma execução verde só conta junto com o NÚMERO de testes aprovados: um filtro com um erro de digitação não encontra teste nenhum e mesmo assim termina com sucesso. O número compara-se com a execução anterior e indica-se no relatório.",
                "zh-cn": "全绿的运行只有连同通过数量一起才算数：过滤条件写错一个字母就一个测试也匹配不到，结果照样是成功。数量要与上一次运行对比，并写进报告。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0108",
              "skill": "code-refactor",
              "tags": ["тесты", "рефакторинг"],
              "alwaysLoad": false,
              "text": {
                "ru": "Рефакторинг делается на зелёном и не меняет ни одной проверки. Правка кода и правка проверок в одном шаге лишают доказательной силы обе: уже не понять, поведение изменилось или ожидание подогнали под новый код.",
                "en": "Refactoring is done on green and changes no test. Changing the code and the tests in one step robs both of their evidential value: it is no longer possible to tell whether the behaviour changed or the expectation was bent to fit the new code.",
                "es": "La refactorización se hace en verde y no toca ninguna prueba. Cambiar el código y las pruebas en un mismo paso priva a ambos de valor probatorio: ya no se sabe si cambió el comportamiento o si se ajustó la expectativa al código nuevo.",
                "pt": "A refatoração faz-se no verde e não altera nenhum teste. Mudar o código e os testes no mesmo passo tira a ambos o valor de prova: já não se sabe se o comportamento mudou ou se a expectativa foi ajustada ao código novo.",
                "zh-cn": "重构在全绿状态下进行，且不改动任何测试。把改代码和改测试放在同一步，会让两者都失去证明力：再也分不清是行为变了，还是把预期改成迁就新代码。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0109",
              "skill": "code-test",
              "tags": ["тесты", "прогон"],
              "alwaysLoad": false,
              "text": {
                "ru": "Чужая проверка, покрасневшая от твоей правки, — это результат, а не помеха. Сначала выясни, какое поведение она защищала. Переписать или отключить её можно только объяснив в отчёте, что поведение изменено намеренно и по чьему решению.",
                "en": "Someone else's test that went red because of your change is a result, not an obstacle. First find out what behaviour it was protecting. Rewriting or switching it off is allowed only with an explanation in the report: that the behaviour was changed deliberately, and by whose decision.",
                "es": "Una prueba ajena que se puso roja por tu cambio es un resultado, no un estorbo. Averigua primero qué comportamiento protegía. Reescribirla o desactivarla solo se permite explicando en el informe que el comportamiento se cambió a propósito y por decisión de quién.",
                "pt": "Um teste alheio que ficou vermelho por causa da sua alteração é um resultado, não um estorvo. Descubra primeiro que comportamento ele protegia. Reescrevê-lo ou desligá-lo só é permitido explicando no relatório que o comportamento foi mudado de propósito e por decisão de quem.",
                "zh-cn": "别人的测试因你的改动而变红，这是结果，不是障碍。先弄清它守护的是什么行为。只有在报告中说明行为是有意改变的、以及由谁决定，才可以改写或停用它。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0110",
              "skill": "code-write",
              "tags": ["тесты", "отчёт"],
              "alwaysLoad": false,
              "text": {
                "ru": "Случай, который ты заметил, но не покрыл проверкой, не остаётся в голове: он либо покрывается сейчас, либо называется в отчёте отдельной строкой «не покрыто». Молчание о непокрытом читается как «проверено».",
                "en": "A case you noticed but did not cover with a test does not stay in your head: either it is covered now, or it is named in the report on a line of its own, «not covered». Silence about what is uncovered reads as «checked».",
                "es": "Un caso que has visto pero no has cubierto con una prueba no se queda en tu cabeza: o se cubre ahora, o se nombra en el informe en una línea aparte, «sin cubrir». Callar lo no cubierto se lee como «comprobado».",
                "pt": "Um caso que você notou mas não cobriu com um teste não fica na sua cabeça: ou é coberto agora, ou é nomeado no relatório numa linha própria, «não coberto». O silêncio sobre o que não está coberto lê-se como «verificado».",
                "zh-cn": "你注意到却没有用测试覆盖的情况，不能只留在脑子里：要么现在就覆盖，要么在报告里单列一行“未覆盖”。对未覆盖之处保持沉默，读起来就等于“已验证”。"
              }
            }
          ],
          "templates": []
        }
        """;

    /// <summary>
    /// НАБОР «ИССЛЕДОВАНИЕ И ОТЧЁТ» (T-272-S0). Третья методология выбрана потому, что задачи
    /// разбора и обзора агенту ставят чаще всего, а проверить их нечем — тестов у отчёта нет,
    /// и дисциплина источников, чисел и раздела «что не сделано» заменяет собой прогон.
    ///
    /// <para>ЕДИНСТВЕННАЯ ЗАПИСЬ С ПОМЕТКОЙ «ЗАГРУЖАТЬ ВСЕГДА» ВО ВСЁМ ДИСТРИБУТИВЕ — про
    /// раздел «что НЕ сделано»: навыка у неё нет (правило верно любому исполнителю), а без
    /// пометки запись без навыка в задание не попадёт вовсе. Остальные записи наборов
    /// проставленный навык имеют и в пометке не нуждаются.</para>
    /// </summary>
    public const string ResearchReportJson = """
        {
          "code": "style.research-report",
          "version": 1,
          "name": {
            "ru": "Исследование и отчёт",
            "en": "Research and reporting",
            "es": "Investigación e informe",
            "pt": "Pesquisa e relatório",
            "zh-cn": "调研与报告"
          },
          "description": {
            "ru": "Стиль работы: как мы собираем факты, чем подтверждаем числа и как пишем отчёт, по которому принимают решение.",
            "en": "A working style: how we gather facts, what backs the numbers up, and how we write a report someone decides by.",
            "es": "Estilo de trabajo: cómo reunimos hechos, con qué respaldamos las cifras y cómo escribimos un informe con el que se decide.",
            "pt": "Estilo de trabalho: como reunimos fatos, com o que sustentamos os números e como escrevemos um relatório pelo qual se decide.",
            "zh-cn": "工作风格：我们如何收集事实、用什么支撑数字，以及如何写出能据以决策的报告。"
          },
          "doc": {
            "ru": "packs/style.research-report.md",
            "en": "packs/style.research-report.md",
            "es": "packs/style.research-report.md",
            "pt": "packs/style.research-report.md",
            "zh-cn": "packs/style.research-report.md"
          },
          "records": [
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0201",
              "skill": "analyze-data",
              "tags": ["исследование"],
              "alwaysLoad": false,
              "text": {
                "ru": "Каждый факт отчёта идёт с источником и датой проверки. Факт без источника подаётся как предположение и прямо помечается таковым — иначе через месяц его уже нечем отличить от проверенного, и на нём строят решение.",
                "en": "Every fact in the report comes with a source and the date it was checked. A fact without a source is presented as an assumption and marked as one — otherwise, a month later, nothing tells it apart from a verified fact, and decisions get built on it.",
                "es": "Cada hecho del informe va con su fuente y la fecha de comprobación. Un hecho sin fuente se presenta como suposición y se marca como tal: si no, un mes después nada lo distingue de un hecho verificado y se decide sobre él.",
                "pt": "Cada fato do relatório vem com a fonte e a data de verificação. Um fato sem fonte apresenta-se como suposição e é marcado como tal — caso contrário, um mês depois nada o distingue de um fato verificado, e decide-se com base nele.",
                "zh-cn": "报告中的每个事实都要附来源和核实日期。没有来源的事实按假设呈现并明确标注——否则一个月后就无法与已核实的事实区分，而决策却建立在它上面。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0202",
              "skill": "analyze-data",
              "tags": ["исследование", "источники"],
              "alwaysLoad": false,
              "text": {
                "ru": "Пересказ и ответ поисковой модели источником не считаются. Идентификаторы, цены, ограничения и имена полей интерфейсов сверяются с первичным источником — самим репозиторием, каталогом API, официальной страницей. Выдуманное имя поля видно только там.",
                "en": "A retelling or an answer from a search model is not a source. Identifiers, prices, limits and the field names of an interface are verified against the primary source — the repository itself, the API catalogue, the official page. An invented field name shows up only there.",
                "es": "Un resumen de segunda mano o la respuesta de un buscador con IA no son fuente. Identificadores, precios, límites y nombres de campos de una interfaz se cotejan con la fuente primaria: el propio repositorio, el catálogo de la API, la página oficial. Un nombre de campo inventado solo se ve ahí.",
                "pt": "Um relato de segunda mão ou a resposta de um modelo de busca não são fonte. Identificadores, preços, limites e nomes de campos de uma interface conferem-se com a fonte primária — o próprio repositório, o catálogo da API, a página oficial. Um nome de campo inventado só aparece lá.",
                "zh-cn": "转述和搜索型模型的回答都不算来源。标识符、价格、限制和接口字段名必须与第一手来源核对——仓库本身、API 目录、官方页面。凭空捏造的字段名只有在那里才看得出来。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0203",
              "skill": "analyze-requirements",
              "tags": ["исследование"],
              "alwaysLoad": false,
              "text": {
                "ru": "Работа начинается с одного вопроса, на который отчёт обязан ответить, и с признака достаточности ответа. Без них исследование расширяется бесконечно, а читатель получает обзор вместо решения.",
                "en": "The work starts with a single question the report must answer, and with the sign that the answer is sufficient. Without them the research expands endlessly and the reader gets a survey instead of a decision.",
                "es": "El trabajo empieza con una sola pregunta que el informe debe responder y con el criterio de cuándo la respuesta basta. Sin ellos la investigación se expande sin fin y quien lee recibe un panorama en vez de una decisión.",
                "pt": "O trabalho começa com uma única pergunta que o relatório tem de responder e com o critério de quando a resposta basta. Sem isso a pesquisa expande-se sem fim e quem lê recebe um panorama em vez de uma decisão.",
                "zh-cn": "工作从一个报告必须回答的问题开始，并明确答案何时算充分。没有这两点，调研会无限扩张，读者拿到的是综述而不是决定。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0204",
              "skill": "text-docs",
              "tags": ["отчёт"],
              "alwaysLoad": false,
              "text": {
                "ru": "Отчёт начинается с вывода: что делать или что выбрать. Дальше — обоснование, дальше — подробности. Решение принимают по первому абзацу; вывод, спрятанный в конце, просто не дочитывают.",
                "en": "A report starts with the conclusion: what to do or what to choose. Then the reasoning, then the details. The decision is made from the first paragraph; a conclusion hidden at the end is simply never reached.",
                "es": "El informe empieza por la conclusión: qué hacer o qué elegir. Después el razonamiento, después los detalles. La decisión se toma con el primer párrafo; una conclusión escondida al final sencillamente no se llega a leer.",
                "pt": "O relatório começa pela conclusão: o que fazer ou o que escolher. Depois o raciocínio, depois os detalhes. A decisão é tomada pelo primeiro parágrafo; uma conclusão escondida no fim simplesmente não chega a ser lida.",
                "zh-cn": "报告从结论开始：要做什么，或者选哪个。随后是理由，再后是细节。决定是看第一段做出的；藏在末尾的结论根本读不到。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0205",
              "skill": "",
              "tags": ["отчёт"],
              "alwaysLoad": true,
              "text": {
                "ru": "Раздел «что НЕ сделано и не проверено» обязателен в любом отчёте. Умолчание о непроверенном дороже признания: по этому разделу планируется следующий шаг, а «забыл сказать» обнаруживается уже в работе и ценой чужого времени.",
                "en": "The section «what was NOT done and not checked» is mandatory in any report. Keeping quiet about the unchecked costs more than admitting it: the next step is planned from that section, while «forgot to mention» surfaces during the work and at someone else's expense.",
                "es": "La sección «qué NO se hizo ni se comprobó» es obligatoria en cualquier informe. Callar lo no comprobado cuesta más que reconocerlo: el siguiente paso se planifica con esa sección, mientras que el «se me olvidó decirlo» aparece ya en plena faena y a costa del tiempo ajeno.",
                "pt": "A seção «o que NÃO foi feito nem verificado» é obrigatória em qualquer relatório. Calar o que não foi verificado custa mais do que admiti-lo: o próximo passo planeia-se a partir dessa seção, enquanto o «esqueci-me de dizer» aparece já durante o trabalho e à custa do tempo alheio.",
                "zh-cn": "任何报告都必须有“哪些没做、哪些没验证”这一节。隐瞒未验证的部分比承认代价更大：下一步正是依据这一节来规划，而“忘了说”会在干活时才暴露，代价由别人的时间来付。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0206",
              "skill": "analyze-data",
              "tags": ["исследование", "сравнение"],
              "alwaysLoad": false,
              "text": {
                "ru": "Варианты сравниваются таблицей с одинаковыми колонками для всех кандидатов. Отвергнутый вариант остаётся в отчёте вместе с причиной отказа — иначе его предложат снова, и разбор придётся повторить целиком.",
                "en": "Options are compared in a table with the same columns for every candidate. A rejected option stays in the report together with the reason for the rejection — otherwise it will be proposed again and the whole analysis will have to be repeated.",
                "es": "Las opciones se comparan en una tabla con las mismas columnas para todos los candidatos. La opción descartada se queda en el informe junto con el motivo del descarte: si no, alguien volverá a proponerla y habrá que repetir todo el análisis.",
                "pt": "As opções comparam-se numa tabela com as mesmas colunas para todos os candidatos. A opção descartada fica no relatório junto com o motivo do descarte — caso contrário alguém a proporá de novo e será preciso repetir toda a análise.",
                "zh-cn": "各方案用同一组列的表格来比较。被否决的方案连同否决理由一起留在报告里——否则它会被再次提出，整个分析得从头再做一遍。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0207",
              "skill": "analyze-data",
              "tags": ["исследование", "числа"],
              "alwaysLoad": false,
              "text": {
                "ru": "Число приводится с единицей измерения, способом получения и датой замера. «Быстрее», «дешевле», «много» без числа — это впечатление, а не результат; по впечатлению решение принять нельзя, и проверить его тоже нечем.",
                "en": "A number comes with its unit, the way it was obtained and the date of the measurement. «Faster», «cheaper», «a lot» without a number is an impression, not a result; no decision can be made from an impression, and there is no way to check it either.",
                "es": "Una cifra se da con su unidad, el modo en que se obtuvo y la fecha de la medición. «Más rápido», «más barato», «mucho» sin cifra es una impresión, no un resultado: con una impresión no se decide, y tampoco hay con qué comprobarla.",
                "pt": "Um número vem com a sua unidade, o modo como foi obtido e a data da medição. «Mais rápido», «mais barato», «muito» sem número é uma impressão, não um resultado: com uma impressão não se decide, nem há como verificá-la.",
                "zh-cn": "给出数字时要带单位、取得方式和测量日期。没有数字的“更快”“更便宜”“很多”只是印象，不是结果；凭印象无法决策，也无从核验。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0208",
              "skill": "text-summarize",
              "tags": ["отчёт"],
              "alwaysLoad": false,
              "text": {
                "ru": "Объём отчёта задаётся вопросом, а не объёмом собранного материала. Протокол поиска, черновики и сырые выгрузки в отчёт не переносятся: туда идёт только то, что меняет решение. Остальное — приложением или ссылкой.",
                "en": "The size of a report is set by the question, not by the amount of material collected. The search log, the drafts and the raw dumps do not go into the report: only what changes the decision does. The rest goes into an appendix or a link.",
                "es": "La extensión del informe la fija la pregunta, no la cantidad de material reunido. El registro de búsqueda, los borradores y los volcados en bruto no van al informe: allí va solo lo que cambia la decisión. El resto, en un anexo o en un enlace.",
                "pt": "O tamanho do relatório é definido pela pergunta, não pela quantidade de material recolhido. O registro da busca, os rascunhos e os despejos em bruto não vão para o relatório: lá vai só o que muda a decisão. O resto, em anexo ou num link.",
                "zh-cn": "报告的篇幅由问题决定，而不是由收集到的材料多少决定。检索过程、草稿和原始导出不写进报告：进报告的只有能改变决定的内容，其余放附录或给链接。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0209",
              "skill": "analyze-plan",
              "tags": ["отчёт", "решение"],
              "alwaysLoad": false,
              "text": {
                "ru": "Рекомендация одна и названа прямо, даже когда варианты близки. «Оба хороши» возвращает выбор заказчику и обесценивает всю работу. Рядом с рекомендацией перечисляются её риски и условие, при котором она перестаёт быть верной.",
                "en": "There is one recommendation and it is stated plainly, even when the options are close. «Both are good» hands the choice back to the customer and devalues the whole work. Next to the recommendation stand its risks and the condition under which it stops holding.",
                "es": "La recomendación es una y se enuncia sin rodeos, incluso cuando las opciones están parejas. «Ambas son buenas» devuelve la elección a quien encargó el trabajo y lo devalúa entero. Junto a la recomendación se enumeran sus riesgos y la condición bajo la cual deja de ser válida.",
                "pt": "A recomendação é uma só e é dita sem rodeios, mesmo quando as opções estão próximas. «Ambas são boas» devolve a escolha a quem encomendou e desvaloriza todo o trabalho. Ao lado da recomendação ficam os seus riscos e a condição sob a qual deixa de valer.",
                "zh-cn": "建议只有一条，并且直接说出来，即使各方案难分伯仲。“两个都不错”等于把选择推回给委托方，让整项工作失去价值。建议旁边要列出它的风险，以及在什么条件下它不再成立。"
              }
            },
            {
              "id": "b1e5a0c0-7d1a-4c31-9f01-0000000p0210",
              "skill": "text-docs",
              "tags": ["отчёт", "источники"],
              "alwaysLoad": false,
              "text": {
                "ru": "Ссылка приводится рабочим адресом, который действительно открывали. Ссылка по памяти не приводится вовсе: неверный адрес в отчёте дороже его отсутствия — по нему ходят, тратят время и перестают верить остальным ссылкам.",
                "en": "A link is given as a working address that was actually opened. A link written from memory is not given at all: a wrong address in a report costs more than no address — people follow it, lose time and stop trusting the other links.",
                "es": "Un enlace se da como una dirección que funciona y que realmente se abrió. Un enlace de memoria no se pone: una dirección errónea en el informe cuesta más que su ausencia, porque la gente la sigue, pierde tiempo y deja de fiarse de los demás enlaces.",
                "pt": "Um link dá-se como um endereço que funciona e que foi realmente aberto. Um link de memória não se põe: um endereço errado no relatório custa mais do que a sua ausência — as pessoas seguem-no, perdem tempo e deixam de confiar nos outros links.",
                "zh-cn": "链接要给真正打开过、确实可用的地址。凭记忆写的链接干脆不要写：报告里一个错误地址比没有地址代价更大——有人照着点开、浪费时间，并且不再相信其余链接。"
              }
            }
          ],
          "templates": []
        }
        """;
}
