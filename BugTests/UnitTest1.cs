using BugPro; 

namespace BugTests;

[TestClass]
public sealed class BugWorkflowTests
{
    private static Bug FreshTicket() => new(7, "падение при экспорте отчёта");

    [TestMethod]
    public void NewTicket_StartsInIncomingPhase()
    {
        var ticket = FreshTicket();

        Assert.AreEqual(TicketPhase.Incoming, ticket.Phase);
        Assert.IsTrue(ticket.Journal[0].Contains("тикет #7"));
    }

    [TestMethod]
    public void Categorize_MovesTicketToClassified()
    {
        var ticket = FreshTicket();

        ticket.Apply(TicketAction.Categorize);

        Assert.AreEqual(TicketPhase.Classified, ticket.Phase);
        Assert.IsTrue(ticket.May(TicketAction.Shelf));
    }

    [TestMethod]
    public void PickUp_WithoutOwner_IsNotAllowed()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);

        Assert.IsFalse(ticket.May(TicketAction.PickUp));
    }

    [TestMethod]
    public void PickUp_WithoutOwner_ThrowsInvalidOperationException()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.PickUp));
    }

    [TestMethod]
    public void AssignOwner_AllowsPickUpTransition()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);
        ticket.AssignOwner("Мария");

        ticket.Apply(TicketAction.PickUp);

        Assert.AreEqual(TicketPhase.BeingFixed, ticket.Phase);
        Assert.AreEqual("Мария", ticket.Owner);
    }

    [TestMethod]
    public void Shelf_FromClassified_MovesTicketToShelved()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);

        ticket.Apply(TicketAction.Shelf);

        Assert.AreEqual(TicketPhase.Shelved, ticket.Phase);
        Assert.IsTrue(ticket.May(TicketAction.Revive));
    }

    [TestMethod]
    public void Block_FromBeingFixed_PlacesTicketOnHold()
    {
        var ticket = MoveToBeingFixed();

        ticket.Apply(TicketAction.Block);

        Assert.AreEqual(TicketPhase.OnHold, ticket.Phase);
        Assert.IsTrue(ticket.Journal.Any(line => line.Contains("приостановлена")));
    }

    [TestMethod]
    public void Unblock_FromOnHold_ReturnsToBeingFixed()
    {
        var ticket = MoveToBeingFixed();
        ticket.Apply(TicketAction.Block);

        ticket.Apply(TicketAction.Unblock);

        Assert.AreEqual(TicketPhase.BeingFixed, ticket.Phase);
    }

    [TestMethod]
    public void SendToVerification_FromBeingFixed_OpensQaPhase()
    {
        var ticket = MoveToBeingFixed();

        ticket.Apply(TicketAction.SendToVerification);

        Assert.AreEqual(TicketPhase.AwaitingVerification, ticket.Phase);
        Assert.IsTrue(ticket.May(TicketAction.Complete));
    }

    [TestMethod]
    public void SendBack_FromVerification_ReturnsToBeingFixed()
    {
        var ticket = MoveToAwaitingVerification();

        ticket.Apply(TicketAction.SendBack);

        Assert.AreEqual(TicketPhase.BeingFixed, ticket.Phase);
    }

    [TestMethod]
    public void Complete_FromVerification_ClosesTicket()
    {
        var ticket = MoveToAwaitingVerification();

        ticket.Apply(TicketAction.Complete);

        Assert.AreEqual(TicketPhase.Done, ticket.Phase);
        Assert.AreEqual("закрыт после успешной проверки", ticket.DescribePhase());
    }

    [TestMethod]
    public void Revive_FromDone_ReopensTicketAsIncoming()
    {
        var ticket = MoveToDone();

        ticket.Apply(TicketAction.Revive);

        Assert.AreEqual(TicketPhase.Incoming, ticket.Phase);
        Assert.IsTrue(ticket.Journal.Any(line => line.Contains("переоткрыт")));
    }

    [TestMethod]
    public void Revive_FromShelved_ReopensTicketAsIncoming()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);
        ticket.Apply(TicketAction.Shelf);

        ticket.Apply(TicketAction.Revive);

        Assert.AreEqual(TicketPhase.Incoming, ticket.Phase);
    }

    [TestMethod]
    public void AppendNote_InClassified_KeepsSamePhase()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);
        var before = ticket.Journal.Count;

        ticket.AppendNote("нужен лог сервера");

        Assert.AreEqual(TicketPhase.Classified, ticket.Phase);
        Assert.AreEqual(before + 1, ticket.Journal.Count);
    }

    [TestMethod]
    public void Categorize_FromBeingFixed_ThrowsInvalidOperationException()
    {
        var ticket = MoveToBeingFixed();

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.Categorize));
    }

    [TestMethod]
    public void Complete_FromIncoming_ThrowsInvalidOperationException()
    {
        var ticket = FreshTicket();

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.Complete));
    }

    [TestMethod]
    public void Block_FromClassified_ThrowsInvalidOperationException()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.Block));
    }

    [TestMethod]
    public void Unblock_FromBeingFixed_ThrowsInvalidOperationException()
    {
        var ticket = MoveToBeingFixed();

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.Unblock));
    }

    [TestMethod]
    public void Shelf_FromBeingFixed_ThrowsInvalidOperationException()
    {
        var ticket = MoveToBeingFixed();

        Assert.ThrowsException<InvalidOperationException>(() => ticket.Apply(TicketAction.Shelf));
    }

    [TestMethod]
    public void HappyPath_ReachesDoneWithExpectedJournalSize()
    {
        var ticket = FreshTicket();

        ticket.Apply(TicketAction.Categorize);
        ticket.AssignOwner("Игорь");
        ticket.Apply(TicketAction.PickUp);
        ticket.Apply(TicketAction.SendToVerification);
        ticket.Apply(TicketAction.Complete);

        Assert.AreEqual(TicketPhase.Done, ticket.Phase);
        Assert.IsTrue(ticket.Journal.Count >= 6);
    }

    [TestMethod]
    public void OnHoldRoundTrip_PreservesOwner()
    {
        var ticket = MoveToBeingFixed();
        ticket.Apply(TicketAction.Block);
        ticket.Apply(TicketAction.Unblock);

        Assert.AreEqual("Игорь", ticket.Owner);
        Assert.AreEqual(TicketPhase.BeingFixed, ticket.Phase);
    }

    [TestMethod]
    public void DescribePhase_ReturnsHumanReadableTextForEachPhase()
    {
        var ticket = MoveToDone();

        Assert.IsFalse(string.IsNullOrWhiteSpace(ticket.DescribePhase()));
        Assert.IsTrue(ticket.DescribePhase().Contains("закрыт"));
    }

    private static Bug MoveToBeingFixed()
    {
        var ticket = FreshTicket();
        ticket.Apply(TicketAction.Categorize);
        ticket.AssignOwner("Игорь");
        ticket.Apply(TicketAction.PickUp);
        return ticket;
    }

    private static Bug MoveToAwaitingVerification()
    {
        var ticket = MoveToBeingFixed();
        ticket.Apply(TicketAction.SendToVerification);
        return ticket;
    }

    private static Bug MoveToDone()
    {
        var ticket = MoveToAwaitingVerification();
        ticket.Apply(TicketAction.Complete);
        return ticket;
    }
}
