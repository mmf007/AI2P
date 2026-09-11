using AI2P.Core.Entities;
using AI2P.UI.Components;
using MudBlazor;

namespace AI2P.UI.Services;

/// <summary>
/// Общий флоу «создать задачу из шаблона» (ТЗ п. 2.4, todo31), используется кнопкой
/// «новая задача» (выбор шаблона), карточкой шаблона и вкладкой «подзадачи» карточки
/// задачи (T-201): анализ иерархии на сервере → переспрос базовой даты и/или варианта
/// автоподбора (только если они нужны) → создание. parentId — задача, подзадачей которой
/// становится созданная голова. Возвращает созданную голову; null — пользователь отменил.
/// </summary>
public static class InstantiateFlow
{
    public static async Task<TaskItem?> RunAsync(IDialogService dialogs, ApiClient api,
        I18nService l, string templateId, string? parentId = null)
    {
        var info = await api.GetTemplateInfoAsync(templateId);
        DateTime? baseDate = null;
        string? pick = null;
        if (info.HasDueDates || info.NeedsExecutorPick)
        {
            var parameters = new DialogParameters<InstantiateDialog>
            {
                { d => d.AskDate, info.HasDueDates },
                { d => d.AskPick, info.NeedsExecutorPick },
                { d => d.TemplateDue, info.MinDueDate },
            };
            var dialog = await dialogs.ShowAsync<InstantiateDialog>(l["task.instantiate.title"], parameters);
            var result = await dialog.Result;
            if (result is not { Canceled: false, Data: InstantiateDialog.Choice choice })
            {
                return null;
            }
            baseDate = choice.BaseDate;
            pick = choice.Pick;
        }
        return await api.InstantiateTemplateAsync(templateId, baseDate, pick, parentId);
    }
}
