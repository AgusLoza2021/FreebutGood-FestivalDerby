namespace FestivalDerby.Driving.Handling
{
    /// <summary>
    /// A four-sample piecewise-linear curve stored as plain floats inside the struct. The handling core
    /// has to run in the engine-free assembly on the fixed server tick, so an AnimationCurve is not an
    /// option (no UnityEngine, and sampling one is not guaranteed to be identical across platforms). A
    /// float[] would either allocate per tick or force the caller to keep arrays alive and aliased.
    /// Four samples cover every curve the model needs: the throttle curve and the two axle slip curves.
    /// </summary>
    public readonly struct Piecewise4
    {
        public readonly float X0;
        public readonly float Y0;
        public readonly float X1;
        public readonly float Y1;
        public readonly float X2;
        public readonly float Y2;
        public readonly float X3;
        public readonly float Y3;

        public Piecewise4(
            float x0, float y0,
            float x1, float y1,
            float x2, float y2,
            float x3, float y3)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            X3 = x3;
            Y3 = y3;
        }

        /// <summary>
        /// Linear interpolation with the two ends held flat outside the sample range. Only +, - and *
        /// are used, so every caller on every platform gets the same bits for the same input.
        /// </summary>
        public float Evaluate(float x)
        {
            if (x <= X0)
            {
                return Y0;
            }

            if (x >= X3)
            {
                return Y3;
            }

            if (x <= X1)
            {
                return Sample(X0, Y0, X1, Y1, x);
            }

            if (x <= X2)
            {
                return Sample(X1, Y1, X2, Y2, x);
            }

            return Sample(X2, Y2, X3, Y3, x);
        }

        private static float Sample(float x0, float y0, float x1, float y1, float x)
        {
            float span = x1 - x0;
            if (span == 0f)
            {
                return y1;
            }

            return y0 + (y1 - y0) * ((x - x0) / span);
        }
    }
}
