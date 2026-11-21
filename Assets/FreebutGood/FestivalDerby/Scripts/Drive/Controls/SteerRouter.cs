using FestivalDerby.Driving.Runtime;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// Hands the driving controls whichever source is being used right now, in priority order. A source
    /// with no input is skipped, so touching the stick and pressing a key never fight.
    /// </summary>
    public sealed class SteerRouter : ISteerSource
    {
        private readonly ISteerSource[] _sources;

        public SteerRouter(params ISteerSource[] sources)
        {
            _sources = sources;
        }

        public float Horizontal
        {
            get
            {
                ISteerSource source = Active();
                return source != null ? source.Horizontal : 0f;
            }
        }

        public float Vertical
        {
            get
            {
                ISteerSource source = Active();
                return source != null ? source.Vertical : 0f;
            }
        }

        public bool HasInput
        {
            get { return Active() != null; }
        }

        private ISteerSource Active()
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                ISteerSource source = _sources[i];
                if (source != null && source.HasInput)
                    return source;
            }

            return null;
        }
    }
}
