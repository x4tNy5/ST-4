using Stateless;

namespace BugPro;

public enum TicketPhase
{
    Incoming,
    Classified,
    BeingFixed,
    OnHold,
    AwaitingVerification,
    Done,
    Shelved
}

public enum TicketAction
{
    Categorize,
    AssignOwner,
    PickUp,
    Block,
    Unblock,
    SendToVerification,
    SendBack,
    Complete,
    Shelf,
    Revive,
    AppendNote
}

public sealed class Bug
{
    private readonly StateMachine<TicketPhase, TicketAction> _flow;
    private readonly StateMachine<TicketPhase, TicketAction>.TriggerWithParameters<string> _annotate;
    private readonly List<string> _journal = [];

    public Bug(int identifier, string summary)
    {
        Identifier = identifier;
        Summary = summary;
        _flow = new StateMachine<TicketPhase, TicketAction>(TicketPhase.Incoming);
        _annotate = _flow.SetTriggerParameters<string>(TicketAction.AppendNote);
        Record($"тикет #{identifier} создан: «{summary}»");
        BuildWorkflow();
    }

    public int Identifier { get; }

    public string Summary { get; }

    public string? Owner { get; private set; }

    public TicketPhase Phase => _flow.State;

    public IReadOnlyList<string> Journal => _journal;

    public bool May(TicketAction action) => _flow.CanFire(action);

    public void Apply(TicketAction action) => _flow.Fire(action);

    public void AssignOwner(string owner)
    {
        Owner = owner;
        Apply(TicketAction.AssignOwner);
    }

    public void AppendNote(string note) => _flow.Fire(_annotate, note);

    public string DescribePhase() => Phase switch
    {
        TicketPhase.Incoming => "ожидает классификации",
        TicketPhase.Classified => "классифицирован, ожидает исполнителя",
        TicketPhase.BeingFixed => "в работе у разработчика",
        TicketPhase.OnHold => "заблокирован внешними зависимостями",
        TicketPhase.AwaitingVerification => "ожидает проверки исправления",
        TicketPhase.Done => "закрыт после успешной проверки",
        TicketPhase.Shelved => "отложен как неактуальный",
        _ => "неизвестная фаза"
    };

    private void BuildWorkflow()
    {
        _flow.Configure(TicketPhase.Incoming)
            .Permit(TicketAction.Categorize, TicketPhase.Classified)
            .OnEntryFrom(TicketAction.Revive, () => Record("тикет переоткрыт"));

        _flow.Configure(TicketPhase.Classified)
            .PermitIf(TicketAction.PickUp, TicketPhase.BeingFixed, HasOwner)
            .Permit(TicketAction.Shelf, TicketPhase.Shelved)
            .PermitReentry(TicketAction.AssignOwner)
            .OnEntryFrom(TicketAction.Categorize, () => Record("приоритет и область определены"));

        _flow.Configure(TicketPhase.BeingFixed)
            .Permit(TicketAction.Block, TicketPhase.OnHold)
            .Permit(TicketAction.SendToVerification, TicketPhase.AwaitingVerification)
            .OnEntryFrom(TicketAction.PickUp, () => Record($"взято в работу: {Owner}"))
            .OnEntryFrom(TicketAction.SendBack, () => Record("возвращено на доработку после ревью"));

        _flow.Configure(TicketPhase.OnHold)
            .Permit(TicketAction.Unblock, TicketPhase.BeingFixed)
            .OnEntry(() => Record("работа приостановлена до снятия блокировки"));

        _flow.Configure(TicketPhase.AwaitingVerification)
            .Permit(TicketAction.Complete, TicketPhase.Done)
            .Permit(TicketAction.SendBack, TicketPhase.BeingFixed)
            .OnEntry(() => Record("патч отправлен на проверку QA"));

        _flow.Configure(TicketPhase.Done)
            .Permit(TicketAction.Revive, TicketPhase.Incoming)
            .OnEntry(() => Record("исправление подтверждено, тикет закрыт"));

        _flow.Configure(TicketPhase.Shelved)
            .Permit(TicketAction.Revive, TicketPhase.Incoming)
            .OnEntry(() => Record("тикет снят с очереди без исправления"));

        _flow.Configure(TicketPhase.Classified)
            .OnEntryFrom(TicketAction.AssignOwner, () => Record($"назначен исполнитель: {Owner}"));

        _flow.Configure(TicketPhase.BeingFixed)
            .OnEntryFrom(TicketAction.Unblock, () => Record("блокировка снята, работа продолжена"));

        _flow.Configure(TicketPhase.Classified)
            .InternalTransition(_annotate, (note, _) => Record($"заметка: {note}"));

        _flow.Configure(TicketPhase.BeingFixed)
            .InternalTransition(_annotate, (note, _) => Record($"заметка: {note}"));

        _flow.Configure(TicketPhase.OnHold)
            .InternalTransition(_annotate, (note, _) => Record($"заметка: {note}"));

        _flow.Configure(TicketPhase.AwaitingVerification)
            .InternalTransition(_annotate, (note, _) => Record($"заметка: {note}"));
    }

    private bool HasOwner() => !string.IsNullOrWhiteSpace(Owner);

    private void Record(string message) => _journal.Add(message);
}

internal static class Program
{
    private static void Main()
    {
        var sample = new Bug(1042, "некорректный расчёт скидки в корзине");

        Console.WriteLine($"Старт: {sample.DescribePhase()}");
        sample.Apply(TicketAction.Categorize);
        sample.AssignOwner("Алексей");
        sample.Apply(TicketAction.PickUp);
        sample.AppendNote("воспроизведено на staging");
        sample.Apply(TicketAction.SendToVerification);
        sample.Apply(TicketAction.Complete);

        Console.WriteLine($"Финал: {sample.DescribePhase()}");
        Console.WriteLine("Журнал изменений:");
        foreach (var entry in sample.Journal)
        {
            Console.WriteLine($"  • {entry}");
        }
    }
}
