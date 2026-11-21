namespace FestivalDerby.Driving
{
    /// <summary>
    /// Raw command for one step; nitroPressed is a caller-supplied pulse, not a held button.
    /// throttle is the drive axis in [-1,1]: 1 asks for full power, 0 releases the car and a
    /// negative value asks to slow down and then back up. Normalization belongs to the rules, not
    /// to the input adapter.
    /// </summary>
    public readonly struct CarInput
    {
        public readonly float steer;
        public readonly float throttle;
        public readonly bool brake;
        public readonly bool reverse;
        public readonly bool nitroPressed;

        /// <summary>
        /// The raw joystick sample this command came from, in [-1,1] and unshaped: no dead zone, no
        /// low-pass, no mapping. V2 reads the stick as a heading, and a heading cannot be recovered from a
        /// shaped steering error, so the caller publishes the sample it actually sampled and every consumer
        /// (including the server) runs the same mapper over it. Stays (0,0) for callers that only have a
        /// steering axis, which is exactly the signal "no stick, use steer".
        /// </summary>
        public readonly float stickX;

        /// <inheritdoc cref="stickX"/>
        public readonly float stickY;

        /// <summary>
        /// CTRL1: the GAS input in [0,1]. A button writes 0 or 1 today; the field is a float so an
        /// analog trigger can write the middle later without touching the pipeline. Only the
        /// GasButton scheme reads it, and it travels as a bit in the buttons byte (see InputCommand).
        /// </summary>
        public readonly float gas;

        public CarInput(float steer, float throttle, bool brake, bool reverse, bool nitroPressed)
        {
            this.steer = steer;
            this.throttle = throttle;
            this.brake = brake;
            this.reverse = reverse;
            this.nitroPressed = nitroPressed;
            this.stickX = 0f;
            this.stickY = 0f;
            this.gas = 0f;
        }

        /// <summary>
        /// Full command, including the raw stick. The pre-stick constructors stay valid and mean "no
        /// stick", which is what a bot or a test rig with a steering axis really has. gas stays 0 too:
        /// every caller written before CTRL1 came from a scheme whose pedal already lived elsewhere.
        /// </summary>
        public CarInput(float steer, float throttle, bool brake, bool reverse, bool nitroPressed,
            float stickX, float stickY)
        {
            this.steer = steer;
            this.throttle = throttle;
            this.brake = brake;
            this.reverse = reverse;
            this.nitroPressed = nitroPressed;
            this.stickX = stickX;
            this.stickY = stickY;
            this.gas = 0f;
        }

        /// <summary>Full command with the CTRL1 gas input, 0..1.</summary>
        public CarInput(float steer, float throttle, bool brake, bool reverse, bool nitroPressed,
            float stickX, float stickY, float gas)
        {
            this.steer = steer;
            this.throttle = throttle;
            this.brake = brake;
            this.reverse = reverse;
            this.nitroPressed = nitroPressed;
            this.stickX = stickX;
            this.stickY = stickY;
            this.gas = gas;
        }

        /// <summary>
        /// Every caller that predates the drive axis meant "drive", so the old shape keeps full
        /// throttle instead of silently turning into a coast. brake and reverse override the
        /// throttle anyway, and a caller that really wants a release has to use the five-argument
        /// form and say 0.
        /// </summary>
        public CarInput(float steer, bool brake, bool reverse, bool nitroPressed)
            : this(steer, 1f, brake, reverse, nitroPressed)
        {
        }

        /// <summary>The same command with the raw stick attached; used by the input adapter.</summary>
        public CarInput WithStick(float x, float y)
        {
            return new CarInput(steer, throttle, brake, reverse, nitroPressed, x, y, gas);
        }

        /// <summary>The same command with the CTRL1 gas input replaced; used by the input adapter.</summary>
        public CarInput WithGas(float value)
        {
            return new CarInput(steer, throttle, brake, reverse, nitroPressed, stickX, stickY, value);
        }
    }
}
