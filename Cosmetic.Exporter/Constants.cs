namespace Cosmetic.Exporter
{
    public static class Constants
    {
        public static readonly string BasePath = Directory.GetCurrentDirectory(); //EXE Path

        public static readonly string DataPath = $"{BasePath}\\.data"; //store shit
        public static readonly string BlinkaExe = $"{DataPath}\\binkadec.exe"; //emotes

        public static readonly string ExportPath = $"{BasePath}\\Export";
    }
}
