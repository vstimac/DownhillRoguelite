namespace DownhillRoguelite.Progression;

/// <summary>The steps of the first-run guide, in the order they are shown (each finished by a game event).</summary>
public enum GuideStep
{
    /// <summary>Follow the cairns downhill (done after the first few metres of descent).</summary>
    Walk,
    /// <summary>Pick up food or water.</summary>
    Gather,
    /// <summary>Read a found text.</summary>
    Read,
    /// <summary>Eat or drink something (shown once food or water is carried).</summary>
    EatDrink,
    /// <summary>Take a boon at the first station's fire.</summary>
    Boon,
    /// <summary>Talk to the traveller or take the job at the first station.</summary>
    Visit,
}
