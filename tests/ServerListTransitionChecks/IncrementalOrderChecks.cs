using System;
using System.Linq;
using IRSpeedyVPN.Components.ServerListControl;

// Exercise the production planner with explicit country completions, rather
// than treating a successful partial ping as a completed country.
static class IncrementalOrderChecks
{
    static GroupItem[] Countries(int count) => Enumerable.Range(1, count).Select(id => new GroupItem
    {
        Service = new Service { ID = id }, CountryName = "Country " + id,
        SignalText = id + " ms", IsSelectable = true, IsChecking = true
    }).ToArray();
    static ServerItem[] Snapshot(GroupItem[] groups, params int[] order)
        => (order.Length == 0 ? groups : order.Select(id => groups[id - 1])).Select(group => new ServerItem(group)).ToArray();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    static void Unique(ServerListViewModel model)
    {
        var expected = model.Servers.Select(item => item.Identity).OrderBy(id => id).ToArray();
        for (int part = 0; part < 5; part++)
        {
            var shown = model.Slots.Select(slot => slot.GetPart((ServerRowPart)part)).ToArray();
            if (shown.Any(item => item == null) || !shown.Select(item => item.Identity).OrderBy(id => id).SequenceEqual(expected))
                throw new Exception("duplicate, missing or vacant country during reveal");
        }
    }
    static void Order(ServerListViewModel model, params int[] expected)
        => Check(model.Slots.Select(slot => slot.DisplayedServer.Service.ID).SequenceEqual(expected),
            "country order " + string.Join(",", expected));
    static ServerListViewModel Start(GroupItem[] groups)
    {
        var model = new ServerListViewModel();
        model.Publish(Snapshot(groups), true);
        model.BeginCheckCycle();
        foreach (var group in groups) model.MarkChecking(new ServerItem(group).Identity);
        foreach (var slot in model.Slots) slot.PropertyChanged += (sender, args) => Unique(model);
        return model;
    }
    static void Complete(ServerListViewModel model, GroupItem[] groups, int id, bool success = true, params int[] order)
    {
        groups[id - 1].IsChecking = false;
        groups[id - 1].IsSelectable = success;
        model.Publish(Snapshot(groups, order), true, true, new ServerItem(groups[id - 1]).Identity);
    }
    static int Drain(ServerListViewModel model)
    {
        int count = 0;
        ServerSortPass pass;
        while ((pass = model.TryBeginPass()) != null)
        {
            if (++count > 2) throw new Exception("unchanged snapshot needs repeated intermediate moves");
            for (int part = 0; part < 5; part++) { model.Reveal(pass, (ServerRowPart)part); Unique(model); }
            model.Finish(pass);
            Unique(model);
        }
        return count;
    }
    internal static void Run()
    {
        var groups = Countries(5);
        var model = Start(groups);
        // D finishes while A/B/C/E are still testing. D moves directly to 1.
        Complete(model, groups, 4);
        var pass = model.TryBeginPass();
        Check(pass != null && pass.Rows.Length == 4 && pass.Rows[0].Target.Service.ID == 4,
            "one completed country promotes immediately with every displaced row in one pass");
        var target = pass.Rows[0].Target;
        model.Reveal(pass, ServerRowPart.Signal);
        Unique(model);
        groups[3].SignalText = "latest ping";
        groups[0].IsSelectedCountry = true;
        model.Publish(Snapshot(groups), true, false);
        Check(ReferenceEquals(pass.Rows[0].Target, target) && model.Slots[0].SignalServer.SignalText != "latest ping",
            "progress-only publication preserves the frozen animation target");
        model.Finish(pass);
        Order(model, 4, 1, 2, 3, 5);
        Check(model.Slots[0].SignalServer.SignalText == "latest ping" && model.Slots[1].SelectionServer.IsSelectedCountry,
            "Finish refreshes frozen rows from the latest snapshot without another Publish");
        Check(model.TryBeginPass() == null, "progress-only update creates no follow-up sorting pass");
        Complete(model, groups, 2, false);
        Drain(model);
        Order(model, 4, 1, 3, 5, 2);
        Complete(model, groups, 3, true, 3, 4, 5, 2, 1);
        Drain(model);
        Order(model, 3, 4, 1, 5, 2);
        Check(model.Slots.Last().DisplayedServer.IsSelectable == false,
            "completed failures follow pending countries; successes use latest order");

        var pinnedGroups = Countries(5);
        pinnedGroups[2].IsLastSuccessfulConnection = true;
        pinnedGroups[2].IsSelectable = false;
        var pinned = Start(pinnedGroups);
        Order(pinned, 3, 1, 2, 4, 5);
        Complete(pinned, pinnedGroups, 5);
        Drain(pinned);
        Order(pinned, 3, 5, 1, 2, 4);
        Complete(pinned, pinnedGroups, 3, false);
        Drain(pinned);
        Order(pinned, 3, 5, 1, 2, 4);
        Check(pinned.Slots[0].BadgeServer.IsLastSuccessfulConnection,
            "checking or failed star remains first independently of completion eligibility");

        var more = Countries(6);
        var incremental = Start(more);
        Complete(incremental, more, 4);
        var first = incremental.TryBeginPass();
        incremental.Reveal(first, ServerRowPart.Name);
        Complete(incremental, more, 6, true, 6, 4, 2, 5, 1, 3);
        Check(incremental.TryBeginPass() == null && first.Rows[0].Target.Service.ID == 4,
            "a mid-pass completion coalesces without replacing the active target");
        incremental.Finish(first);
        var second = incremental.TryBeginPass();
        Check(second != null && second.Rows[0].Target.Service.ID == 6,
            "next TryBeginPass immediately consumes a completion queued during the preceding pass");
        incremental.Finish(second);
        Drain(incremental);
        Order(incremental, 6, 4, 1, 2, 3, 5);
        incremental.BeginCheckCycle();
        incremental.Publish(Snapshot(more, 5, 3, 2, 1, 4, 6), true);
        Check(incremental.TryBeginPass() == null, "new cycle forgets old completion eligibility without a sweep");
        Order(incremental, 6, 4, 1, 2, 3, 5);
        more[2].IsSelectable = true;
        more[2].IsChecking = false;
        incremental.MarkChecking(new ServerItem(more[2]).Identity);
        incremental.Publish(Snapshot(more), true, false);
        Check(incremental.TryBeginPass() == null, "canceled or partial checking never promotes an unfinished country");

        var duplicates = Countries(3);
        duplicates[0].IsChecking = false;
        var duplicate = new ServerItem(new GroupItem { Service = new Service { ID = 1 }, CountryName = "duplicate", IsSelectable = false });
        var dedup = new ServerListViewModel();
        var original = Snapshot(duplicates);
        dedup.Publish(new[] { original[0], duplicate, original[1], original[2] }, true);
        Check(dedup.Servers.Count == 3 && dedup.Servers[0].CountryName == "Country 1",
            "duplicate service keys keep the first canonical item and create only one slot");
        dedup.BeginCheckCycle();
        dedup.Publish(new[] { original[0], duplicate, original[1], original[2] }, true, true, original[0].Identity);
        duplicates[2].IsChecking = false;
        dedup.Publish(new[] { new ServerItem(duplicates[2]), original[0], duplicate, original[1] }, true, true, original[2].Identity);
        var duplicatePass = dedup.TryBeginPass();
        Check(duplicatePass != null, "repeated keys remain safe while planning a multi-row animation");
        dedup.Reveal(duplicatePass, ServerRowPart.Signal);
        dedup.Publish(new[] { new ServerItem(duplicates[2]), original[0], duplicate, original[1] }, true, false);
        dedup.Finish(duplicatePass);
        Drain(dedup);
        Order(dedup, 3, 1, 2);
        Unique(dedup);
        Check(dedup.Servers.Count == 3, "duplicate keys remain safe in Publish, PresentationOrder and Finish");
    }
}
