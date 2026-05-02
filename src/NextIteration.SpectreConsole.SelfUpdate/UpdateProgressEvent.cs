namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// A stage-level progress event emitted by an <see cref="IUpdateInstaller"/>
    /// during <see cref="IUpdateInstaller.InstallAsync"/>.
    /// </summary>
    /// <param name="Stage">Which pipeline stage produced the event.</param>
    /// <param name="PercentComplete">
    /// Optional 0–1 fractional progress value within the stage. Stages that
    /// don't produce intra-stage progress (e.g. <see cref="UpdateStage.Verifying"/>
    /// for a small file) emit <see langword="null"/> and exactly two events:
    /// one at the start and one at the end.
    /// </param>
    public sealed record UpdateProgressEvent(UpdateStage Stage, double? PercentComplete);
}
