namespace BehavioralAuthSystem.Models;

public class BehavioralCapture
{
    public string PatternType { get; set; } = string.Empty;

    // Mouse Click pattern
    public string ClickType { get; set; } = string.Empty;
    public int ClickCount { get; set; }

    // Temporary click data used only while capturing.
    // It is NOT saved as the behavioral secret.
    public List<BehavioralPoint> Clicks { get; set; } = new();

    // Shape
    public string ShapeType { get; set; } = string.Empty;

    // Temporary path used only to classify the shape.
    // It is NOT saved in the enrolled pattern.
    public List<BehavioralPoint> Path { get; set; } = new();

    // Keyboard shortcut
    public ShortcutCapture Shortcut { get; set; } = new();

    // Sequential keyboard pattern
    public List<string> KeySequence { get; set; } = new();

    // Legacy property retained so old JSON can deserialize safely.
    public BehavioralPoint? Tap { get; set; }
}

public class BehavioralPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double T { get; set; }
}

public class ShortcutCapture
{
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Meta { get; set; }
    public string Key { get; set; } = string.Empty;
}