namespace Synthetic;

public class Base
{
    public int baseCount;
}

public interface ILabelled
{
    string Label { get; }
}

public sealed class Target : Base, ILabelled
{
    public static int total;
    private readonly string label = "";

    public string Label => label;

    public static int Run(int count) => count + total;

    public string Describe(int count, ref float scale) => label + count + scale;

    public void Act(Base other) => baseCount += other.baseCount;
}

public struct Cell
{
    public int x;

    public readonly int Area() => x * x;
}
