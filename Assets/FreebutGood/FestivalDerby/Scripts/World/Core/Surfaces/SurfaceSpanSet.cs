using System;

namespace FestivalDerby.World
{
    /// <summary>
    /// Immutable, normalized surface profile. Input spans are clipped to the path, wrapped across
    /// the start line on a closed loop, sorted and resolved so a later authored span overwrites an
    /// earlier one where they overlap. Gaps fall back to the base surface.
    /// </summary>
    public sealed class SurfaceSpanSet
    {
        private readonly SurfaceSpan[] _spans;
        private readonly float _length;
        private readonly bool _closed;
        private readonly SurfaceId _base;

        public int Count
        {
            get { return _spans.Length; }
        }

        public SurfaceSpanSet(SurfaceSpan[] spans, float pathLength, bool closed, SurfaceId baseSurface)
        {
            _length = FinitePositive(pathLength);
            _closed = closed;
            _base = baseSurface;

            int inputCount = spans == null ? 0 : spans.Length;
            // A closed span crossing the start line becomes two spans, so inputCount * 2 is the cap.
            _spans = new SurfaceSpan[inputCount * 2];
            int used = 0;
            for (int i = 0; i < inputCount; i++)
            {
                SurfaceSpan span = spans[i];
                if (!Finite(span.StartDistance) || !Finite(span.EndDistance))
                    continue;

                span.Order = i;
                span.TransitionLength = FinitePositive(span.TransitionLength);
                if (closed && _length > 0f)
                    used = AddWrapped(span, used);
                else
                    used = AddClipped(span, used);
            }

            if (used != _spans.Length)
                Array.Resize(ref _spans, used);
            Array.Sort(_spans, Compare);
        }

        /// <summary>
        /// Resolves the surface at <paramref name="distance"/>. The winning span is the last
        /// authored one that covers the distance, which is what makes an overlap deterministic.
        /// </summary>
        public SurfaceBlend Evaluate(float distance)
        {
            if (_length <= 0f || _spans.Length == 0)
                return new SurfaceBlend(_base, _base, 0);

            float d = _closed ? Mod(distance, _length) : Clamp(distance, 0f, _length);
            int selected = -1;
            for (int i = 0; i < _spans.Length; i++)
            {
                if (d >= _spans[i].StartDistance && d < _spans[i].EndDistance
                    && (selected < 0 || _spans[i].Order > _spans[selected].Order))
                    selected = i;
            }

            if (selected < 0)
                return new SurfaceBlend(_base, _base, 0);

            SurfaceSpan span = _spans[selected];
            float transition = span.TransitionLength;

            // Entering the span: the previous surface fades in as the span's own surface takes over.
            if (transition > 0f && d - span.StartDistance < transition)
            {
                float weight = (d - span.StartDistance) / transition;
                return Mix(PreviousSurface(selected), span.SurfaceId, weight);
            }

            // Leaving the span: at the edge of the blend the span is fully in force and fades into
            // the following surface, so the span stays Primary and the neighbour is Secondary.
            if (transition > 0f && span.EndDistance - d <= transition)
            {
                float weight = (span.EndDistance - d) / transition;
                return Mix(span.SurfaceId, NextSurface(selected), 1f - weight);
            }

            return new SurfaceBlend(span.SurfaceId, span.SurfaceId, 0);
        }

        // ------------------------------------------------------------------ normalization

        /// <summary>Wraps a span into [0, length], splitting one that crosses the start line.</summary>
        private int AddWrapped(SurfaceSpan span, int used)
        {
            float start = Mod(span.StartDistance, _length);
            float end = Mod(span.EndDistance, _length);
            if (span.StartDistance <= span.EndDistance && span.EndDistance - span.StartDistance >= _length)
            {
                // The span covers the whole loop.
                span.StartDistance = 0f;
                span.EndDistance = _length;
                _spans[used++] = span;
            }
            else if (end < start || span.StartDistance > span.EndDistance)
            {
                // Crossing the start line: [start, length] plus [0, end].
                SurfaceSpan head = span;
                head.StartDistance = start;
                head.EndDistance = _length;
                _spans[used++] = head;
                span.StartDistance = 0f;
                span.EndDistance = end;
                if (end > 0f)
                    _spans[used++] = span;
            }
            else
            {
                span.StartDistance = start;
                span.EndDistance = end;
                if (end > start)
                    _spans[used++] = span;
            }

            return used;
        }

        /// <summary>Clips an open-path span to the path and drops the empty result.</summary>
        private int AddClipped(SurfaceSpan span, int used)
        {
            span.StartDistance = Clamp(span.StartDistance, 0f, _length);
            span.EndDistance = Clamp(span.EndDistance, 0f, _length);
            if (span.EndDistance > span.StartDistance)
                _spans[used++] = span;
            return used;
        }

        // ------------------------------------------------------------------ queries

        private SurfaceId PreviousSurface(int index)
        {
            return index > 0 && _spans[index - 1].EndDistance == _spans[index].StartDistance
                ? _spans[index - 1].SurfaceId
                : _base;
        }

        private SurfaceId NextSurface(int index)
        {
            return index + 1 < _spans.Length && _spans[index + 1].StartDistance == _spans[index].EndDistance
                ? _spans[index + 1].SurfaceId
                : _base;
        }

        private static SurfaceBlend Mix(SurfaceId from, SurfaceId to, float weight)
        {
            if (from == to)
                return new SurfaceBlend(to, to, 0);
            return new SurfaceBlend(from, to, (byte)(Clamp(weight, 0f, 1f) * 255f + 0.5f));
        }

        private static int Compare(SurfaceSpan a, SurfaceSpan b)
        {
            int byStart = a.StartDistance.CompareTo(b.StartDistance);
            return byStart != 0 ? byStart : a.EndDistance.CompareTo(b.EndDistance);
        }

        // ------------------------------------------------------------------ float helpers

        private static bool Finite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        private static float FinitePositive(float f)
        {
            return Finite(f) && f > 0f ? f : 0f;
        }

        private static float Clamp(float f, float min, float max)
        {
            return f < min ? min : f > max ? max : f;
        }

        private static float Mod(float f, float length)
        {
            float remainder = f % length;
            return remainder < 0f ? remainder + length : remainder;
        }
    }
}
