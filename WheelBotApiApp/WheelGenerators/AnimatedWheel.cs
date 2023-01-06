namespace WheelBotApiApp.WheelGenerators;

public class AnimatedWheel
{
    public AnimatedWheel(int selectedIndex, string result, Stream spinningAnimation)
    {
        SelectedIndex = selectedIndex;
        Result = result;
        SpinningAnimation = spinningAnimation;
    }

    public int SelectedIndex { get; set; }
    public string Result { get; set; }
    public Stream SpinningAnimation { get; set; }
}
