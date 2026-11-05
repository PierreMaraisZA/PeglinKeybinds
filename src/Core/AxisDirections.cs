namespace PeglinKeybinds.Core
{
    /// <summary>
    /// How a binding on a stick axis is divided between the two directions of an action. A binding can cover the
    /// whole axis (both directions) or just one half of it, and an inverted axis has its halves the other way round.
    /// These rules live here, away from Rewired, so they can be tested without a controller plugged in.
    /// </summary>
    public static class AxisDirections
    {
        /// <summary>
        /// Which way the stick is pushed to drive the action's given direction: true for the positive half. A whole
        /// axis drives the positive direction when it isn't inverted, and the negative direction when it is; a
        /// binding already split onto one half is triggered by that half.
        /// </summary>
        public static bool PushedPositive(bool wholeAxis, bool positiveHalf, bool inverted, bool rowPositive)
        {
            return wholeAxis ? rowPositive != inverted : positiveHalf;
        }

        /// <summary>
        /// When one direction is cleared from a whole-axis binding, the half the other direction stays on: true for
        /// the positive half. On a normal stick the opposite direction sits on the opposite half; inverting the axis
        /// swaps them.
        /// </summary>
        public static bool OtherHalfPositive(bool rowPositive, bool inverted)
        {
            return rowPositive == inverted;
        }

        /// <summary>
        /// Whether a push on an axis is already what the row has, so adding it again would only duplicate it. A
        /// whole-axis binding already covers both directions; a split one only covers its own half.
        /// </summary>
        public static bool SamePush(bool wholeAxis, bool positiveHalf, bool positivePush)
        {
            return wholeAxis || positiveHalf == positivePush;
        }
    }
}
