using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The restsite president's conversations as a graph of beats (docs/restsite-president.md). Only
/// line ids from the manifest and choice ids from player_text.csv appear here; no words. Where a
/// conversation starts follows the priority: a quest ready to hand in, the first meeting, a special
/// moment (back after a death, low health, a heavy pack, a stalled quest), a quest on offer, then
/// small talk. Every action goes through the host (state, rewards, saving).
/// </summary>
public sealed class PresidentDialogue
{
    private readonly IPresidentHost _host;
    private readonly VoiceManifest _manifest;
    private readonly RandomNumberGenerator _random = new();
    private string _lastIdle = "";

    /// <summary>Lines also said as idle remarks: "a man of few words" fits a carrier standing silently by (the voice pack files it as idle).</summary>
    public string[] ExtraIdleLines { get; set; } = { "q1_end_silent" };

    public PresidentDialogue(IPresidentHost host, VoiceManifest manifest, ulong seed = 0)
    {
        _host = host;
        _manifest = manifest;
        if (seed == 0)
        {
            _random.Randomize();
        }
        else
        {
            _random.Seed = seed;
        }
    }

    private PresidentSave Save => _host.Save;

    /// <summary>Where a talk starts (E at the president).</summary>
    public DialogueBeat Begin()
    {
        if (Save.Quest(PresidentSave.QuestResults) == QuestStatus.Ready)
        {
            return ResultsReturn();
        }
        if (Save.Quest(PresidentSave.QuestContractors) == QuestStatus.Ready)
        {
            return ContractorsReturn();
        }
        if (Save.Quest(PresidentSave.QuestVictory) == QuestStatus.Ready)
        {
            return VictoryReturn();
        }
        if (!Save.Met)
        {
            return Intro();
        }
        string special = SpecialLine();
        if (special.Length > 0)
        {
            return Hub(special);
        }
        DialogueBeat? offer = Offer();
        if (offer != null)
        {
            return offer;
        }
        return Hub(IdleLine());
    }

    /// <summary>The first meeting: two lines, then the four questions (plus "but I'm descending").</summary>
    public DialogueBeat Intro()
    {
        DialogueBeat beat = new("intro_001", "intro_002");
        beat.Then(_host.MarkMet);
        AddIntroChoices(beat, true);
        return beat;
    }

    private void AddIntroChoices(DialogueBeat beat, bool descending)
    {
        if (descending)
        {
            beat.Option(Choice("c_descending", () => IntroAgain("intro_004", false)));
        }
        beat.Option(Choice("c_who", () => IntroAgain2(descending, "intro_a2", "intro_a3")));
        beat.Option(Choice("c_rest", () => new DialogueBeat("intro_b2").Then(_host.Rest)));
        beat.Option(Choice("c_job", () => ResultsOffer("intro_c2")));
        beat.Option(Choice("c_leave", () => new DialogueBeat("intro_d2")));
    }

    private DialogueBeat IntroAgain(string line, bool descending)
    {
        DialogueBeat beat = new(line);
        AddIntroChoices(beat, descending);
        return beat;
    }

    private DialogueBeat IntroAgain2(bool descending, params string[] lines)
    {
        DialogueBeat beat = new(lines);
        AddIntroChoices(beat, descending);
        return beat;
    }

    /// <summary>The usual menu after a greeting line (or none).</summary>
    public DialogueBeat Hub(params string[] lines)
    {
        DialogueBeat beat = new(lines);
        beat.Option(Choice("c_who", () => Hub("intro_a2", "intro_a3")));
        beat.Option(Choice("c_rest", () => new DialogueBeat("rest_001").Then(_host.Rest)));
        beat.Option(Choice("c_shop", () => Shop(true, false)));
        if (OfferableQuest().Length > 0)
        {
            beat.Option(Choice("c_job", () => Offer() ?? Hub()));
        }
        if (SecretOpen)
        {
            beat.Option(Choice("c_secret", Secret));
        }
        beat.Option(Choice("c_leave", Leave));
        return beat;
    }

    private DialogueBeat Leave()
    {
        if (Save.SecretSeen && _manifest.Has("farewell_final_01"))
        {
            return new DialogueBeat("farewell_final_01");
        }
        return new DialogueBeat("leave_01");
    }

    /// <summary>A moment worth remarking on, in order: back after a death, low health, a heavy pack, back for another visit, quest 2 stalled. Empty if none.</summary>
    public string SpecialLine()
    {
        if (_host.DeathGreetingPending)
        {
            _host.MarkDeathGreeted();
            _host.MarkReturnGreeted();
            if (Save.Returns >= 5)
            {
                return "return_after_death_03";
            }
            return "return_after_death_02";
        }
        if (_host.LowHealth)
        {
            return "low_health_01";
        }
        if (_host.HeavyLoad)
        {
            return "heavy_inventory_01";
        }
        if (_host.ReturnGreetingPending)
        {
            // "Opet ste tu?": the first word on a visit after the first meeting.
            _host.MarkReturnGreeted();
            return "return_after_death_01";
        }
        if (_host.ContractorsReminderPending)
        {
            _host.MarkContractorsReminded();
            return "q2_fail";
        }
        return "";
    }

    /// <summary>The next quest he can offer (one at a time, in order), or empty.</summary>
    public string OfferableQuest()
    {
        if (Save.Quest(PresidentSave.QuestResults) == QuestStatus.NotStarted)
        {
            return PresidentSave.QuestResults;
        }
        if (Save.Quest(PresidentSave.QuestResults) != QuestStatus.Completed)
        {
            return "";
        }
        if (Save.Quest(PresidentSave.QuestContractors) == QuestStatus.NotStarted)
        {
            return PresidentSave.QuestContractors;
        }
        if (Save.Quest(PresidentSave.QuestContractors) != QuestStatus.Completed)
        {
            return "";
        }
        if (Save.Quest(PresidentSave.QuestVictory) == QuestStatus.NotStarted)
        {
            return PresidentSave.QuestVictory;
        }
        return "";
    }

    private DialogueBeat? Offer()
    {
        switch (OfferableQuest())
        {
            case PresidentSave.QuestResults:
                return ResultsOffer();
            case PresidentSave.QuestContractors:
                return ContractorsOffer();
            case PresidentSave.QuestVictory:
                return VictoryOffer();
            default:
                return null;
        }
    }

    /// <summary>The secret question shows after five returns, with all three quests done, once.</summary>
    public bool SecretOpen => Save.Returns >= 5 && Save.AllQuestsDone && !Save.SecretSeen;

    // Quest 1: "Nema konačnih rezultata".

    public DialogueBeat ResultsOffer(string lead = "")
    {
        List<string> lines = new();
        if (lead.Length > 0)
        {
            lines.Add(lead);
        }
        lines.Add("q1_offer_01");
        lines.Add("q1_offer_03");
        DialogueBeat beat = new(lines.ToArray());
        beat.Option(Choice("c_accept", () => new DialogueBeat("q1_accept").Then(_host.AcceptResults)));
        beat.Option(Choice("c_decline", () => new DialogueBeat("q1_decline")));
        return beat;
    }

    public DialogueBeat ResultsReturn()
    {
        DialogueBeat beat = new("q1_return_01", "q1_return_02", "q1_return_04");
        beat.Then(_host.HandInResults);
        foreach ((string choice, string line, string ending) in new[]
        {
            ("c_truth", "q1_end_truth", "truth"),
            ("c_agree", "q1_end_agree", "agree"),
            ("c_silent", "q1_end_silent", "silent"),
        })
        {
            string end = ending;
            beat.Option(Choice(choice, () => new DialogueBeat(line).Then(() => _host.FinishResults(end))));
        }
        return beat;
    }

    // Quest 2: "Samo izvođači radova".

    public DialogueBeat ContractorsOffer()
    {
        DialogueBeat beat = new("q2_offer_01", "q2_offer_03", "q2_offer_05");
        beat.Option(Choice("c_accept", () => new DialogueBeat("q2_accept").Then(_host.AcceptContractors)));
        beat.Option(Choice("c_decline", () => Hub()));
        return beat;
    }

    public DialogueBeat ContractorsReturn()
    {
        return new DialogueBeat("q2_complete").Then(_host.FinishContractors);
    }

    // Quest 3: "Istorijska pobeda".

    public DialogueBeat VictoryOffer()
    {
        DialogueBeat beat = new("q3_offer_01", "q3_offer_03");
        beat.Option(Choice("c_accept", () =>
        {
            if (_host.AcceptVictory())
            {
                return new DialogueBeat("q3_accept");
            }
            return Hub();
        }));
        beat.Option(Choice("c_decline", () => Hub()));
        return beat;
    }

    public DialogueBeat VictoryReturn()
    {
        string line = "q3_complete";
        if (_host.VictoryTorn)
        {
            line = "q3_broken";
        }
        return new DialogueBeat(line).Then(_host.FinishVictory);
    }

    // The shop "Privremene mere": paid in favour (the game has no money).

    public DialogueBeat Shop(bool opening, bool bought)
    {
        int cheapest = int.MaxValue;
        for (int i = 0; i < _host.ShopCount; i++)
        {
            cheapest = Mathf.Min(cheapest, _host.ShopCost(i));
        }
        if (opening && Save.Favor < cheapest)
        {
            return Hub("no_money_01");
        }
        DialogueBeat beat = new();
        if (opening)
        {
            beat.Lines.Add("shop_001");
        }
        for (int i = 0; i < _host.ShopCount; i++)
        {
            int index = i;
            if (_host.ShopCost(i) > Save.Favor)
            {
                continue;
            }
            string id = _host.ShopTextId(i);
            beat.Option(new DialogueOption(id, _host.Text.Format(id, "cost", _host.ShopCost(i)), () =>
            {
                bool done = _host.Buy(index);
                return Shop(false, bought || done);
            }));
        }
        beat.Option(Choice("c_shop_done", () =>
        {
            if (bought)
            {
                return Hub();
            }
            return Hub("shop_002");
        }));
        return beat;
    }

    // The podium: one speech per run.

    /// <summary>At the podium: speak (buff) or not. Null when the speech was already given this run.</summary>
    public DialogueBeat? Podium()
    {
        if (_host.PodiumUsed)
        {
            return null;
        }
        DialogueBeat beat = new();
        beat.Option(Choice("c_podium_speak", () => new DialogueBeat("podium_buff_01").Then(() => _host.Podium(true))));
        beat.Option(Choice("c_podium_skip", () => new DialogueBeat("podium_skip_01").Then(() => _host.Podium(false))));
        return beat;
    }

    // The secret scene: his lines only from the manifest, the player's questions in between.

    public DialogueBeat Secret()
    {
        DialogueBeat last = new DialogueBeat("secret_aftermath_01").Then(_host.FinishSecret);
        DialogueBeat seven = new DialogueBeat("secret_07").Option(Choice("c_secret_meet", () => last));
        DialogueBeat six = new DialogueBeat("secret_06").Option(Choice("c_secret_why", () => seven));
        DialogueBeat four = new DialogueBeat("secret_04").Option(Choice("c_secret_back", () => six));
        return new DialogueBeat("secret_02").Option(Choice("c_secret_down", () => four));
    }

    /// <summary>Small talk for the hub and idle remarks: an idle line (late in the descent also the melancholic ones), never the same twice running.</summary>
    public string IdleLine()
    {
        List<VoiceLine> pool = new(_manifest.ForEvent("idle"));
        foreach (string id in ExtraIdleLines)
        {
            if (_manifest.Find(id) is VoiceLine extra)
            {
                pool.Add(extra);
            }
        }
        if (_host.Late)
        {
            pool.AddRange(_manifest.ForEvent("late_game"));
        }
        if (pool.Count == 0)
        {
            return "";
        }
        string pick = pool[_random.RandiRange(0, pool.Count - 1)].Id;
        if (pick == _lastIdle && pool.Count > 1)
        {
            pick = pool[(pool.FindIndex(line => line.Id == pick) + 1) % pool.Count].Id;
        }
        _lastIdle = pick;
        return pick;
    }

    private DialogueOption Choice(string id, System.Func<DialogueBeat?> next)
    {
        return new DialogueOption(id, _host.Text.Get(id), next);
    }
}
