namespace FestivalDerby.Match
{
    /// <summary>How a run is won or lost. Values are stable: they may be serialized in scenes.</summary>
    public enum MatchMode
    {
        /// <summary>No rules: just drive. What the car core always was.</summary>
        FreeDrive = 0,

        /// <summary>Catch the truck: first to stay on its bed wins, the last car is eliminated on a clock.</summary>
        TruckChase = 1,

        /// <summary>A classic race: first to finish the chosen number of laps wins.</summary>
        Laps = 2
    }
}
