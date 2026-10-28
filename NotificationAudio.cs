namespace TunerFest;

public static class NotificationAudio
{
    public record Tone(string Code, string Title)
    {
        public override string ToString() => Title;
    }

    public static readonly Tone[] Tones =
    [
        new("Asterisk", "Мелодія завершення"),
        new("Exclamation", "Подвійний сигнал"),
        new("Beep", "Короткий дзвінок"),
        new("Hand", "Низький сигнал"),
    ];
    static readonly Dictionary<string, System.Media.SoundPlayer> players = [];

    public static byte[] Wave(string code)
    {
        double[] notes = code switch
        {
            "Exclamation" => [660, 660],
            "Beep" => [880],
            "Hand" => [440, 330, 220],
            _ => [523.25, 659.25, 783.99],
        };
        const int rate = 22050;
        double noteSeconds = code == "Beep" ? .3 : .24;
        int samples = (int)(rate * (notes.Length * noteSeconds + .15));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double time = (double)i / rate;
            int note = (int)(time / noteSeconds);
            double value = 0;
            if (note < notes.Length)
            {
                double t = time - note * noteSeconds;
                double envelope = Math.Min(1, t / .012) * Math.Max(0, 1 - t / (noteSeconds - .02));
                value =
                    .65
                    * envelope
                    * (Math.Sin(2 * Math.PI * notes[note] * t) + .12 * Math.Sin(4 * Math.PI * notes[note] * t))
                    / 1.12;
            }
            writer.Write((short)(value * short.MaxValue));
        }
        return stream.ToArray();
    }

    public static string? Play(bool enabled, string code)
    {
        if (!enabled)
            return null;
        try
        {
            if (!players.TryGetValue(code, out var player))
            {
                player = new System.Media.SoundPlayer(new MemoryStream(Wave(code), false));
                player.Load();
                players.Add(code, player);
            }
            player.Play();
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
