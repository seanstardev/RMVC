public sealed class RVoid
{
    private RVoid()
    {
    }

    public static RVoid Value { get; } = new RVoid();

    public override string ToString() => nameof(RVoid);
}