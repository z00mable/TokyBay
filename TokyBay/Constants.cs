using Spectre.Console;

namespace TokyBay
{
    public static class Constants
    {
        public static void ShowHeader()
        {
            AnsiConsole.Write(
                new FigletText(FigletFont.Load("Bulbhead.flf"), "TokyBay")
                    .LeftJustified()
                    .Color(Color.Red));
            AnsiConsole.WriteLine();
        }
    }
}
