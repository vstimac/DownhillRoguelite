using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// NPC voices and their subtitles: every voiced NPC line (the president, the šalteruša, HŽ) has its
/// recording, its words as recorded and an English translation; Options → Dialogue text picks the
/// subtitle's language without touching the voice (a Croatian voice with English chosen gets the
/// English subtitle), and the player's answers in those conversations follow it too. Uses its own
/// options file, so the player's options stay untouched.
/// </summary>
public partial class DialogueLanguageTests : Node
{
    private const string President = "res://narrative/president/data/npc_restsite_president.csv";
    private const string PresidentText = "res://narrative/president/data/player_text.csv";
    private const string Salterusa = "res://narrative/salterusa/data/npc_salterusa.csv";
    private const string Hz = "res://narrative/hz_putnik/data/npc_hz_putnik.csv";

    private readonly List<string> _failures = new();
    private readonly List<SpeechLine> _heard = new();

    public override async void _Ready()
    {
        GameOptions.UseFile("user://dialogue-language-tests.cfg");
        GameEvents.SpeechStarted += OnSpeech;

        CheckManifest("president", President, 57, needsAudioPath: false);
        CheckManifest("šalteruša", Salterusa, 97, needsAudioPath: true);
        CheckManifest("HŽ putnik", Hz, 23, needsAudioPath: true);

        VoiceManifest president = VoiceManifest.Load(President);
        VoiceLine first = president.Find("intro_001") ?? default;
        GameOptions.SetDialogueLanguage(DialogueLanguage.English);
        Check("English: a Croatian line's subtitle is its translation", first.Shown.StartsWith("Stop. Stop, please."), first.Shown);
        GameOptions.SetDialogueLanguage(DialogueLanguage.Croatian);
        Check("Hrvatski: the subtitle is the words as recorded", first.Shown.StartsWith("Stanite. Stanite"), first.Shown);
        VoiceLine untranslated = new("x", 1, "e", "calm", "Samo hrvatski.", "", "");
        GameOptions.SetDialogueLanguage(DialogueLanguage.English);
        Check("a line without a translation falls back to its own words", untranslated.Shown == "Samo hrvatski.", untranslated.Shown);

        // Said aloud: the subtitle follows the option, the voice file stays the recorded one.
        NpcVoice voice = new() { NpcId = "npc_restsite_president", SpeakerName = "President", Manifest = president, LogLines = false };
        AddChild(voice);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GameOptions.SetDialogueLanguage(DialogueLanguage.English);
        voice.Say("idle_03");
        string english = _heard.Count > 0 ? _heard[^1].Text : "";
        GameOptions.SetDialogueLanguage(DialogueLanguage.Croatian);
        voice.Say("idle_03");
        string croatian = _heard.Count > 1 ? _heard[^1].Text : "";
        Check("a spoken line's subtitle is English or Croatian as chosen", english == "They say the bottom is near. They've been saying it for years."
            && croatian == "Kažu da je dno blizu. To govore već godinama.", $"EN \"{english}\", HR \"{croatian}\"");
        voice.Skip();

        TextTable text = TextTable.Load(PresidentText);
        GameOptions.SetDialogueLanguage(DialogueLanguage.English);
        string who = text.Get("c_who");
        string cost = text.Format("c_shop_map", "cost", 3);
        GameOptions.SetDialogueLanguage(DialogueLanguage.Croatian);
        Check("the player's answers follow the option", who == "Who are you?" && text.Get("c_who") == "Tko si ti?" && cost == "Map of optimism (3 favour)",
            $"{who} / {text.Get("c_who")} / {cost}");
        Check("every answer and note of the president's has its English", MissingEnglish(PresidentText).Count == 0, string.Join(", ", MissingEnglish(PresidentText)));

        Narrative.Salterusa clerk = new();
        GameOptions.SetDialogueLanguage(DialogueLanguage.Croatian);
        string[] hr = clerk.ShownChoices;
        string promptHr = clerk.Prompt;
        GameOptions.SetDialogueLanguage(DialogueLanguage.English);
        string[] en = clerk.ShownChoices;
        Check("the šalteruša's choices come in both languages", hr.Length == en.Length && hr.Length == clerk.ChoiceTextsHr.Length && hr[4] == "Doviđenja." && en[4] == "Goodbye."
            && promptHr.Contains("šalteru") && clerk.Prompt.Contains("counter"), $"{hr[0]} / {en[0]}");
        clerk.Free();

        GameEvents.SpeechStarted -= OnSpeech;
        GameOptions.UseFile(GameOptions.DefaultPath);
        GD.Print(_failures.Count == 0 ? "DIALOGUE LANGUAGE TESTS: PASS" : "DIALOGUE LANGUAGE TESTS: FAIL (" + string.Join(", ", _failures) + ")");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void OnSpeech(SpeechLine line) => _heard.Add(line);

    /// <summary>Every line: an id, its words, an English translation, and (when it names one) a recording that loads.</summary>
    private void CheckManifest(string name, string path, int expected, bool needsAudioPath)
    {
        VoiceManifest manifest = VoiceManifest.Load(path);
        List<string> noWords = new(), noEnglish = new(), noAudio = new(), placeholders = new();
        foreach (VoiceLine line in manifest.Lines)
        {
            if (line.Text.Length == 0)
            {
                noWords.Add(line.Id);
            }
            if (line.TextEn.Length == 0)
            {
                noEnglish.Add(line.Id);
            }
            if (line.Text.StartsWith('['))
            {
                placeholders.Add(line.Id);
            }
            if (needsAudioPath && NpcVoice.Stream(line.AudioPath) == null)
            {
                noAudio.Add(line.Id);
            }
        }
        Check($"{name}: {expected} lines, each with words and an English translation", manifest.Count == expected && noWords.Count == 0 && noEnglish.Count == 0,
            $"{manifest.Count} lines, no words [{string.Join(", ", noWords)}], no English [{string.Join(", ", noEnglish)}]");
        if (needsAudioPath)
        {
            Check($"{name}: every line's recording loads", noAudio.Count == 0, $"missing [{string.Join(", ", noAudio)}]");
        }
        // Not a failure: lines still described rather than transcribed (they need listening).
        GD.Print($"NOTE  {name}: {placeholders.Count} of {manifest.Count} lines are descriptions in [brackets], not transcribed words yet");
    }

    private static List<string> MissingEnglish(string path)
    {
        List<string> missing = new();
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        string[] header = file.GetCsvLine();
        int english = System.Array.IndexOf(header, "text_en");
        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length >= 2 && row[0].Length > 0 && (english < 0 || english >= row.Length || row[english].Length == 0))
            {
                missing.Add(row[0]);
            }
        }
        return missing;
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
