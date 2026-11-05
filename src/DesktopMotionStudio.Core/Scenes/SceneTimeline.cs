namespace DesktopMotionStudio.Core.Scenes;

public readonly record struct SceneFrame(double Scale, double Opacity, double RotationDegrees, bool Completed);

public sealed class SceneTimeline
{
    public ScenePlaybackDefinition Definition { get; }

    public SceneTimeline(ScenePlaybackDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsValid) throw new ArgumentException("Playback definition is not supported or valid.", nameof(definition));
        Definition = definition;
    }

    public SceneFrame Evaluate(TimeSpan elapsed, bool reducedMotion = false)
    {
        double time = Math.Clamp(elapsed.TotalMilliseconds, 0, Definition.DurationMilliseconds);
        bool completed = time >= Definition.DurationMilliseconds;
        if (reducedMotion) return new(1, 1, 0, completed);
        if (time == 0) return Frame(Definition.Keyframes[0], false);
        if (completed) return Frame(Definition.Keyframes[^1], true);
        int index = 0;
        while (index < Definition.Keyframes.Length - 2 && time >= Definition.Keyframes[index + 1].AtMilliseconds) index++;
        SceneKeyframe a = Definition.Keyframes[index], b = Definition.Keyframes[index + 1];
        double fraction = (time - a.AtMilliseconds) / (b.AtMilliseconds - a.AtMilliseconds);
        double eased = fraction * fraction * (3 - 2 * fraction);
        double Mix(double left, double right) => left + (right - left) * eased;
        return new(Mix(a.Scale, b.Scale), Mix(a.Opacity, b.Opacity), Mix(a.RotationDegrees, b.RotationDegrees), false);
    }

    private static SceneFrame Frame(SceneKeyframe keyframe, bool completed) => new(keyframe.Scale, keyframe.Opacity, keyframe.RotationDegrees, completed);
}
